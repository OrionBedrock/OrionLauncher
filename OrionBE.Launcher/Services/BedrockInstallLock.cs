using System.Collections.Concurrent;

namespace OrionBE.Launcher.Services;

internal static class BedrockInstallLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    public static async Task<IDisposable> AcquireAsync(string versionKey, CancellationToken cancellationToken)
    {
        var sem = Locks.GetOrAdd(versionKey, static _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Release(sem);
    }

    private sealed class Release : IDisposable
    {
        private readonly SemaphoreSlim _sem;

        public Release(SemaphoreSlim sem) => _sem = sem;

        public void Dispose() => _sem.Release();
    }
}
