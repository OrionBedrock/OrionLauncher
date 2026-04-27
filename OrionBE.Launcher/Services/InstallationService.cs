using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrionBE.Launcher.Core;
using OrionBE.Launcher.Core.Events;
using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

public sealed class InstallationService : IInstallationService
{
    private const string LeviLaminaPackageBase = "github.com/LiteLDev/LeviLamina#client";
    private const string LipNpmMetadataUrl = "https://registry.npmjs.org/@futrime/lip";
    private const string DotNetRuntimeVersion = "10.0.5";
    private const string DotNetRuntimeZipUrl =
        "https://dotnetcli.azureedge.net/dotnet/Runtime/10.0.5/dotnet-runtime-10.0.5-win-x64.zip";
    private const int DefaultLipTimeoutSeconds = 180;

    private readonly IInstanceService _instanceService;
    private readonly IFileSystemService _fileSystem;
    private readonly IDownloadService _downloadService;
    private readonly IHttpClientFactory _httpClientFactory;
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
        IHttpClientFactory httpClientFactory,
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
        _httpClientFactory = httpClientFactory;
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

        if (installLeviLamina && !string.IsNullOrWhiteSpace(config.LeviLaminaVersion))
        {
            Report("A instalar LeviLamina via LIP", 0.94);
            await InstallLeviLaminaAsync(instanceFolderName, config, cancellationToken).ConfigureAwait(false);
            Report("LeviLamina instalado e validado", 0.98);
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

    private async Task InstallLeviLaminaAsync(
        string instanceFolderName,
        InstanceConfig config,
        CancellationToken cancellationToken)
    {
        var requestedVersion = config.LeviLaminaVersion?.Trim();
        if (string.IsNullOrWhiteSpace(requestedVersion))
        {
            throw new InvalidOperationException("LeviLamina está habilitado, mas nenhuma versão foi definida.");
        }

        var packageRef = $"{LeviLaminaPackageBase}@{requestedVersion}";
        var gameDir = OrionPaths.InstanceGame(instanceFolderName);

        bool success;
        if (OperatingSystem.IsLinux())
        {
            success = await InstallLeviLaminaOnLinuxAsync(config, gameDir, packageRef, cancellationToken).ConfigureAwait(false);
        }
        else if (OperatingSystem.IsWindows())
        {
            success = await InstallLeviLaminaNativeAsync(gameDir, packageRef, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new PlatformNotSupportedException("Instalação LeviLamina só está implementada para Linux e Windows.");
        }

        if (!success)
        {
            throw new InvalidOperationException("Falha ao instalar/atualizar LeviLamina via LIP.");
        }

        var leviArtifactsDir = Path.Combine(gameDir, "mods", "LeviLamina");
        if (!Directory.Exists(leviArtifactsDir))
        {
            throw new InvalidOperationException(
                $"LIP terminou sem erro, mas artefato esperado não foi encontrado: {leviArtifactsDir}");
        }
    }

    private async Task<bool> InstallLeviLaminaOnLinuxAsync(
        InstanceConfig config,
        string gameDir,
        string packageRef,
        CancellationToken cancellationToken)
    {
        var timeout = ResolveLipTimeout();
        var customLipCommand = Environment.GetEnvironmentVariable("ORION_LIP_COMMAND")?.Trim();
        if (!string.IsNullOrWhiteSpace(customLipCommand))
        {
            _logger.LogInformation("Usando ORION_LIP_COMMAND no fluxo Linux.");
            return await InstallLeviLaminaUsingNativeLipCommandAsync(gameDir, customLipCommand, packageRef, timeout, cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(config.LinuxUmuRunPath) || !File.Exists(config.LinuxUmuRunPath))
        {
            throw new InvalidOperationException("umu-run não configurado para instalação LeviLamina no Linux.");
        }

        if (string.IsNullOrWhiteSpace(config.LinuxProtonPath) || !Directory.Exists(config.LinuxProtonPath))
        {
            throw new InvalidOperationException("PROTONPATH não configurado para instalação LeviLamina no Linux.");
        }

        if (string.IsNullOrWhiteSpace(config.LinuxWinePrefixPath))
        {
            throw new InvalidOperationException("WINEPREFIX da instância não configurado.");
        }

        await _fileSystem.EnsureDirectoryAsync(config.LinuxWinePrefixPath, cancellationToken).ConfigureAwait(false);
        var dotnetRootUnixPath = await EnsureLinuxWineDotNetRuntimeAsync(config.LinuxWinePrefixPath, cancellationToken)
            .ConfigureAwait(false);

        var cachedLipExe = await EnsureWindowsLipInCacheAsync(cancellationToken).ConfigureAwait(false);
        var localLipExe = Path.Combine(gameDir, "lip.exe");
        File.Copy(cachedLipExe, localLipExe, overwrite: true);

        var installCmdPath = Path.Combine(gameDir, "orion-lip-install.cmd");
        var updateCmdPath = Path.Combine(gameDir, "orion-lip-update.cmd");
        await File.WriteAllTextAsync(installCmdPath, $"@echo off{Environment.NewLine}lip.exe install {packageRef}{Environment.NewLine}", cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(updateCmdPath, $"@echo off{Environment.NewLine}lip.exe update {packageRef}{Environment.NewLine}", cancellationToken)
            .ConfigureAwait(false);

        var baseEnv = new Dictionary<string, string?>
        {
            ["PROTONPATH"] = config.LinuxProtonPath,
            ["WINEPREFIX"] = config.LinuxWinePrefixPath,
            ["DOTNET_ROOT"] = ToWineWindowsPath(dotnetRootUnixPath),
            ["DOTNET_ROOT_X64"] = ToWineWindowsPath(dotnetRootUnixPath),
        };

        var installExit = await RunProcessAsync(
                config.LinuxUmuRunPath,
                ["cmd.exe", "/c", "orion-lip-install.cmd"],
                gameDir,
                baseEnv,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);

        if (installExit == 0)
        {
            return true;
        }

        _logger.LogInformation("lip install falhou com código {Code}, tentando update.", installExit);
        var updateExit = await RunProcessAsync(
                config.LinuxUmuRunPath,
                ["cmd.exe", "/c", "orion-lip-update.cmd"],
                gameDir,
                baseEnv,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);

        return updateExit == 0;
    }

    private async Task<bool> InstallLeviLaminaNativeAsync(
        string gameDir,
        string packageRef,
        CancellationToken cancellationToken)
    {
        var timeout = ResolveLipTimeout();
        var customLipCommand = Environment.GetEnvironmentVariable("ORION_LIP_COMMAND")?.Trim();
        var lipCommand = string.IsNullOrWhiteSpace(customLipCommand) ? "lip" : customLipCommand;

        return await InstallLeviLaminaUsingNativeLipCommandAsync(gameDir, lipCommand, packageRef, timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> InstallLeviLaminaUsingNativeLipCommandAsync(
        string gameDir,
        string lipCommand,
        string packageRef,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        var executeArg = OperatingSystem.IsWindows() ? "/c" : "-lc";
        var installExit = await RunProcessAsync(
                shell,
                [executeArg, $"{lipCommand} install {packageRef}"],
                gameDir,
                null,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
        if (installExit == 0)
        {
            return true;
        }

        _logger.LogInformation("lip install (nativo) falhou com código {Code}, tentando update.", installExit);
        var updateExit = await RunProcessAsync(
                shell,
                [executeArg, $"{lipCommand} update {packageRef}"],
                gameDir,
                null,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
        return updateExit == 0;
    }

    private async Task<string> EnsureWindowsLipInCacheAsync(CancellationToken cancellationToken)
    {
        var cacheDir = Path.Combine(OrionPaths.Cache, "tools", "lip");
        await _fileSystem.EnsureDirectoryAsync(cacheDir, cancellationToken).ConfigureAwait(false);

        var cachedLipExe = Path.Combine(cacheDir, "lip.exe");
        if (File.Exists(cachedLipExe))
        {
            return cachedLipExe;
        }

        var archivePath = Path.Combine(cacheDir, "lip-latest.tgz");
        await DownloadLatestLipArchiveAsync(archivePath, cancellationToken).ConfigureAwait(false);
        await ExtractLipExeFromArchiveAsync(archivePath, cachedLipExe, cancellationToken).ConfigureAwait(false);
        return cachedLipExe;
    }

    private async Task DownloadLatestLipArchiveAsync(string archivePath, CancellationToken cancellationToken)
    {
        using var http = _httpClientFactory.CreateClient(nameof(DownloadService));
        using var metaResponse = await http.GetAsync(LipNpmMetadataUrl, cancellationToken).ConfigureAwait(false);
        metaResponse.EnsureSuccessStatusCode();

        await using var metaStream = await metaResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(metaStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var latest = doc.RootElement.GetProperty("dist-tags").GetProperty("latest").GetString();
        if (string.IsNullOrWhiteSpace(latest))
        {
            throw new InvalidOperationException("Não foi possível resolver versão latest do pacote @futrime/lip.");
        }

        var tarball = doc.RootElement.GetProperty("versions").GetProperty(latest).GetProperty("dist").GetProperty("tarball")
            .GetString();
        if (string.IsNullOrWhiteSpace(tarball))
        {
            throw new InvalidOperationException("Não foi possível resolver URL do tarball do LIP.");
        }

        using var archiveResponse = await http.GetAsync(tarball, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        archiveResponse.EnsureSuccessStatusCode();
        await using var input = await archiveResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(archivePath);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExtractLipExeFromArchiveAsync(
        string archivePath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        await using var source = File.OpenRead(archivePath);
        await using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        TarEntry? entry;
        while ((entry = tar.GetNextEntry()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.EntryType is not TarEntryType.RegularFile)
            {
                continue;
            }

            if (!string.Equals(entry.Name, "package/win32-x64/lip.exe", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(entry.Name, "package/win32-x64/lipd.exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await using var outStream = File.Create(outputPath);
            if (entry.DataStream is null)
            {
                break;
            }

            await entry.DataStream.CopyToAsync(outStream, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("Arquivo lip.exe/lipd.exe não encontrado no pacote @futrime/lip.");
    }

    private async Task<string> EnsureLinuxWineDotNetRuntimeAsync(string winePrefixPath, CancellationToken cancellationToken)
    {
        var runtimeRoot = Path.Combine(winePrefixPath, "dotnet-runtime", DotNetRuntimeVersion);
        if (HasDotNetRuntime10(runtimeRoot))
        {
            return runtimeRoot;
        }

        await _fileSystem.EnsureDirectoryAsync(runtimeRoot, cancellationToken).ConfigureAwait(false);

        var zipCacheDir = Path.Combine(OrionPaths.Cache, "dependencies");
        await _fileSystem.EnsureDirectoryAsync(zipCacheDir, cancellationToken).ConfigureAwait(false);
        var zipPath = Path.Combine(zipCacheDir, $"dotnet-runtime-{DotNetRuntimeVersion}-win-x64.zip");
        if (!File.Exists(zipPath))
        {
            using var response = await _httpClientFactory.CreateClient(nameof(DownloadService))
                .GetAsync(DotNetRuntimeZipUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = File.Create(zipPath);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        if (Directory.Exists(runtimeRoot))
        {
            Directory.Delete(runtimeRoot, recursive: true);
            await _fileSystem.EnsureDirectoryAsync(runtimeRoot, cancellationToken).ConfigureAwait(false);
        }

        ZipFile.ExtractToDirectory(zipPath, runtimeRoot, overwriteFiles: true);
        if (!HasDotNetRuntime10(runtimeRoot))
        {
            throw new InvalidOperationException("Falha ao preparar .NET Runtime 10 (win-x64) para o WINEPREFIX.");
        }

        return runtimeRoot;
    }

    private static bool HasDotNetRuntime10(string runtimeRoot)
    {
        var sharedDir = Path.Combine(runtimeRoot, "shared", "Microsoft.NETCore.App");
        if (!Directory.Exists(sharedDir))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateDirectories(sharedDir)
                .Select(Path.GetFileName)
                .Any(name => !string.IsNullOrWhiteSpace(name) && name.StartsWith("10.", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static string ToWineWindowsPath(string unixPath)
    {
        var normalized = Path.GetFullPath(unixPath).Replace('/', '\\');
        return $"Z:{normalized}";
    }

    private static TimeSpan ResolveLipTimeout()
    {
        var raw = Environment.GetEnvironmentVariable("ORION_LIP_TIMEOUT_SECONDS");
        return int.TryParse(raw, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(DefaultLipTimeoutSeconds);
    }

    private async Task<int> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? extraEnvironment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        if (extraEnvironment is not null)
        {
            foreach (var (key, value) in extraEnvironment)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                process.StartInfo.Environment[key] = value;
            }
        }

        process.Start();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // no-op
            }

            throw new TimeoutException($"Processo {fileName} excedeu o timeout de {timeout.TotalSeconds:F0}s.");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            _logger.LogInformation("{File} stdout: {Output}", fileName, stdout.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _logger.LogInformation("{File} stderr: {Output}", fileName, stderr.Trim());
        }

        return process.ExitCode;
    }
}
