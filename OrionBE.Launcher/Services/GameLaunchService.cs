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
            throw new InvalidOperationException("Instância não encontrada.");
        }

        var exe = summary.Config.BedrockWindowsExecutablePath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            throw new InvalidOperationException(
                "Executável do jogo não encontrado. Reinstale a instância ou verifique a pasta do jogo.");
        }

        var workDir = Path.GetDirectoryName(exe);
        if (string.IsNullOrEmpty(workDir))
        {
            throw new InvalidOperationException("Caminho do executável inválido.");
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
                    "umu-run não configurado. Abra as definições da instância ou reinstale no Linux para obter o runtime GDK.");
            }

            if (string.IsNullOrWhiteSpace(proton) || !Directory.Exists(proton))
            {
                throw new InvalidOperationException(
                    "PROTONPATH (GDK Proton) não configurado. Reinstale a instância no Linux.");
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
            _logger.LogInformation("Jogo iniciado via umu-run: {Exe}", exe);
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
            _logger.LogInformation("Jogo iniciado (Windows): {Exe}", exe);
            return;
        }

        throw new PlatformNotSupportedException("Arranque Bedrock só está implementado para Linux e Windows.");
    }
}
