using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

public interface IBedrockVersionCatalogService
{
    /// <summary>Fetches or returns cached catalog with 30-minute TTL.</summary>
    Task<IReadOnlyList<BedrockVersionEntry>> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a dropdown label or normalized version string to an entry.</summary>
    Task<BedrockVersionEntry?> TryResolveAsync(string selectedVersionLabel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the newest catalog entry whose <see cref="BedrockVersionEntry.Type"/> matches the current entry
    /// (stable/release stays on release; preview stays on preview) and whose version is strictly greater than the current one.
    /// </summary>
    Task<BedrockVersionEntry?> TryGetLatestUpgradeInSameChannelAsync(
        string currentVersionLabel,
        CancellationToken cancellationToken = default);
}
