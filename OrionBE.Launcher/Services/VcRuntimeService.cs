using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace OrionBE.Launcher.Services;

public sealed class VcRuntimeService : IVcRuntimeService
{
    private const string VcRuntimeFileName = "vcruntime140_1.dll";
    private const string VcProxyLatestApi = "https://api.github.com/repos/LiteLDev/vcproxy/releases/latest";
    private static readonly string[] StaticFallbackUrls =
    [
        "https://raw.githubusercontent.com/LiteLDev/LeviLauncher/refs/heads/main/internal/vcruntime/vcruntime140_1.dll",
        "https://cdn.jsdelivr.net/gh/LiteLDev/LeviLauncher@main/internal/vcruntime/vcruntime140_1.dll",
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFileSystemService _fileSystem;
    private readonly ILogger<VcRuntimeService> _logger;

    public VcRuntimeService(
        IHttpClientFactory httpClientFactory,
        IFileSystemService fileSystem,
        ILogger<VcRuntimeService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task EnsureForGameAsync(string gameRoot, string exePath, CancellationToken cancellationToken = default)
    {
        var source = await ResolveOrDownloadSourceAsync(cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            _logger.LogWarning("VC runtime DLL unavailable; skipping.");
            return;
        }

        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(gameRoot, "vcruntime140_1.dll"),
        };

        var exeDir = Path.GetDirectoryName(exePath);
        if (!string.IsNullOrWhiteSpace(exeDir))
        {
            targets.Add(Path.Combine(exeDir, "vcruntime140_1.dll"));
        }

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _fileSystem.EnsureDirectoryAsync(Path.GetDirectoryName(target)!, cancellationToken).ConfigureAwait(false);
            File.Copy(source, target, overwrite: true);
        }
    }

    private async Task<string?> ResolveOrDownloadSourceAsync(CancellationToken cancellationToken)
    {
        var localCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "vcruntime140_1.dll"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "Dependencies", "vcruntime140_1.dll"),
            Path.Combine(Environment.CurrentDirectory, "vcruntime140_1.dll"),
        };

        var local = localCandidates.FirstOrDefault(File.Exists);
        if (local is not null)
        {
            return local;
        }

        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "OrionBE",
            "cache",
            "dependencies");
        await _fileSystem.EnsureDirectoryAsync(cacheDir, cancellationToken).ConfigureAwait(false);
        var cachedDll = Path.Combine(cacheDir, VcRuntimeFileName);
        if (File.Exists(cachedDll))
        {
            return cachedDll;
        }

        foreach (var url in await ResolveDownloadUrlsAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var client = _httpClientFactory.CreateClient(nameof(DownloadService));
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = File.Create(cachedDll);
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                return cachedDll;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed VC runtime URL: {Url}", url);
            }
        }

        _logger.LogWarning("Failed to download vcruntime140_1.dll from all known sources.");
        return null;
    }

    private async Task<IReadOnlyList<string>> ResolveDownloadUrlsAsync(CancellationToken cancellationToken)
    {
        var urls = new List<string>();
        try
        {
            var client = _httpClientFactory.CreateClient("GitHub");
            using var response = await client.GetAsync(VcProxyLatestApi, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (!asset.TryGetProperty("name", out var nameEl)
                            || !asset.TryGetProperty("browser_download_url", out var urlEl))
                        {
                            continue;
                        }

                        var name = nameEl.GetString();
                        var url = urlEl.GetString();
                        if (string.Equals(name, VcRuntimeFileName, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(url))
                        {
                            urls.Add(url);
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "vcproxy latest release lookup failed.");
        }

        urls.AddRange(StaticFallbackUrls);
        return urls;
    }
}
