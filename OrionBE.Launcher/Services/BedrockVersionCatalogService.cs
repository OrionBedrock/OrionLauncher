using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OrionBE.Launcher.Core;
using OrionBE.Launcher.Infrastructure.Json;
using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

/// <summary>
/// Remote list from <see href="https://github.com/LukasPAH/minecraft-windows-gdk-version-db">minecraft-windows-gdk-version-db</see>
/// (same URL as Amethyst <c>VersionDatabase.DATABASE_URL</c>).
/// </summary>
public sealed class BedrockVersionCatalogService : IBedrockVersionCatalogService
{
    public const string DatabaseUrl =
        "https://raw.githubusercontent.com/LukasPAH/minecraft-windows-gdk-version-db/refs/heads/main/historical_versions.json";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
    private static readonly Regex GuidRegex = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFileSystemService _fileSystem;
    private readonly ILogger<BedrockVersionCatalogService> _logger;

    private IReadOnlyList<BedrockVersionEntry>? _memoryCatalog;
    private DateTime _memoryCatalogUtc;

    public BedrockVersionCatalogService(
        IHttpClientFactory httpClientFactory,
        IFileSystemService fileSystem,
        ILogger<BedrockVersionCatalogService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BedrockVersionEntry>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (_memoryCatalog is not null && DateTime.UtcNow - _memoryCatalogUtc < CacheTtl)
        {
            return _memoryCatalog;
        }

        await _fileSystem.EnsureDirectoryAsync(OrionPaths.Cache, cancellationToken).ConfigureAwait(false);

        var disk = await TryLoadCacheFileAsync(cancellationToken).ConfigureAwait(false);
        if (disk is not null && DateTime.UtcNow - disk.Value.LastUpdatedUtc < CacheTtl)
        {
            _memoryCatalog = disk.Value.Entries;
            _memoryCatalogUtc = disk.Value.LastUpdatedUtc;
            return _memoryCatalog;
        }

        try
        {
            var fresh = await FetchRemoteAsync(cancellationToken).ConfigureAwait(false);
            await SaveCacheFileAsync(fresh, cancellationToken).ConfigureAwait(false);
            _memoryCatalog = fresh;
            _memoryCatalogUtc = DateTime.UtcNow;
            return _memoryCatalog;
        }
        catch (Exception ex) when (disk is not null)
        {
            _logger.LogWarning(ex, "Bedrock version DB fetch failed; using stale cache.");
            _memoryCatalog = disk.Value.Entries;
            _memoryCatalogUtc = disk.Value.LastUpdatedUtc;
            return _memoryCatalog;
        }
    }

    public async Task<BedrockVersionEntry?> TryResolveAsync(string selectedVersionLabel, CancellationToken cancellationToken = default)
    {
        var key = selectedVersionLabel.Trim();
        var catalog = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        return catalog.FirstOrDefault(e =>
            string.Equals(e.DropdownLabel, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.Version, key, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(DateTime LastUpdatedUtc, IReadOnlyList<BedrockVersionEntry> Entries)?> TryLoadCacheFileAsync(
        CancellationToken cancellationToken)
    {
        var json = await _fileSystem.ReadAllTextIfExistsAsync(OrionPaths.BedrockVersionCacheFile, cancellationToken)
            .ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        try
        {
            var model = JsonSerializer.Deserialize<BedrockCatalogCacheFile>(json, LauncherJson.Options);
            if (model?.Versions is null || model.Versions.Count == 0)
            {
                return null;
            }

            return (model.LastUpdatedUtc, model.Versions);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ignoring invalid bedrock version cache.");
            return null;
        }
    }

    private async Task SaveCacheFileAsync(IReadOnlyList<BedrockVersionEntry> entries, CancellationToken cancellationToken)
    {
        var model = new BedrockCatalogCacheFile
        {
            LastUpdatedUtc = DateTime.UtcNow,
            Versions = entries.ToList(),
        };
        var json = JsonSerializer.Serialize(model, LauncherJson.Options);
        await _fileSystem.WriteAllTextAsync(OrionPaths.BedrockVersionCacheFile, json, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<BedrockVersionEntry>> FetchRemoteAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(DownloadService));
        using var response = await client.GetAsync(DatabaseUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<HistoricalVersionsDto>(stream, LauncherJson.Options, cancellationToken)
            .ConfigureAwait(false);
        if (dto is null)
        {
            throw new InvalidOperationException("Versão remota inválida (JSON vazio).");
        }

        static string FixVersion(string v) =>
            v.Replace("Release ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Preview ", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

        var list = new List<BedrockVersionEntry>();
        foreach (var row in dto.ReleaseVersions ?? [])
        {
            if (row.Urls is null || row.Urls.Count == 0)
            {
                continue;
            }

            list.Add(new BedrockVersionEntry
            {
                Version = FixVersion(row.Version),
                Uuid = ExtractUuidFromUrl(row.Urls[0]),
                Type = "release",
                Urls = row.Urls.ToList(),
            });
        }

        foreach (var row in dto.PreviewVersions ?? [])
        {
            if (row.Urls is null || row.Urls.Count == 0)
            {
                continue;
            }

            list.Add(new BedrockVersionEntry
            {
                Version = FixVersion(row.Version),
                Uuid = ExtractUuidFromUrl(row.Urls[0]),
                Type = "preview",
                Urls = row.Urls.ToList(),
            });
        }

        list.Sort(static (a, b) => BedrockVersionOrdering.CompareDescending(a.Version, b.Version));
        return list;
    }

    private static string ExtractUuidFromUrl(string url)
    {
        var matches = GuidRegex.Matches(url);
        return matches.Count == 0 ? string.Empty : matches[^1].Value;
    }

    private sealed class BedrockCatalogCacheFile
    {
        public DateTime LastUpdatedUtc { get; set; }
        public List<BedrockVersionEntry> Versions { get; set; } = [];
    }

    private sealed class HistoricalVersionsDto
    {
        [JsonPropertyName("file_version")]
        public int FileVersion { get; set; }

        [JsonPropertyName("releaseVersions")]
        public List<VersionUrlsRow>? ReleaseVersions { get; set; }

        [JsonPropertyName("previewVersions")]
        public List<VersionUrlsRow>? PreviewVersions { get; set; }
    }

    private sealed class VersionUrlsRow
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("urls")]
        public List<string>? Urls { get; set; }
    }
}
