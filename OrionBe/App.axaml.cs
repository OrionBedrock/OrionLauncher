using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using OrionBE.Launcher.Services;
using OrionBe.Composition;
using OrionBE.Launcher.I18n;
using OrionBe.ViewModel;
using System.Linq;

namespace OrionBe;

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
        DisableAvaloniaDataAnnotationValidation();

        AppBootstrapper.Bootstrap();
        _serviceProvider = AppBootstrapper.Services;

        var launcherSettings = _serviceProvider.GetRequiredService<ILauncherSettingsService>().Load();
        var lang = string.IsNullOrWhiteSpace(launcherSettings.UiLanguage) ? "en-US" : launcherSettings.UiLanguage;
        Localizer.Instance.LoadLanguage(lang);

        var backgroundQueue = _serviceProvider.GetRequiredService<IBackgroundTaskQueue>();
        _ = backgroundQueue.RunProcessorAsync(_shutdownCts.Token);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>(),
            };

            var uiDialogs = _serviceProvider.GetRequiredService<IUiDialogService>();
            uiDialogs.AttachMainWindow(desktop.MainWindow);

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
                Localizer.Instance["dialogs_dependency_check_intro"] +
                string.Join('\n', report.MissingItems.Select(static item => $"- {item}")) +
                Localizer.Instance["dialogs_dependency_check_outro"];
            await uiDialogs
                .ShowMessageAsync(Localizer.Instance["dialogs_dependency_check_title"], message)
                .ConfigureAwait(true);
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
