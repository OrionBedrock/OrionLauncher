namespace OrionBE.Launcher.Services;

public interface IBackgroundTaskQueue
{
    ValueTask QueueAsync(Func<CancellationToken, Task> workItem, CancellationToken cancellationToken = default);
    Task RunProcessorAsync(CancellationToken cancellationToken);
}
