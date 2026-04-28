using System.Diagnostics;
using Microsoft.Extensions.Logging;
namespace OrionBE.Launcher.Services;

public sealed class GameLaunchService : IGameLaunchService
{
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

    public async Task LaunchInstanceAsync(string instanceFolderName, CancellationToken cancellationToken = default)
    {
        var summary = await _instanceService.GetAsync(instanceFolderName, cancellationToken).ConfigureAwait(false);
        if (summary is null)
        {
            throw new InvalidOperationException("Instance not found.");
        }

        var exe = summary.Config.BedrockWindowsExecutablePath;
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

        await _vcRuntimeService.EnsureForGameAsync(OrionBE.Launcher.Core.OrionPaths.InstanceGame(instanceFolderName), exe, cancellationToken)
            .ConfigureAwait(false);

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
}
