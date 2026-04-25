using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

public interface IApiService
{
    Task<IReadOnlyList<string>> GetGameVersionsAsync(CancellationToken cancellationToken = default);
    Task<string?> GetLatestGameVersionAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModCatalogItem>> GetModsCatalogAsync(CancellationToken cancellationToken = default);
    Task<ModCatalogItem?> GetModByIdAsync(string modId, CancellationToken cancellationToken = default);
}
