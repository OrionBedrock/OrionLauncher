using System.Text.Json;
using OrionBE.Launcher.Core;
using OrionBE.Launcher.Infrastructure.Json;
using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

public sealed class ModService : IModService
{
    private readonly IFileSystemService _fileSystem;
    private readonly IDownloadService _downloadService;

    public ModService(IFileSystemService fileSystem, IDownloadService downloadService)
    {
        _fileSystem = fileSystem;
        _downloadService = downloadService;
    }

    public async Task<string> EnsureGlobalModFromCatalogAsync(
        ModCatalogItem mod,
        ModVersion selectedVersion,
        CancellationToken cancellationToken = default)
    {
        var folder = GlobalModFolderName(mod.Name, selectedVersion.Version);
        var root = OrionPaths.GlobalModFolder(folder);
        await _fileSystem.EnsureDirectoryAsync(root, cancellationToken).ConfigureAwait(false);

        var modConfig = new ModConfig
        {
            Name = mod.Name,
            Version = selectedVersion.Version,
            SupportedGameVersion = selectedVersion.SupportedGameVersion,
        };

        var json = JsonSerializer.Serialize(modConfig, LauncherJson.Options);
        await _fileSystem.WriteAllTextAsync(OrionPaths.GlobalModConfigPath(folder), json, cancellationToken).ConfigureAwait(false);

        var payloadPath = Path.Combine(root, "payload.mock.bin");
        await _downloadService
            .DownloadToFileAsync(new Uri(selectedVersion.DownloadUrl), payloadPath, null, cancellationToken)
            .ConfigureAwait(false);

        return folder;
    }

    public async Task CopyGlobalModIntoInstanceAsync(
        string instanceFolderName,
        string globalModFolderName,
        CancellationToken cancellationToken = default)
    {
        var source = OrionPaths.GlobalModFolder(globalModFolderName);
        var dest = Path.Combine(OrionPaths.InstanceMods(instanceFolderName), globalModFolderName);
        await _fileSystem.EnsureDirectoryAsync(Path.GetDirectoryName(dest)!, cancellationToken).ConfigureAwait(false);
        await _fileSystem.CopyDirectoryRecursiveAsync(source, dest, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<string>> ListGlobalModFoldersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(OrionPaths.GlobalMods))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var names = Directory
            .GetDirectories(OrionPaths.GlobalMods)
            .Select(static d => Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar)))
            .ToArray();
        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    private static string GlobalModFolderName(string modName, string version)
    {
        var safeName = string.Concat(modName.Where(static c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        if (string.IsNullOrEmpty(safeName))
        {
            safeName = "mod";
        }

        var safeVer = string.Concat(version.Where(static c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'));
        return $"{safeName}-{safeVer}";
    }
}
