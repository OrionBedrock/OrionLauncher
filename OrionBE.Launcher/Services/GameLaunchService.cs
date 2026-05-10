using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using OrionBE.Launcher.Core;
using OrionBE.Launcher.I18n;
namespace OrionBE.Launcher.Services;

public sealed class GameLaunchService : IGameLaunchService
{
    /// <summary>
    /// Spacewar — Valve's conventional placeholder Steam App ID for Proton outside the Steam client.
    /// Without a numeric ID, Proton often logs <c>SteamGameId: default</c>.
    /// </summary>
    private const string NonSteamProtonPlaceholderAppId = "480";

    private static readonly Lock ActiveLaunchesLock = new();
    private static readonly HashSet<string> ActiveLaunches = new(StringComparer.OrdinalIgnoreCase);

    private readonly IInstanceService _instanceService;
    private readonly IVcRuntimeService _vcRuntimeService;
    private readonly IUiDialogService _uiDialogs;
    private readonly ILauncherSettingsService _launcherSettings;
    private readonly ILogger<GameLaunchService> _logger;

    public GameLaunchService(
        IInstanceService instanceService,
        IVcRuntimeService vcRuntimeService,
        IUiDialogService uiDialogs,
        ILauncherSettingsService launcherSettings,
        ILogger<GameLaunchService> logger)
    {
        _instanceService = instanceService;
        _vcRuntimeService = vcRuntimeService;
        _uiDialogs = uiDialogs;
        _launcherSettings = launcherSettings;
        _logger = logger;
    }

    private void RememberLastPlayed(string instanceFolderName)
    {
        try
        {
            var s = _launcherSettings.Load();
            s.LastPlayedInstanceFolderName = instanceFolderName;
            _launcherSettings.Save(s);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not persist last-played instance.");
        }
    }

    public bool IsInstanceRunning(string instanceFolderName)
    {
        lock (ActiveLaunchesLock)
        {
            if (ActiveLaunches.Contains(instanceFolderName))
            {
                return true;
            }
        }

        return TryDetectExistingGameProcess(instanceFolderName);
    }

