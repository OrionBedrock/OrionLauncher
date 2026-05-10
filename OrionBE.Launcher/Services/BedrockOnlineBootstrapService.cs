using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OrionBE.Launcher.Core;

namespace OrionBE.Launcher.Services;

public sealed class BedrockOnlineBootstrapService : IBedrockOnlineBootstrapService
{
    private const string CaCertDownloadUrl = "https://curl.se/ca/cacert.pem";

    /// <summary>Windows binaries are listed here (GitHub "latest" releases often ship sources only).</summary>
    private const string CurlSeWindowsPageUrl = "https://curl.se/windows/";

    /// <summary>Last resort if HTML/GitHub resolution fails — update if this bundle disappears upstream.</summary>
    private const string FallbackWin64MingwZipUrl =
        "https://curl.se/windows/dl-8.20.0_2/curl-8.20.0_2-win64-mingw.zip";

    private static readonly Regex CurlSeWin64MingwZipHref = new(
        @"href=""(dl-[^""]+-win64-mingw\.zip)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IDownloadService _downloadService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFileSystemService _fileSystem;
    private readonly ILogger<BedrockOnlineBootstrapService> _logger;

    public BedrockOnlineBootstrapService(
        IDownloadService downloadService,
        IHttpClientFactory httpClientFactory,
        IFileSystemService fileSystem,
        ILogger<BedrockOnlineBootstrapService> logger)
    {
        _downloadService = downloadService;
        _httpClientFactory = httpClientFactory;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task EnsureOnlineSupportFilesAsync(
        string gameRoot,
        string minecraftWindowsExePath,
        Action<string>? logLine,
        CancellationToken cancellationToken = default)
    {
        var packageRoot = BedrockGameLayout.ResolveBedrockPackageRoot(gameRoot, minecraftWindowsExePath);
        var contentDir = BedrockGameLayout.GetContentDirectory(packageRoot);
        await _fileSystem.EnsureDirectoryAsync(contentDir, cancellationToken).ConfigureAwait(false);

        var certsDir = Path.Combine(packageRoot, "etc", "ssl", "certs");
        await _fileSystem.EnsureDirectoryAsync(certsDir, cancellationToken).ConfigureAwait(false);

        logLine?.Invoke($"Bedrock package root: {packageRoot}");
        logLine?.Invoke($"Content folder: {contentDir}");

        await EnsureCaBundleAsync(certsDir, logLine, cancellationToken).ConfigureAwait(false);
        await EnsureXcurlAsync(contentDir, logLine, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureCaBundleAsync(string certsDir, Action<string>? logLine, CancellationToken cancellationToken)
    {
        await _fileSystem.EnsureDirectoryAsync(OrionPaths.BedrockOnlineSupportCacheDir, cancellationToken).ConfigureAwait(false);
        var cachedPem = Path.Combine(OrionPaths.BedrockOnlineSupportCacheDir, "cacert.pem");

        if (!File.Exists(cachedPem) || new FileInfo(cachedPem).Length < 100)
        {
            logLine?.Invoke("Downloading cacert.pem (curl.se)…");
            await _downloadService
                .DownloadToFileAsync(new Uri(CaCertDownloadUrl), cachedPem, null, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            logLine?.Invoke("Reusing cached cacert.pem.");
        }

        var dest = Path.Combine(certsDir, "ca-bundle.crt");
        await Task.Run(() => File.Copy(cachedPem, dest, overwrite: true), cancellationToken).ConfigureAwait(false);
        logLine?.Invoke($"CA bundle: {dest}");
    }

    private async Task EnsureXcurlAsync(string contentDir, Action<string>? logLine, CancellationToken cancellationToken)
    {
        // Cached filename is historical; payload may be libcurl-4.dll (MSYS2) or libcurl-x64.dll (curl-for-win).
        var cachedDll = Path.Combine(OrionPaths.BedrockOnlineSupportCacheDir, "libcurl-4.dll");
        if (!File.Exists(cachedDll) || new FileInfo(cachedDll).Length < 4096)
        {
            logLine?.Invoke("Fetching libcurl from official curl win64-mingw package…");
            await _fileSystem.EnsureDirectoryAsync(OrionPaths.BedrockOnlineSupportCacheDir, cancellationToken).ConfigureAwait(false);

            var zipUrl = await ResolveWin64MingwZipUrlAsync(logLine, cancellationToken).ConfigureAwait(false);
            var zipPath = Path.Combine(OrionPaths.BedrockOnlineSupportCacheDir, "curl-win64-mingw.zip");
            await _downloadService.DownloadToFileAsync(new Uri(zipUrl), zipPath, null, cancellationToken).ConfigureAwait(false);

            await Task.Run(() => LibcurlZipExtractor.ExtractLibcurlDllFromWindowsMingwZip(zipPath, cachedDll), cancellationToken)
                .ConfigureAwait(false);

            try
            {
                File.Delete(zipPath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not remove temporary curl zip {Path}", zipPath);
            }
        }
        else
        {
            logLine?.Invoke("Reusing cached libcurl DLL (libcurl-4 or libcurl-x64).");
        }

        // mcbe-on-linux: ship mingw libcurl as Xcurl.dll (same payload; name required by the game).
        var destXcurl = Path.Combine(contentDir, "Xcurl.dll");
        await Task.Run(() => File.Copy(cachedDll, destXcurl, overwrite: true), cancellationToken).ConfigureAwait(false);
        logLine?.Invoke($"Xcurl.dll (libcurl): {destXcurl}");
    }

    private async Task<string> ResolveWin64MingwZipUrlAsync(Action<string>? logLine, CancellationToken cancellationToken)
    {
        var url = await TryResolveWin64MingwZipFromCurlSeAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(url))
        {
            logLine?.Invoke($"ZIP win64-mingw (curl.se): {url}");
            return url;
        }

        logLine?.Invoke("curl.se page did not yield a link; trying GitHub releases with binaries…");
        url = await TryResolveWin64MingwZipFromGitHubAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(url))
        {
            logLine?.Invoke($"ZIP win64-mingw (GitHub): {url}");
            return url;
        }

        _logger.LogWarning("Using pinned fallback URL for curl win64-mingw zip.");
        logLine?.Invoke($"Using pinned fallback URL: {FallbackWin64MingwZipUrl}");
        return FallbackWin64MingwZipUrl;
    }

    private async Task<string?> TryResolveWin64MingwZipFromCurlSeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var http = _httpClientFactory.CreateClient(nameof(DownloadService));
            using var response = await http.GetAsync(CurlSeWindowsPageUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var match = CurlSeWin64MingwZipHref.Match(html);
            if (!match.Success)
            {
                return null;
            }

            var relative = match.Groups[1].Value;
            return new Uri(new Uri(CurlSeWindowsPageUrl), relative).AbsoluteUri;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not parse curl.se/windows for win64-mingw zip.");
            return null;
        }
    }

    private async Task<string?> TryResolveWin64MingwZipFromGitHubAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var http = _httpClientFactory.CreateClient("GitHub");
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OrionBE-Launcher/1.0");
            using var response = await http
                .GetAsync("https://api.github.com/repos/curl/curl/releases?per_page=40", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!release.TryGetProperty("assets", out var assets))
                {
                    continue;
                }

                foreach (var asset in assets.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var nameProp))
                    {
                        continue;
                    }

                    var name = nameProp.GetString();
                    if (name is null ||
                        !name.Contains("win64-mingw", StringComparison.OrdinalIgnoreCase) ||
                        !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (asset.TryGetProperty("browser_download_url", out var urlProp))
                    {
                        return urlProp.GetString();
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve win64-mingw zip from GitHub releases.");
            return null;
        }
    }

}
