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
    private readonly ILeviLaminaCompatibilityService _leviLaminaCompatibilityService;
    private readonly IDisposable _progressSubscription;

    public ObservableCollection<string> GameVersions { get; } = new();
    public ObservableCollection<string> LeviLaminaVersions { get; } = new();

    public AddInstanceViewModel(
        IApiService apiService,
        IInstanceService instanceService,
        IInstallationService installationService,
        INavigationService navigationService,
        IAppEventBus eventBus,
        ILeviLaminaCompatibilityService leviLaminaCompatibilityService)
    {
        _apiService = apiService;
        _instanceService = instanceService;
        _installationService = installationService;
        _navigationService = navigationService;
        _eventBus = eventBus;
        _leviLaminaCompatibilityService = leviLaminaCompatibilityService;
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
    private bool _installWithLeviLamina;

    [ObservableProperty]
    private string? _selectedLeviLaminaVersion;

    [ObservableProperty]
    private bool _isSelectedVersionLeviCompatible;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private double _installProgress;

    [ObservableProperty]
    private string _installStatus = string.Empty;

    partial void OnInstanceNameChanged(string value) => CreateCommand.NotifyCanExecuteChanged();

    partial void OnIsInstallingChanged(bool value) => CreateCommand.NotifyCanExecuteChanged();

    partial void OnSelectedGameVersionChanged(string value) => _ = RefreshLeviCompatibilityAsync(value);

    partial void OnIsModsModeChanged(bool value)
    {
        if (!value)
        {
            InstallWithLeviLamina = false;
        }
    }

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
            await RefreshLeviCompatibilityAsync(SelectedGameVersion).ConfigureAwait(false);
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
                .InstallNewInstanceAsync(
                    folder,
                    InstanceName.Trim(),
                    SelectedGameVersion,
                    IsModsMode,
                    IsModsMode && InstallWithLeviLamina && IsSelectedVersionLeviCompatible,
                    IsModsMode && InstallWithLeviLamina ? SelectedLeviLaminaVersion : null,
                    progress)
                .ConfigureAwait(false);

            _navigationService.GoBack();
        }
        finally
        {
            IsInstalling = false;
        }
    }

    public bool ShowLeviLaminaOption => IsModsMode && IsSelectedVersionLeviCompatible;
    public string LeviSupportStatusText => IsSelectedVersionLeviCompatible
        ? "LeviLamina support available for this game version."
        : "LeviLamina not available for this game version.";

    private async Task RefreshLeviCompatibilityAsync(string selectedGameVersion)
    {
        try
        {
            var versions = await _leviLaminaCompatibilityService
                .GetSupportedVersionsAsync(selectedGameVersion)
                .ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LeviLaminaVersions.Clear();
                foreach (var v in versions)
                {
                    LeviLaminaVersions.Add(v);
                }

                IsSelectedVersionLeviCompatible = LeviLaminaVersions.Count > 0;
                if (!IsSelectedVersionLeviCompatible)
                {
                    InstallWithLeviLamina = false;
                    SelectedLeviLaminaVersion = null;
                }
                else if (string.IsNullOrWhiteSpace(SelectedLeviLaminaVersion))
                {
                    SelectedLeviLaminaVersion = LeviLaminaVersions[0];
                }

                OnPropertyChanged(nameof(ShowLeviLaminaOption));
                OnPropertyChanged(nameof(LeviSupportStatusText));
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LeviLaminaVersions.Clear();
                IsSelectedVersionLeviCompatible = false;
                InstallWithLeviLamina = false;
                SelectedLeviLaminaVersion = null;
                OnPropertyChanged(nameof(ShowLeviLaminaOption));
                OnPropertyChanged(nameof(LeviSupportStatusText));
            });
        }
    }

    public void Dispose() => _progressSubscription.Dispose();
}
