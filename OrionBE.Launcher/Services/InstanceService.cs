using System.Text.Json;
using OrionBE.Launcher.Core;
using OrionBE.Launcher.Infrastructure.Json;
using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

public sealed class InstanceService : IInstanceService
{
    private readonly IFileSystemService _fileSystem;

    public InstanceService(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task EnsureLauncherLayoutAsync(CancellationToken cancellationToken = default)
    {
        await _fileSystem.EnsureDirectoryAsync(OrionPaths.Root, cancellationToken).ConfigureAwait(false);
        await _fileSystem.EnsureDirectoryAsync(OrionPaths.Instances, cancellationToken).ConfigureAwait(false);
        await _fileSystem.EnsureDirectoryAsync(OrionPaths.GlobalMods, cancellationToken).ConfigureAwait(false);
        await _fileSystem.EnsureDirectoryAsync(OrionPaths.Cache, cancellationToken).ConfigureAwait(false);
        await _fileSystem.EnsureDirectoryAsync(OrionPaths.Assets, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InstanceSummary>> ListInstancesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLauncherLayoutAsync(cancellationToken).ConfigureAwait(false);
        var dirs = _fileSystem.EnumerateDirectories(OrionPaths.Instances);
        var list = new List<InstanceSummary>();
        foreach (var dir in dirs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
            var cfgPath = OrionPaths.InstanceConfigPath(folder);
            var json = await _fileSystem.ReadAllTextIfExistsAsync(cfgPath, cancellationToken).ConfigureAwait(false);
            if (json is null)
            {
                continue;
            }

            var cfg = JsonSerializer.Deserialize<InstanceConfig>(json, LauncherJson.Options);
            if (cfg is null)
            {
                continue;
            }

            list.Add(new InstanceSummary { FolderName = folder, Config = cfg });
        }

        return list;
    }

    public async Task<InstanceSummary?> GetAsync(string instanceFolderName, CancellationToken cancellationToken = default)
    {
        var json = await _fileSystem.ReadAllTextIfExistsAsync(OrionPaths.InstanceConfigPath(instanceFolderName), cancellationToken)
            .ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        var cfg = JsonSerializer.Deserialize<InstanceConfig>(json, LauncherJson.Options);
        return cfg is null ? null : new InstanceSummary { FolderName = instanceFolderName, Config = cfg };
    }

    public async Task<string> AllocateInstanceFolderNameAsync(string displayName, CancellationToken cancellationToken = default)
    {
        var baseName = InstanceFolderNameHelper.ToFolderName(displayName);
        var name = baseName;
        var i = 2;
        while (await _fileSystem.DirectoryExistsAsync(OrionPaths.InstanceRoot(name), cancellationToken).ConfigureAwait(false))
        {
            name = $"{baseName}_{i++}";
        }

        return name;
    }

    public async Task SaveConfigAsync(string instanceFolderName, InstanceConfig config, CancellationToken cancellationToken = default)
    {
        var path = OrionPaths.InstanceConfigPath(instanceFolderName);
        await _fileSystem.EnsureDirectoryAsync(Path.GetDirectoryName(path)!, cancellationToken).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(config, LauncherJson.Options);
        await _fileSystem.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteInstanceAsync(string instanceFolderName, CancellationToken cancellationToken = default)
    {
        var root = OrionPaths.InstanceRoot(instanceFolderName);
        if (!Directory.Exists(root))
        {
            return Task.CompletedTask;
        }

        return Task.Run(
            () =>
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            },
            cancellationToken);
    }
}
