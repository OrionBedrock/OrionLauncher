using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using OrionBE.Launcher.Core;

namespace OrionBE.Launcher.Services;

public sealed class GameLaunchService : IGameLaunchService
{
    private static readonly Lock ActiveLaunchesLock = new();
    private static readonly HashSet<string> ActiveLaunches = new(StringComparer.OrdinalIgnoreCase);

    private readonly IInstanceService _instanceService;
    private readonly IVcRuntimeService _vcRuntimeService;
    private readonly ILogger<GameLaunchService> _logger;

    public GameLaunchService(
        IInstanceService instanceService,
        IVcRuntimeService vcRuntimeService,
        ILogger<GameLaunchService> logger)
    {
        _instanceService = instanceService;
        _vcRuntimeService = vcRuntimeService;
        _logger = logger;
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

        return TryDetectExistingInstanceProcess(instanceFolderName);
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
            if (TryDetectExistingInstanceProcess(instanceFolderName))
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

            ApplyLinuxCompatibilityEnvironment(summary.Config, psi);
            if (summary.Config.CollectLaunchDiagnostics)
            {
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
            }
            var process = Process.Start(psi);
            if (process is not null && summary.Config.CollectLaunchDiagnostics)
            {
                _ = StartLaunchDiagnosticsCaptureAsync(instanceFolderName, process);
            }
            _logger.LogInformation("Game started via umu-run: {Exe}", exe);
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

    private static bool TryDetectExistingInstanceProcess(string instanceFolderName)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return false;
        }

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

                var cmdline = System.Text.Encoding.UTF8.GetString(raw).Replace('\0', ' ');
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

    private void ApplyLinuxCompatibilityEnvironment(Models.InstanceConfig config, ProcessStartInfo psi)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        if (config.EnableGnomeCompatibilityProfile)
        {
            // Temporary profile while collecting evidence for GNOME/Zorin focus/minimize issues.
            psi.Environment["WINE_FULLSCREEN_PAUSE_ON_FOCUS_LOSS"] = "0";
            psi.Environment["PROTON_LOG"] = "1";
            psi.Environment["PROTON_LOG_DIR"] = OrionPaths.Root;
        }

        if (config.UseX11Fallback)
        {
            // Best-effort X11 forcing for session/compositor compatibility testing.
            psi.Environment["PROTON_ENABLE_WAYLAND"] = "0";
            psi.Environment["SDL_VIDEODRIVER"] = "x11";
            psi.Environment["GDK_BACKEND"] = "x11";
        }
    }

    private async Task StartLaunchDiagnosticsCaptureAsync(string instanceFolderName, Process process)
    {
        try
        {
            var logsDir = Path.Combine(OrionPaths.InstanceRoot(instanceFolderName), "logs");
            Directory.CreateDirectory(logsDir);
            var logPath = Path.Combine(logsDir, $"launch-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
            await using var output = new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            await writer.WriteLineAsync($"[orion] launch diagnostics started at {DateTime.UtcNow:O}");
            await writer.WriteLineAsync($"[orion] pid={process.Id}");
            await writer.FlushAsync();

            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var stdout = await outTask.ConfigureAwait(false);
            var stderr = await errTask.ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                await writer.WriteLineAsync("[stdout]");
                await writer.WriteLineAsync(stdout.Trim());
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                await writer.WriteLineAsync("[stderr]");
                await writer.WriteLineAsync(stderr.Trim());
            }

            await writer.WriteLineAsync($"[orion] exit_code={process.ExitCode}");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Launch diagnostics capture failed.");
        }
    }
}
