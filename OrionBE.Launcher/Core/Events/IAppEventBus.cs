namespace OrionBE.Launcher.Core.Events;

public interface IAppEventBus
{
    void Publish<TEvent>(TEvent evt) where TEvent : class;
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
}
