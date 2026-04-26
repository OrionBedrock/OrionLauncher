using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace OrionBE.Launcher.Services;

public sealed class LeviLaminaCompatibilityService : ILeviLaminaCompatibilityService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(20);
    private static readonly string[] DbUrls =
    [
        "https://cdn.jsdelivr.net/gh/LiteLDev/levilamina-client-version-db@main/v2/version-db.json",
        "https://fastly.jsdelivr.net/gh/LiteLDev/levilamina-client-version-db@main/v2/version-db.json",
        "https://raw.githubusercontent.com/LiteLDev/levilamina-client-version-db/refs/heads/main/v2/version-db.json",
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBedrockVersionCatalogService _bedrockVersionCatalogService;
    private readonly ILogger<LeviLaminaCompatibilityService> _logger;

    private Dictionary<string, List<string>>? _cachedMap;
    private DateTime _cacheUtc;

    public LeviLaminaCompatibilityService(
        IHttpClientFactory httpClientFactory,
        IBedrockVersionCatalogService bedrockVersionCatalogService,
        ILogger<LeviLaminaCompatibilityService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _bedrockVersionCatalogService = bedrockVersionCatalogService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> GetSupportedVersionsAsync(
        string selectedGameVersionLabel,
        CancellationToken cancellationToken = default)
    {
        var game = await _bedrockVersionCatalogService.TryResolveAsync(selectedGameVersionLabel, cancellationToken)
            .ConfigureAwait(false);
        if (game is null || string.IsNullOrWhiteSpace(game.Version))
        {
            return [];
        }

        var db = await EnsureMapAsync(cancellationToken).ConfigureAwait(false);
        List<string>? versions = null;
        foreach (var key in BuildLookupKeys(game.Version))
        {
            if (db.TryGetValue(key, out var hit) && hit.Count > 0)
            {
                versions = hit;
                break;
            }
        }

        if (versions is null || versions.Count == 0)
        {
            return [];
        }

        return versions
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(v => v, Comparer<string>.Create(CompareSemverLike))
            .ToList();
    }

    private static IEnumerable<string> BuildLookupKeys(string gameVersion)
    {
        var v = gameVersion.Trim();
        if (string.IsNullOrWhiteSpace(v))
        {
            yield break;
        }

        yield return v;

        if (v.StartsWith("1.", StringComparison.Ordinal))
        {
            var withoutMajor = v[2..];
            if (!string.IsNullOrWhiteSpace(withoutMajor))
            {
                yield return withoutMajor;
            }
        }
        else
        {
            yield return $"1.{v}";
        }
    }

    private async Task<Dictionary<string, List<string>>> EnsureMapAsync(CancellationToken cancellationToken)
    {
        if (_cachedMap is not null && DateTime.UtcNow - _cacheUtc < CacheTtl)
        {
            return _cachedMap;
        }

        var client = _httpClientFactory.CreateClient(nameof(DownloadService));
        Exception? last = null;
        foreach (var url in DbUrls)
        {
            try
            {
                using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    continue;
                }

                await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var db = await JsonSerializer.DeserializeAsync<LeviDbRoot>(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (db?.Versions is null)
                {
                    continue;
                }

                _cachedMap = db.Versions.ToDictionary(
                    kv => kv.Key.Trim(),
                    kv => kv.Value?.ToList() ?? [],
                    StringComparer.OrdinalIgnoreCase);
                _cacheUtc = DateTime.UtcNow;
                return _cachedMap;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        if (last is not null)
        {
            _logger.LogWarning(last, "Failed to fetch LeviLamina compatibility DB.");
        }

        _cachedMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        _cacheUtc = DateTime.UtcNow;
        return _cachedMap;
    }

    private static int CompareSemverLike(string a, string b)
    {
        var pa = a.TrimStart('v', 'V').Split('.', StringSplitOptions.RemoveEmptyEntries);
        var pb = b.TrimStart('v', 'V').Split('.', StringSplitOptions.RemoveEmptyEntries);
        var n = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < n; i++)
        {
            var ia = i < pa.Length && int.TryParse(pa[i], out var ai) ? ai : 0;
            var ib = i < pb.Length && int.TryParse(pb[i], out var bi) ? bi : 0;
            var c = ia.CompareTo(ib);
            if (c != 0)
            {
                return c;
            }
        }

        return string.CompareOrdinal(a, b);
    }

    private sealed class LeviDbRoot
    {
        [JsonPropertyName("versions")]
        public Dictionary<string, List<string>>? Versions { get; set; }
    }
}
