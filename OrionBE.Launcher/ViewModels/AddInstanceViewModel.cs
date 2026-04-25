using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrionBE.Launcher.Core.Events;
using OrionBE.Launcher.Services;

namespace OrionBE.Launcher.ViewModels;

public sealed partial class AddInstanceViewModel : ViewModelBase, IDisposable
{
    private readonly IApiService _apiService;
    private readonly IInstanceService _instanceService;
    private readonly IInstallationService _installationService;
    private readonly INavigationService _navigationService;
    private readonly IAppEventBus _eventBus;
    private readonly IDisposable _progressSubscription;

    public ObservableCollection<string> GameVersions { get; } = new();

    public AddInstanceViewModel(
        IApiService apiService,
        IInstanceService instanceService,
        IInstallationService installationService,
        INavigationService navigationService,
        IAppEventBus eventBus)
    {
        _apiService = apiService;
        _instanceService = instanceService;
        _installationService = installationService;
        _navigationService = navigationService;
        _eventBus = eventBus;
        _progressSubscription = _eventBus.Subscribe<InstallationProgressChanged>(e =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                InstallProgress = e.Progress01;
                InstallStatus = e.Step;
            });
        });

        _ = InitializeAsync();
    }

    [ObservableProperty]
    private string _instanceName = string.Empty;

    [ObservableProperty]
    private string _selectedGameVersion = string.Empty;

    [ObservableProperty]
    private bool _isModsMode;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private double _installProgress;

    [ObservableProperty]
    private string _installStatus = string.Empty;

    partial void OnInstanceNameChanged(string value) => CreateCommand.NotifyCanExecuteChanged();

    partial void OnIsInstallingChanged(bool value) => CreateCommand.NotifyCanExecuteChanged();

    private async Task InitializeAsync()
    {
        try
        {
            var versions = await _apiService.GetGameVersionsAsync().ConfigureAwait(false);
            foreach (var v in versions)
            {
                GameVersions.Add(v);
            }

            var latest = await _apiService.GetLatestGameVersionAsync().ConfigureAwait(false);
            SelectedGameVersion = !string.IsNullOrEmpty(latest)
                ? latest
                : (GameVersions.Count > 0 ? GameVersions[0] : string.Empty);
        }
        catch
        {
            // Rede indisponível: utilizador pode tentar novamente ao reabrir o ecrã.
        }
        finally
        {
            CreateCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void Cancel() => _navigationService.GoBack();

    private bool CanCreate() => !IsInstalling && !string.IsNullOrWhiteSpace(InstanceName);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create() => _ = CreateCoreAsync();

    private async Task CreateCoreAsync()
    {
        IsInstalling = true;
        InstallProgress = 0;
        InstallStatus = "Preparing...";

        try
        {
            var folder = await _instanceService.AllocateInstanceFolderNameAsync(InstanceName.Trim()).ConfigureAwait(false);
            var progress = new Progress<(string Step, double Progress01)>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    InstallProgress = p.Progress01;
                    InstallStatus = p.Step;
                });
            });

            await _installationService
                .InstallNewInstanceAsync(folder, InstanceName.Trim(), SelectedGameVersion, IsModsMode, progress)
                .ConfigureAwait(false);

            _navigationService.GoBack();
        }
        finally
        {
            IsInstalling = false;
        }
    }

    public void Dispose() => _progressSubscription.Dispose();
}
