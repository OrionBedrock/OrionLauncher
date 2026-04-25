using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

public interface IBedrockVersionCatalogService
{
    /// <summary>Fetches or returns cached catalog (30-minute TTL, same idea as Amethyst).</summary>
    Task<IReadOnlyList<BedrockVersionEntry>> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a dropdown label or normalized version string to an entry.</summary>
    Task<BedrockVersionEntry?> TryResolveAsync(string selectedVersionLabel, CancellationToken cancellationToken = default);
}
