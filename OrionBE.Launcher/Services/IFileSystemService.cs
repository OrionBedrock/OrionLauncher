namespace OrionBE.Launcher.Services;

public interface IFileSystemService
{
    Task EnsureDirectoryAsync(string path, CancellationToken cancellationToken = default);
    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default);
    Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);
    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default);
    Task CopyDirectoryRecursiveAsync(string sourceDir, string destDir, CancellationToken cancellationToken = default);
    Task DeleteFileIfExistsAsync(string path, CancellationToken cancellationToken = default);
    IReadOnlyList<string> EnumerateDirectories(string path);
    IReadOnlyList<string> EnumerateFiles(string path, string searchPattern);
}
