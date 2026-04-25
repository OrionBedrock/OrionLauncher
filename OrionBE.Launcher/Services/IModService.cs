using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

public interface IModService
{
    Task<string> EnsureGlobalModFromCatalogAsync(
        ModCatalogItem mod,
        ModVersion selectedVersion,
        CancellationToken cancellationToken = default);

    Task CopyGlobalModIntoInstanceAsync(
        string instanceFolderName,
        string globalModFolderName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListGlobalModFoldersAsync(CancellationToken cancellationToken = default);
}
