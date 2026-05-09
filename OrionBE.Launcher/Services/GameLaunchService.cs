using System.Diagnostics;
using System.Runtime.InteropServices;
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

            Process.Start(psi);
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
}
