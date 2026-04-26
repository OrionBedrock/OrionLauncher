using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OrionBE.Launcher.Core;
using OrionBE.Launcher.Core.Events;
using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

public sealed class InstallationService : IInstallationService
{
    private readonly IInstanceService _instanceService;
    private readonly IFileSystemService _fileSystem;
    private readonly IDownloadService _downloadService;
    private readonly IAppEventBus _eventBus;
    private readonly IGdkLinuxRuntimeService _gdkLinuxRuntimeService;
    private readonly IBedrockVersionCatalogService _bedrockCatalog;
    private readonly IXvdToolService _xvdToolService;
    private readonly IVcRuntimeService _vcRuntimeService;
    private readonly ILogger<InstallationService> _logger;

    public InstallationService(
        IInstanceService instanceService,
        IFileSystemService fileSystem,
        IDownloadService downloadService,
        IAppEventBus eventBus,
        IGdkLinuxRuntimeService gdkLinuxRuntimeService,
        IBedrockVersionCatalogService bedrockCatalog,
        IXvdToolService xvdToolService,
        IVcRuntimeService vcRuntimeService,
        ILogger<InstallationService> logger)
    {
        _instanceService = instanceService;
        _fileSystem = fileSystem;
        _downloadService = downloadService;
        _eventBus = eventBus;
        _gdkLinuxRuntimeService = gdkLinuxRuntimeService;
        _bedrockCatalog = bedrockCatalog;
        _xvdToolService = xvdToolService;
        _vcRuntimeService = vcRuntimeService;
        _logger = logger;
    }

    public async Task InstallNewInstanceAsync(
        string instanceFolderName,
        string displayName,
        string gameVersion,
        bool modsEnabled,
        bool installLeviLamina,
        string? leviLaminaVersion,
        IProgress<(string Step, double Progress01)>? progress,
        CancellationToken cancellationToken = default)
    {
        await _instanceService.EnsureLauncherLayoutAsync(cancellationToken).ConfigureAwait(false);

        await _fileSystem.EnsureDirectoryAsync(OrionPaths.InstanceRoot(instanceFolderName), cancellationToken).ConfigureAwait(false);
        await _fileSystem.EnsureDirectoryAsync(OrionPaths.InstanceGame(instanceFolderName), cancellationToken).ConfigureAwait(false);
        await _fileSystem.EnsureDirectoryAsync(OrionPaths.InstanceMods(instanceFolderName), cancellationToken).ConfigureAwait(false);

        var config = new InstanceConfig
        {
            Name = displayName,
            Version = gameVersion,
            GamePath = "game",
            ModsEnabled = modsEnabled,
            Mods = [],
        };

        await _instanceService.SaveConfigAsync(instanceFolderName, config, cancellationToken).ConfigureAwait(false);

        void Report(string step, double p01)
        {
            progress?.Report((step, p01));
            _eventBus.Publish(new InstallationProgressChanged(step, p01));
        }

        Report("A resolver versão no catálogo Bedrock", 0.02);
        var entry = await _bedrockCatalog.TryResolveAsync(gameVersion, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            throw new InvalidOperationException(
                $"Versão \"{gameVersion}\" não encontrada no catálogo. Atualize a lista ou escolha outra versão.");
        }

        config.BedrockVersionUuid = string.IsNullOrWhiteSpace(entry.Uuid) ? null : entry.Uuid;
        config.LeviLaminaVersion = installLeviLamina && !string.IsNullOrWhiteSpace(leviLaminaVersion)
            ? leviLaminaVersion.Trim()
            : null;
        await _instanceService.SaveConfigAsync(instanceFolderName, config, cancellationToken).ConfigureAwait(false);

        if (OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            throw new InvalidOperationException(
                "A instalação do pacote .msixvc (XVDTool) só é suportada em Linux x64. Use um sistema x86_64 ou instale manualmente.");
        }

        if (OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            throw new InvalidOperationException("XVDTool neste fluxo requer Windows x64.");
        }

        if (OperatingSystem.IsLinux() && _gdkLinuxRuntimeService.IsSupported)
        {
            Report("A instalar GDK Proton + UMU", 0.06);
            var gdk = await _gdkLinuxRuntimeService
                .EnsureRuntimeAsync(progress, cancellationToken)
                .ConfigureAwait(false);

            if (gdk is not null)
            {
                config.LinuxUmuRunPath = gdk.UmuRunPath;
                config.LinuxProtonPath = gdk.ProtonDirectoryPath;
                await _instanceService.SaveConfigAsync(instanceFolderName, config, cancellationToken).ConfigureAwait(false);
                Report("Runtime Linux guardado na configuração", 0.12);
            }
            else
            {
                Report("Runtime GDK Linux falhou ou foi ignorado (ver registos)", 0.12);
            }
        }
        else if (!OperatingSystem.IsLinux())
        {
            Report("A ignorar Proton/UMU (não Linux)", 0.1);
        }

        Report("A obter XVDTool (github.com/AmethystAPI/xvdtool)", 0.14);
        var xvdtool = await _xvdToolService.EnsureToolAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(xvdtool))
        {
            throw new InvalidOperationException("Não foi possível obter o XVDTool. Verifique a rede e a arquitetura (x64).");
        }

