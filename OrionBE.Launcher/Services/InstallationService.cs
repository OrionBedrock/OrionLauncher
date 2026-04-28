using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Linq;
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

        UiLog($"Installation start: folder \"{instanceFolderName}\", game version \"{gameVersion}\", mods={(modsEnabled ? "yes" : "no")}, LeviLamina={(installLeviLamina ? leviLaminaVersion ?? "(not set)" : "no")}.");

        void Report(string step, double p01)
        {
            progress?.Report((step, p01));
            _eventBus.Publish(new InstallationProgressChanged(step, p01));
            UiLog(step);
        }

        Report("Resolving version in Bedrock catalog", 0.02);
        var entry = await _bedrockCatalog.TryResolveAsync(gameVersion, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            throw new InvalidOperationException(
                $"Version \"{gameVersion}\" was not found in the catalog. Refresh the list or pick another version.");
        }

        config.BedrockVersionUuid = string.IsNullOrWhiteSpace(entry.Uuid) ? null : entry.Uuid;
        config.LeviLaminaVersion = installLeviLamina && !string.IsNullOrWhiteSpace(leviLaminaVersion)
            ? leviLaminaVersion.Trim()
            : null;
        await _instanceService.SaveConfigAsync(instanceFolderName, config, cancellationToken).ConfigureAwait(false);

        if (OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            throw new InvalidOperationException(
                "Installing the .msixvc package (XVDTool) is only supported on Linux x64. Use an x86_64 system or install manually.");
        }

        if (OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            throw new InvalidOperationException("This flow requires XVDTool on Windows x64.");
        }

        if (OperatingSystem.IsLinux() && _gdkLinuxRuntimeService.IsSupported)
        {
            Report("Installing GDK Proton + UMU", 0.06);
            var gdkProgress = new Progress<(string Step, double Progress01)>(p =>
            {
                progress?.Report(p);
                _eventBus.Publish(new InstallationProgressChanged(p.Step, p.Progress01));
                UiLog(p.Step);
            });
            var gdk = await _gdkLinuxRuntimeService
                .EnsureRuntimeAsync(gdkProgress, cancellationToken)
                .ConfigureAwait(false);

            if (gdk is not null)
            {
                config.LinuxUmuRunPath = gdk.UmuRunPath;
                config.LinuxProtonPath = gdk.ProtonDirectoryPath;
                await _instanceService.SaveConfigAsync(instanceFolderName, config, cancellationToken).ConfigureAwait(false);
                Report("Linux runtime saved to instance config", 0.12);
            }
            else
            {
                Report("Linux GDK runtime failed or was skipped (see logs)", 0.12);
            }
        }
        else if (!OperatingSystem.IsLinux())
        {
            Report("Skipping Proton/UMU (not Linux)", 0.1);
        }

        Report("Fetching XVDTool (github.com/AmethystAPI/xvdtool)", 0.14);
        var xvdtool = await _xvdToolService.EnsureToolAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(xvdtool))
        {
            throw new InvalidOperationException("Could not obtain XVDTool. Check your network and use x64.");
        }

        await _fileSystem.EnsureDirectoryAsync(OrionPaths.BedrockMsixvcCacheDir, cancellationToken).ConfigureAwait(false);
        var cachedMsixvc = Path.Combine(OrionPaths.BedrockMsixvcCacheDir, $"Minecraft-{entry.Version}.msixvc");
        var workDir = Path.Combine(OrionPaths.Cache, "bedrock_work");
        await _fileSystem.EnsureDirectoryAsync(workDir, cancellationToken).ConfigureAwait(false);
        var workMsixvc = Path.Combine(workDir, $"{instanceFolderName}.msixvc");

        using (await BedrockInstallLock.AcquireAsync(entry.Version, cancellationToken).ConfigureAwait(false))
        {
            Report("Choosing mirror and downloading .msixvc", 0.18);
            await EnsureMsixvcPresentAsync(entry, cachedMsixvc, p => Report("Downloading .msixvc", 0.18 + p * 0.42), cancellationToken)
                .ConfigureAwait(false);

            Report("Preparing working copy of package", 0.62);
            await Task.Run(() => File.Copy(cachedMsixvc, workMsixvc, overwrite: true), cancellationToken).ConfigureAwait(false);

            try
            {
                Report("Decrypting package (CIK)", 0.66);
                UiLog("CIK: decrypting .msixvc with available CIK keys (trying each key).");
                await _xvdToolService.DecryptMsixvcInPlaceAsync(xvdtool, workMsixvc, cancellationToken).ConfigureAwait(false);
                UiLog("CIK: decryption finished successfully.");

                var gameDir = OrionPaths.InstanceGame(instanceFolderName);
                if (Directory.Exists(gameDir))
                {
                    Directory.Delete(gameDir, recursive: true);
                }

                await _fileSystem.EnsureDirectoryAsync(gameDir, cancellationToken).ConfigureAwait(false);

                Report("Extracting game files", 0.72);
                UiLog($"Extracting package contents to {gameDir}");
                await _xvdToolService.ExtractMsixvcAsync(xvdtool, workMsixvc, gameDir, cancellationToken).ConfigureAwait(false);
                UiLog("Package extraction finished.");
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
                    _logger.LogDebug(ex, "Could not delete temporary file {Path}", workMsixvc);
                }
            }
        }

        Report("Locating Minecraft.Windows.exe", 0.88);
        var exe = BedrockGameLayout.FindWindowsExecutable(OrionPaths.InstanceGame(instanceFolderName));
        if (string.IsNullOrEmpty(exe))
        {
            throw new InvalidOperationException(
                "Extraction finished but Minecraft.Windows.exe was not found in the game folder.");
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
            _eventBus.Publish(new InstallationExtrasStep("Mods enabled: extra hooks remain mock."));
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }

        if (installLeviLamina && !string.IsNullOrWhiteSpace(config.LeviLaminaVersion))
        {
            Report("Installing LeviLamina via LIP", 0.94);
            await InstallLeviLaminaAsync(instanceFolderName, config, cancellationToken).ConfigureAwait(false);
            Report("LeviLamina installed and verified", 0.98);
        }

        Report("Done", 1);
        _eventBus.Publish(new InstancesChanged());
        _logger.LogInformation("Instance {Folder} installed ({Version}, exe={Exe})", instanceFolderName, gameVersion, exe);
    }

    private async Task EnsureMsixvcPresentAsync(
        BedrockVersionEntry entry,
        string destinationPath,
        Action<double> downloadProgress,
        CancellationToken cancellationToken)
    {
        var mirror = await PickFastestMirrorAsync(entry.Urls, cancellationToken).ConfigureAwait(false);
        UiLog($"Download mirror selected: {mirror}");
        var uri = new Uri(mirror);
        var expected = await _downloadService.TryGetContentLengthAsync(uri, cancellationToken).ConfigureAwait(false);
        if (expected is > 0 && File.Exists(destinationPath))
        {
            var len = new FileInfo(destinationPath).Length;
            if (len == expected.Value)
            {
                _logger.LogInformation("Reusing cached .msixvc: {Path}", destinationPath);
                UiLog($".msixvc already in cache with expected size ({len} bytes). Skipping download.");
                downloadProgress(1);
                return;
            }
        }

        UiLog("Starting .msixvc download…");
        var lastLogged = -0.11;
        await _downloadService
            .DownloadToFileAsync(
                uri,
                destinationPath,
                new Progress<double>(p =>
                {
                    downloadProgress(p);
                    if (p >= 1 || p - lastLogged >= 0.10)
                    {
                        lastLogged = p >= 1 ? 1 : Math.Floor(p / 0.10) * 0.10;
                        UiLog($".msixvc download progress: {(p * 100):F0}%");
                    }
                }),
                cancellationToken)
            .ConfigureAwait(false);
        UiLog(".msixvc download finished.");
    }

    private async Task<string> PickFastestMirrorAsync(IReadOnlyList<string> urls, CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
        {
            throw new InvalidOperationException("The selected version has no download URLs.");
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
                _logger.LogDebug(ex, "Mirror HEAD failed: {Url}", url);
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
            throw new InvalidOperationException("LeviLamina is enabled but no version was set.");
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
            throw new PlatformNotSupportedException("LeviLamina installation is only implemented for Linux and Windows.");
        }

        if (!success)
        {
            throw new InvalidOperationException("Failed to install or update LeviLamina via LIP.");
        }

        var leviArtifactsDir = Path.Combine(gameDir, "mods", "LeviLamina");
        if (!Directory.Exists(leviArtifactsDir))
        {
            throw new InvalidOperationException(
                $"LIP exited successfully but expected artifact was not found: {leviArtifactsDir}");
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
            _logger.LogInformation("Using ORION_LIP_COMMAND for Linux flow.");
            return await InstallLeviLaminaUsingNativeLipCommandAsync(gameDir, customLipCommand, packageRef, timeout, cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(config.LinuxUmuRunPath) || !File.Exists(config.LinuxUmuRunPath))
        {
            throw new InvalidOperationException("umu-run is not configured for LeviLamina installation on Linux.");
        }

        if (string.IsNullOrWhiteSpace(config.LinuxProtonPath) || !Directory.Exists(config.LinuxProtonPath))
        {
            throw new InvalidOperationException("PROTONPATH is not configured for LeviLamina installation on Linux.");
        }

        if (string.IsNullOrWhiteSpace(config.LinuxWinePrefixPath))
        {
            throw new InvalidOperationException("Instance WINEPREFIX is not configured.");
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

        _logger.LogInformation("lip install failed with code {Code}, trying update.", installExit);
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

        _logger.LogInformation("lip install (native) failed with code {Code}, trying update.", installExit);
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
            throw new InvalidOperationException("Could not resolve latest version for package @futrime/lip.");
        }

        var tarball = doc.RootElement.GetProperty("versions").GetProperty(latest).GetProperty("dist").GetProperty("tarball")
            .GetString();
        if (string.IsNullOrWhiteSpace(tarball))
        {
            throw new InvalidOperationException("Could not resolve LIP tarball URL.");
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

        throw new InvalidOperationException("lip.exe/lipd.exe not found in @futrime/lip package.");
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
            throw new InvalidOperationException("Failed to prepare .NET Runtime 10 (win-x64) for WINEPREFIX.");
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
            }

            throw new TimeoutException($"Process {fileName} exceeded timeout of {timeout.TotalSeconds:F0}s.");
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

        UiLog($"{Path.GetFileName(fileName)} exited with code {process.ExitCode}.");
        var merged = string.Join("\n", new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(merged))
        {
            var t = merged.Trim();
            const int maxUiProcessChars = 480;
            if (t.Length > maxUiProcessChars)
            {
                t = t[..maxUiProcessChars] + "…";
            }

            UiLog($"Process output ({Path.GetFileName(fileName)}):\n{t}");
        }

        return process.ExitCode;
    }

    private void UiLog(string message, InstallationLogSeverity severity = InstallationLogSeverity.Info)
    {
        _eventBus.Publish(new InstallationLogLine(message, severity));
    }
}
