using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace OrionBE.Launcher.Services;

public sealed class DownloadService : IDownloadService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DownloadService> _logger;

    public DownloadService(IHttpClientFactory httpClientFactory, ILogger<DownloadService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<long?> TryGetContentLengthAsync(Uri source, CancellationToken cancellationToken = default)
    {
        if (!source.IsAbsoluteUri || source.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var client = _httpClientFactory.CreateClient(nameof(DownloadService));
        using var request = new HttpRequestMessage(HttpMethod.Head, source);
        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return response.Content.Headers.ContentLength;
    }

    public async Task DownloadToFileAsync(
        Uri source,
        string destinationFilePath,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!source.IsAbsoluteUri || source.Scheme is not ("http" or "https"))
        {
            await SimulateDownloadAsync(destinationFilePath, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        var client = _httpClientFactory.CreateClient(nameof(DownloadService));
        using var response = await client
            .GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(destinationFilePath);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readTotal += read;
            if (total > 0)
            {
                progress?.Report(readTotal / (double)total);
            }
        }

        progress?.Report(1);
    }

    private static async Task SimulateDownloadAsync(
        string destinationFilePath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        progress?.Report(0.25);
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        progress?.Report(0.55);
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        progress?.Report(0.85);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
        await File.WriteAllTextAsync(destinationFilePath, "mock-download\n", cancellationToken).ConfigureAwait(false);
        progress?.Report(1);
    }
}
