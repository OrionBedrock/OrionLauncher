using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using OrionBE.Launcher.Composition;
using OrionBE.Launcher.Services;
using OrionBE.Launcher.ViewModels;
using OrionBE.Launcher.Views;
using System.Linq;

namespace OrionBE.Launcher;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private readonly CancellationTokenSource _shutdownCts = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            _serviceProvider = AppBootstrapper.CreateServiceProvider();

            var backgroundQueue = _serviceProvider.GetRequiredService<IBackgroundTaskQueue>();
            _ = backgroundQueue.RunProcessorAsync(_shutdownCts.Token);

            var mainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>(),
            };

            var uiDialogs = _serviceProvider.GetRequiredService<IUiDialogService>();
            uiDialogs.AttachMainWindow(mainWindow);

            desktop.MainWindow = mainWindow;
            desktop.Exit += (_, _) => _shutdownCts.Cancel();

            _ = RunFirstLaunchDependencyCheckAsync(_serviceProvider, uiDialogs);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunFirstLaunchDependencyCheckAsync(
        ServiceProvider serviceProvider,
        IUiDialogService uiDialogs)
    {
        try
        {
            var checker = serviceProvider.GetRequiredService<IStartupDependencyCheckService>();
            var report = await checker.RunIfFirstLaunchAsync().ConfigureAwait(true);
            if (!report.IsFirstLaunch || !report.HasMissingItems)
            {
                return;
            }

            var message =
                "The launcher detected missing runtime dependencies during first startup.\n\n" +
                string.Join('\n', report.MissingItems.Select(static item => $"- {item}")) +
                "\n\nPlease install the missing dependencies and restart OrionBE Launcher.";
            await uiDialogs.ShowMessageAsync("Dependency check", message).ConfigureAwait(true);
        }
        catch
        {
            // Dependency check must never block launcher startup.
        }
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
