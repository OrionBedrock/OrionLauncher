using Avalonia;
using OrionBe.Composition;

namespace OrionBe.Extensions;

public static class AppBuilderExtensions
{
    public static AppBuilder BootstrapApplication(this AppBuilder builder)
    {
        AppBootstrapper.Bootstrap();

        return builder
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
