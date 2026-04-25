namespace OrionBE.Launcher.Services;

public interface IDownloadService
{
    Task<long?> TryGetContentLengthAsync(Uri source, CancellationToken cancellationToken = default);

    Task DownloadToFileAsync(
        Uri source,
        string destinationFilePath,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default);
}
