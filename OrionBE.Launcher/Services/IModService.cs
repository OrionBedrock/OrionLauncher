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

    Task<string> ImportGlobalModZipAsync(
        string zipPath,
        string fallbackName,
        string instanceGameVersion,
        ModZipImportOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<ModZipImportInspection> AnalyzeModZipAsync(
        string zipPath,
        CancellationToken cancellationToken = default);

    Task<string> ImportGlobalModDllAsync(
        string dllPath,
        string displayName,
        string instanceGameVersion,
        CancellationToken cancellationToken = default);

    Task<ModConfig?> GetGlobalModConfigAsync(
        string globalModFolderName,
        CancellationToken cancellationToken = default);

    ModCompatibilityReport EvaluateCompatibility(InstanceConfig instanceConfig, ModConfig? modConfig);

    Task<IReadOnlyList<string>> ListGlobalModFoldersAsync(CancellationToken cancellationToken = default);
}
