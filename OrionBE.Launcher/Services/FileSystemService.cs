namespace OrionBE.Launcher.Services;

public sealed class FileSystemService : IFileSystemService
{
    public Task EnsureDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, contents, cancellationToken);

    public async Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(path));
    }

    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Directory.Exists(path));
    }

    public async Task CopyDirectoryRecursiveAsync(string sourceDir, string destDir, CancellationToken cancellationToken = default)
    {
        await EnsureDirectoryAsync(destDir, cancellationToken).ConfigureAwait(false);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dest = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destSub = Path.Combine(destDir, Path.GetFileName(dir));
            await CopyDirectoryRecursiveAsync(dir, destSub, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task DeleteFileIfExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<string> EnumerateDirectories(string path) =>
        Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>();

    public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern) =>
        Directory.Exists(path) ? Directory.GetFiles(path, searchPattern) : Array.Empty<string>();
}
