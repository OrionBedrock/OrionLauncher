using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrionBE.Launcher.Core.Events;
using OrionBE.Launcher.Infrastructure.Ui;
using OrionBE.Launcher.Services;
using OrionBE.Launcher.ViewModels;

namespace OrionBE.Launcher.Composition;

public static class AppBootstrapper
{
    public static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(static builder =>
        {
            builder.ClearProviders();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddHttpClient(
            nameof(DownloadService),
            static client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("OrionBE-Launcher/1.0");
            });
        services.AddHttpClient(
            "GitHub",
            static client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("OrionBE-Launcher/1.0");
            });

        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IAppEventBus, AppEventBus>();
        services.AddSingleton<IBedrockVersionCatalogService, BedrockVersionCatalogService>();
        services.AddSingleton<IApiService, LauncherApiService>();
        services.AddSingleton<IInstanceService, InstanceService>();
        services.AddSingleton<IModService, ModService>();
        services.AddSingleton<IDownloadService, DownloadService>();
        services.AddSingleton<IXvdToolService, XvdToolService>();
        services.AddSingleton<IGdkLinuxRuntimeService, GdkLinuxRuntimeService>();
        services.AddSingleton<IInstallationService, InstallationService>();
        services.AddSingleton<IGameLaunchService, GameLaunchService>();
        services.AddSingleton<IFileExplorerService, FileExplorerService>();
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        services.AddSingleton<INavigationService>(static sp => new NavigationService(sp));
        services.AddSingleton<IUiDialogService, AvaloniaUiDialogService>();

        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<BrowseModsViewModel>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<AddInstanceViewModel>();
        services.AddTransient<InstanceSettingsViewModel>();
        services.AddTransient<ModDetailsViewModel>();

        return services.BuildServiceProvider();
    }
}