        await _fileSystem.EnsureDirectoryAsync(OrionPaths.BedrockMsixvcCacheDir, cancellationToken).ConfigureAwait(false);
        var cachedMsixvc = Path.Combine(OrionPaths.BedrockMsixvcCacheDir, $"Minecraft-{entry.Version}.msixvc");
        var workDir = Path.Combine(OrionPaths.Cache, "bedrock_work");
        await _fileSystem.EnsureDirectoryAsync(workDir, cancellationToken).ConfigureAwait(false);
        var workMsixvc = Path.Combine(workDir, $"{instanceFolderName}.msixvc");

        using (await BedrockInstallLock.AcquireAsync(entry.Version, cancellationToken).ConfigureAwait(false))
        {
            Report("A escolher mirror e a descarregar .msixvc", 0.18);
            await EnsureMsixvcPresentAsync(entry, cachedMsixvc, p => Report("A descarregar .msixvc", 0.18 + p * 0.42), cancellationToken)
                .ConfigureAwait(false);

            Report("A preparar cópia de trabalho do pacote", 0.62);
            await Task.Run(() => File.Copy(cachedMsixvc, workMsixvc, overwrite: true), cancellationToken).ConfigureAwait(false);

            try
            {
                Report("A desencriptar pacote (CIK)", 0.66);
                await _xvdToolService.DecryptMsixvcInPlaceAsync(xvdtool, workMsixvc, cancellationToken).ConfigureAwait(false);

                var gameDir = OrionPaths.InstanceGame(instanceFolderName);
                if (Directory.Exists(gameDir))
                {
                    Directory.Delete(gameDir, recursive: true);
                }

                await _fileSystem.EnsureDirectoryAsync(gameDir, cancellationToken).ConfigureAwait(false);

                Report("A extrair ficheiros do jogo", 0.72);
                await _xvdToolService.ExtractMsixvcAsync(xvdtool, workMsixvc, gameDir, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (File.Exists(workMsixvc))
                    {
                        File.Delete(workMsixvc);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Não foi possível apagar ficheiro temporário {Path}", workMsixvc);
                }
            }
        }

        Report("A localizar Minecraft.Windows.exe", 0.88);
        var exe = BedrockGameLayout.FindWindowsExecutable(OrionPaths.InstanceGame(instanceFolderName));
        if (string.IsNullOrEmpty(exe))
        {
            throw new InvalidOperationException(
                "Extração concluída mas Minecraft.Windows.exe não foi encontrado na pasta do jogo.");
        }

        await _vcRuntimeService.EnsureForGameAsync(OrionPaths.InstanceGame(instanceFolderName), exe, cancellationToken)
            .ConfigureAwait(false);

        config.BedrockWindowsExecutablePath = exe;
        config.LinuxWinePrefixPath = Path.Combine(OrionPaths.InstanceRoot(instanceFolderName), "wineprefix");
        await _fileSystem.EnsureDirectoryAsync(config.LinuxWinePrefixPath, cancellationToken).ConfigureAwait(false);
        await _fileSystem.EnsureDirectoryAsync(Path.Combine(config.LinuxWinePrefixPath, "dosdevices"), cancellationToken)
            .ConfigureAwait(false);

        await _instanceService.SaveConfigAsync(instanceFolderName, config, cancellationToken).ConfigureAwait(false);

        if (modsEnabled)
        {
            _eventBus.Publish(new InstallationExtrasStep("Mods ativos: hooks extra continuam mock."));
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }

        Report("Concluído", 1);
        _eventBus.Publish(new InstancesChanged());
        _logger.LogInformation("Instância {Folder} instalada ({Version}, exe={Exe})", instanceFolderName, gameVersion, exe);
    }

    

    private async Task EnsureMsixvcPresentAsync(
        BedrockVersionEntry entry,
        string destinationPath,
        Action<double> downloadProgress,
        CancellationToken cancellationToken)
    {
        var mirror = await PickFastestMirrorAsync(entry.Urls, cancellationToken).ConfigureAwait(false);
        var uri = new Uri(mirror);
        var expected = await _downloadService.TryGetContentLengthAsync(uri, cancellationToken).ConfigureAwait(false);
        if (expected is > 0 && File.Exists(destinationPath))
        {
            var len = new FileInfo(destinationPath).Length;
            if (len == expected.Value)
            {
                _logger.LogInformation("A reutilizar .msixvc em cache: {Path}", destinationPath);
                downloadProgress(1);
                return;
            }
        }

        await _downloadService
            .DownloadToFileAsync(uri, destinationPath, new Progress<double>(downloadProgress), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> PickFastestMirrorAsync(IReadOnlyList<string> urls, CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
        {
            throw new InvalidOperationException("A versão escolhida não tem URLs de download.");
        }

        string? bestUrl = null;
        long bestMs = long.MaxValue;
        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var sw = Stopwatch.StartNew();
                var len = await _downloadService.TryGetContentLengthAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
                sw.Stop();
                if (len is null)
                {
                    continue;
                }

                if (sw.ElapsedMilliseconds < bestMs)
                {
                    bestMs = sw.ElapsedMilliseconds;
                    bestUrl = url;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Mirror HEAD falhou: {Url}", url);
            }
        }

        return bestUrl ?? urls[0];
    }
}