    public async Task LaunchInstanceAsync(string instanceFolderName, CancellationToken cancellationToken = default)
    {
        lock (ActiveLaunchesLock)
        {
            if (!ActiveLaunches.Add(instanceFolderName))
            {
                throw new InvalidOperationException("Launch is already in progress for this instance.");
            }
        }

        try
        {
            if (TryDetectExistingGameProcess(instanceFolderName))
            {
                throw new InvalidOperationException("This instance is already running.");
            }

        var summary = await _instanceService.GetAsync(instanceFolderName, cancellationToken).ConfigureAwait(false);
        if (summary is null)
        {
            throw new InvalidOperationException("Instance not found.");
        }

        var gameRoot = OrionPaths.InstanceGame(instanceFolderName);
        var canonicalExe = BedrockGameLayout.FindWindowsExecutable(gameRoot);

        string exe;
        if (canonicalExe is not null)
        {
            exe = canonicalExe;
            if (!string.Equals(summary.Config.BedrockWindowsExecutablePath, canonicalExe, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Updating BedrockWindowsExecutablePath to canonical executable: {Exe}",
                    canonicalExe);
                summary.Config.BedrockWindowsExecutablePath = canonicalExe;
                await _instanceService.SaveConfigAsync(instanceFolderName, summary.Config, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            exe = summary.Config.BedrockWindowsExecutablePath ?? "";
        }

        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            throw new InvalidOperationException(
                "Game executable not found. Reinstall the instance or check the game folder.");
        }

        var workDir = Path.GetDirectoryName(exe);
        if (string.IsNullOrEmpty(workDir))
        {
            throw new InvalidOperationException("Invalid executable path.");
        }

        await _vcRuntimeService.EnsureForGameAsync(gameRoot, exe, cancellationToken)
            .ConfigureAwait(false);

        EnsureMultiplayerSafetyWarningBypass(gameRoot);

        if (OperatingSystem.IsLinux())
        {
            var umu = summary.Config.LinuxUmuRunPath;
            var proton = summary.Config.LinuxProtonPath;
            if (string.IsNullOrWhiteSpace(umu) || !File.Exists(umu))
            {
                throw new InvalidOperationException(
                    "umu-run is not configured. Open instance settings or reinstall on Linux to obtain the GDK runtime.");
            }

            if (string.IsNullOrWhiteSpace(proton) || !Directory.Exists(proton))
            {
                throw new InvalidOperationException(
                    "PROTONPATH (GDK Proton) is not configured. Reinstall the instance on Linux.");
            }

            var psi = new ProcessStartInfo
            {
                FileName = umu,
                UseShellExecute = false,
                WorkingDirectory = workDir,
            };
            psi.ArgumentList.Add(exe);
            psi.Environment["PROTONPATH"] = proton;
            if (!string.IsNullOrWhiteSpace(summary.Config.LinuxWinePrefixPath))
            {
                psi.Environment["WINEPREFIX"] = summary.Config.LinuxWinePrefixPath!;
            }

            ApplyLinuxProtonBaselineEnvironment(psi);

            // After all env mutations: ensure umu is invoked with Steam identity on argv when possible.
            ConfigureLinuxUmuInvocation(psi, umu, exe);

            var process = Process.Start(psi);
            if (process is null)
            {
                throw new InvalidOperationException("Could not start umu-run (Process.Start returned null).");
            }

            AttachLinuxUmuExitNotifier(instanceFolderName, process);

            _logger.LogInformation("Game started via umu-run: {Exe}, pid={Pid}", exe, process.Id);
            RememberLastPlayed(instanceFolderName);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = workDir,
                    UseShellExecute = true,
                });
            _logger.LogInformation("Game started (Windows): {Exe}", exe);
            RememberLastPlayed(instanceFolderName);
            return;
        }

        throw new PlatformNotSupportedException("Bedrock launch is only implemented for Linux and Windows.");
        }
        finally
        {
            lock (ActiveLaunchesLock)
            {
                ActiveLaunches.Remove(instanceFolderName);
            }
        }
    }

    private void EnsureMultiplayerSafetyWarningBypass(string gameRoot)
    {
        var optionsPath = Path.Combine(
            gameRoot,
            "Minecraft Bedrock",
            "Users",
            "Shared",
            "games",
            "com.mojang",
            "minecraftpe",
            "options.txt");

        try
        {
            MinecraftOptionsPatcher.EnsureSafetyWarningDisabledInFile(optionsPath);
            _logger.LogInformation(
                "Ensured multiplayer online safety warning is disabled in options.txt: {Path}",
                optionsPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update options.txt at {Path}", optionsPath);
        }
    }

    private static bool TryDetectExistingGameProcess(string instanceFolderName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return TryDetectExistingGameProcessLinux(instanceFolderName);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return TryDetectExistingGameProcessWindows(instanceFolderName);
        }

        return false;
    }

    private static bool TryDetectExistingGameProcessLinux(string instanceFolderName)
    {
        var needle = $"instances/{instanceFolderName}";
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var cmdlinePath = $"/proc/{process.Id}/cmdline";
                if (!File.Exists(cmdlinePath))
                {
                    continue;
                }

                var raw = File.ReadAllBytes(cmdlinePath);
                if (raw.Length == 0)
                {
                    continue;
                }

                var cmdline = Encoding.UTF8.GetString(raw).Replace('\0', ' ');
                if (!cmdline.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (cmdline.Contains("Minecraft.Windows", StringComparison.OrdinalIgnoreCase)
                    || cmdline.Contains("umu-run", StringComparison.OrdinalIgnoreCase)
                    || cmdline.Contains("wine", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Process may exit while scanning.
            }
        }

        return false;
    }

    private static bool TryDetectExistingGameProcessWindows(string instanceFolderName)
    {
        var gameRoot = Path.GetFullPath(OrionPaths.InstanceGame(instanceFolderName));
        try
        {
            foreach (var proc in Process.GetProcessesByName("Minecraft.Windows"))
            {
                try
                {
                    string path;
                    try
                    {
                        path = proc.MainModule?.FileName ?? "";
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    if (IsPathUnderDirectory(Path.GetFullPath(path), gameRoot))
                    {
                        return true;
                    }
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsPathUnderDirectory(string filePath, string directoryRoot)
    {
        try
        {
            var root = Path.GetFullPath(directoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var file = Path.GetFullPath(filePath);
            return file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || file.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(file, root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyLinuxProtonBaselineEnvironment(ProcessStartInfo psi)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // STEAM_COMPAT_APP_ID is what pressure-vessel / some Proton entry points read in addition
        // to SteamAppId/SteamGameId.
        var id = NonSteamProtonPlaceholderAppId;
        psi.Environment["SteamAppId"] = id;
        psi.Environment["SteamGameId"] = id;
        psi.Environment["STEAM_COMPAT_APP_ID"] = id;
    }

    /// <summary>
    /// umu/pressure-vessel may not forward the full parent environment; prefixing
    /// <c>/usr/bin/env SteamAppId=… SteamGameId=… STEAM_COMPAT_APP_ID=… umu-run …</c> forces
    /// the identity on the direct child so Proton logs show a numeric SteamGameId.
    /// </summary>
    private static void ConfigureLinuxUmuInvocation(ProcessStartInfo psi, string umuPath, string exePath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        const string envBinary = "/usr/bin/env";
        var id = NonSteamProtonPlaceholderAppId;

        if (File.Exists(envBinary))
        {
            psi.FileName = envBinary;
            psi.ArgumentList.Clear();
            psi.ArgumentList.Add($"SteamAppId={id}");
            psi.ArgumentList.Add($"SteamGameId={id}");
            psi.ArgumentList.Add($"STEAM_COMPAT_APP_ID={id}");
            psi.ArgumentList.Add(umuPath);
            psi.ArgumentList.Add(exePath);
        }
        else
        {
            psi.FileName = umuPath;
            psi.ArgumentList.Clear();
            psi.ArgumentList.Add(exePath);
        }
    }

    private void AttachLinuxUmuExitNotifier(string instanceFolderName, Process process)
    {
        try
        {
            var notified = 0;
            void NotifyIfFailed()
            {
                try
                {
                    process.Refresh();
                    if (!process.HasExited)
                    {
                        return;
                    }

                    var code = process.ExitCode;
                    if (code == 0)
                    {
                        return;
                    }

                    if (Interlocked.Exchange(ref notified, 1) != 0)
                    {
                        return;
                    }

                    _logger.LogWarning(
                        "umu-run exited with non-zero code {Code} (instance {Instance})",
                        code,
                        instanceFolderName);
                    _ = _uiDialogs.ShowMessageAsync(
                        Localizer.Instance["app_brand"],
                        Localizer.Instance.Format("dialogs_umu_exit_body", code));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Linux umu exit notifier failed.");
                }
            }

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => NotifyIfFailed();
            NotifyIfFailed();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AttachLinuxUmuExitNotifier failed.");
        }
    }
}
