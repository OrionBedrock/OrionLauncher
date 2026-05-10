using Microsoft.Extensions.DependencyInjection;
using OrionBE.Launcher.Composition;
using OrionBe.Extensions;
using OrionBe.Loaders;

namespace OrionBe.Composition;

public static class AppBootstrapper
{
    public static ServiceProvider Services { get; private set; } = null!;

    public static void Bootstrap()
    {
        AsyncImageLoader.ImageLoader.AsyncImageLoader = new LruImageLoader();
        AsyncImageLoader.ImageBrushLoader.AsyncImageLoader = new LruImageLoader();

        var services = new ServiceCollection();
        services.AddApplicationServices();
        services.AddOrionLauncherServices();
        Services = services.BuildServiceProvider();
    }
}
