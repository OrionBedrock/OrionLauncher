using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace OrionBE.Launcher.Services;

public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<CancellationToken, Task>> _channel = Channel.CreateUnbounded<Func<CancellationToken, Task>>();
    private readonly ILogger<BackgroundTaskQueue> _logger;

    public BackgroundTaskQueue(ILogger<BackgroundTaskQueue> logger)
    {
        _logger = logger;
    }

    public ValueTask QueueAsync(Func<CancellationToken, Task> workItem, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(workItem, cancellationToken);

    public async Task RunProcessorAsync(CancellationToken cancellationToken)
    {
        await foreach (var work in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await work(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background work item failed");
            }
        }
    }
}
