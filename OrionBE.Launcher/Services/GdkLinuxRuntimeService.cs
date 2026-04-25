using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrionBE.Launcher.Core;

namespace OrionBE.Launcher.Services;

/// <summary>
/// Fetches latest GitHub release assets, extracts under <c>~/OrionBE/cache/tools/</c>,
/// applies chmod 755 recursively, and resolves <c>umu-run</c> and GDK <c>proton</c>.
/// Repositories: <see href="https://github.com/raonygamer/gdk-proton">raonygamer/gdk-proton</see>,
/// <see href="https://github.com/raonygamer/umu-launcher">raonygamer/umu-launcher</see>.
/// </summary>
public sealed class GdkLinuxRuntimeService : IGdkLinuxRuntimeService
{
    public const string GdkProtonRepo = "raonygamer/gdk-proton";
    public const string UmuLauncherRepo = "raonygamer/umu-launcher";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDownloadService _downloadService;
    private readonly IFileSystemService _fileSystem;
    private readonly ILogger<GdkLinuxRuntimeService> _logger;

    public GdkLinuxRuntimeService(
        IHttpClientFactory httpClientFactory,
        IDownloadService downloadService,
        IFileSystemService fileSystem,
        ILogger<GdkLinuxRuntimeService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _downloadService = downloadService;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public bool IsSupported => OperatingSystem.IsLinux();

    public async Task<GdkLinuxRuntimePaths?> EnsureRuntimeAsync(
        IProgress<(string Step, double Progress01)>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return null;
        }

        void Report(string step, double p01) => progress?.Report((step, p01));

        await _fileSystem.EnsureDirectoryAsync(OrionPaths.Cache, cancellationToken).ConfigureAwait(false);
        var toolsRoot = Path.Combine(OrionPaths.Cache, "tools");
        await _fileSystem.EnsureDirectoryAsync(toolsRoot, cancellationToken).ConfigureAwait(false);

        Report("Fetching GDK Proton (GitHub latest)", 0.62);
        var protonDir = await EnsureToolFromGitHubAsync(
                GdkProtonRepo,
                Path.Combine(toolsRoot, "gdk-proton"),
                "proton",
                cancellationToken)
            .ConfigureAwait(false);

        Report("Fetching UMU Launcher (GitHub latest)", 0.72);
        var umuPath = await EnsureToolFromGitHubAsync(
                UmuLauncherRepo,
                Path.Combine(toolsRoot, "umu-launcher"),
                "umu-run",
                cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(protonDir) || string.IsNullOrEmpty(umuPath))
        {
            _logger.LogWarning("GDK Linux runtime incomplete: protonDir={Proton}, umu={Umu}", protonDir, umuPath);
            return null;
        }

        Report("Linux GDK runtime ready (umu-run + GDK Proton)", 0.82);
        return new GdkLinuxRuntimePaths(umuPath, protonDir);
    }

    private async Task<string?> EnsureToolFromGitHubAsync(
        string repo,
        string installRoot,
        string markerFileName,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("GitHub");
        var url = $"https://api.github.com/repos/{repo}/releases/latest";
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GitHub API failed for {Repo}: {Status}", repo, response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagEl))
        {
            return null;
        }

        var tag = tagEl.GetString() ?? "unknown";
        if (!root.TryGetProperty("assets", out var assets) || assets.GetArrayLength() == 0)
        {
            _logger.LogError("No assets on latest release for {Repo}", repo);
            return null;
        }

        JsonElement asset = default;
        foreach (var a in assets.EnumerateArray())
        {
            if (a.TryGetProperty("name", out var nameEl) &&
                IsSupportedArchive(nameEl.GetString()) &&
                a.TryGetProperty("browser_download_url", out _))
            {
                asset = a;
                break;
            }
        }

        if (asset.ValueKind == JsonValueKind.Undefined)
        {
            _logger.LogError("No supported archive asset for {Repo}", repo);
            return null;
        }

        var downloadUrl = asset.GetProperty("browser_download_url").GetString();
        var assetName = asset.GetProperty("name").GetString();
        if (string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(assetName))
        {
            return null;
        }

        var versionDir = Path.Combine(installRoot, SanitizeTag(tag));
        await _fileSystem.EnsureDirectoryAsync(versionDir, cancellationToken).ConfigureAwait(false);

        var extractDir = Path.Combine(versionDir, "extracted");
        var markerPath = FindExecutableFile(extractDir, markerFileName);
        if (markerPath is not null)
        {
            return markerFileName == "umu-run" ? markerPath : Path.GetDirectoryName(markerPath);
        }

        var archivePath = Path.Combine(versionDir, assetName);
        await _downloadService
            .DownloadToFileAsync(new Uri(downloadUrl), archivePath, null, cancellationToken)
            .ConfigureAwait(false);

        if (Directory.Exists(extractDir))
        {
            Directory.Delete(extractDir, recursive: true);
        }

        await _fileSystem.EnsureDirectoryAsync(extractDir, cancellationToken).ConfigureAwait(false);
        await ExtractArchiveAsync(archivePath, extractDir, cancellationToken).ConfigureAwait(false);

        try
        {
            File.Delete(archivePath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete archive {Path}", archivePath);
        }

        await ChmodRecursive755Async(extractDir, cancellationToken).ConfigureAwait(false);

        var resolved = FindExecutableFile(extractDir, markerFileName);
        if (resolved is null)
        {
            _logger.LogError("After extract, '{Marker}' not found under {Dir}", markerFileName, extractDir);
            return null;
        }

        return markerFileName == "umu-run" ? resolved : Path.GetDirectoryName(resolved);
    }

    private static bool IsSupportedArchive(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeTag(string tag) =>
        string.Concat(tag.Select(static c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));

    private static string? FindExecutableFile(string root, string fileName)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static async Task ExtractArchiveAsync(string archivePath, string destDir, CancellationToken cancellationToken)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, destDir, overwriteFiles: true);
            return;
        }

        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            await RunProcessAsync(
                    "tar",
                    ["-xzf", archivePath, "-C", destDir],
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException($"Unsupported archive format: {archivePath}");
    }

    private static async Task ChmodRecursive755Async(string path, CancellationToken cancellationToken)
    {
        await RunProcessAsync("chmod", ["-R", "755", path], cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunProcessAsync(string fileName, string[] args, CancellationToken cancellationToken)
    {
        using var proc = new Process();
        proc.StartInfo.FileName = fileName;
        proc.StartInfo.RedirectStandardOutput = true;
        proc.StartInfo.RedirectStandardError = true;
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.CreateNoWindow = true;
        foreach (var a in args)
        {
            proc.StartInfo.ArgumentList.Add(a);
        }

        proc.Start();
        await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"{fileName} exited with {proc.ExitCode}: {err}");
        }
    }
}
