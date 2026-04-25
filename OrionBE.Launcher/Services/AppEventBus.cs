using System.Collections.Concurrent;
using OrionBE.Launcher.Core.Events;

namespace OrionBE.Launcher.Services;

public sealed class AppEventBus : IAppEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

    public void Publish<TEvent>(TEvent evt) where TEvent : class
    {
        if (_handlers.TryGetValue(typeof(TEvent), out var list))
        {
            foreach (var d in list.ToArray())
            {
                ((Action<TEvent>)d)(evt);
            }
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        var list = _handlers.GetOrAdd(typeof(TEvent), _ => []);
        lock (list)
        {
            list.Add(handler);
        }

        return new Subscription(() =>
        {
            if (_handlers.TryGetValue(typeof(TEvent), out var inner))
            {
                lock (inner)
                {
                    inner.Remove(handler);
                }
            }
        });
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
