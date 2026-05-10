using Microsoft.Extensions.DependencyInjection;

namespace OrionBE.Launcher.Composition;

/// <summary>
/// Used by tests and tooling to build the service provider without the OrionBe shell.
/// </summary>
public static class LauncherRuntimeBootstrapper
{
    public static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddOrionLauncherServices();
        return services.BuildServiceProvider();
    }
}
