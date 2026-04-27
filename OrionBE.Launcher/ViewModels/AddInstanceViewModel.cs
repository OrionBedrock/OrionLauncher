using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrionBE.Launcher.Core.Events;
using OrionBE.Launcher.Services;

namespace OrionBE.Launcher.ViewModels;

public sealed partial class AddInstanceViewModel : ViewModelBase, IDisposable
{
    private const int MaxInstallLogChars = 384_000;

    private readonly ConcurrentQueue<InstallationLogLine> _installLogQueue = new();
    private int _installLogDrainScheduled;

    private readonly IApiService _apiService;
    private readonly IInstanceService _instanceService;
    private readonly IInstallationService _installationService;
    private readonly INavigationService _navigationService;
    private readonly IAppEventBus _eventBus;
    private readonly ILeviLaminaCompatibilityService _leviLaminaCompatibilityService;
    private readonly IDisposable _progressSubscription;
    private readonly IDisposable _installLogSubscription;

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
            Dispatcher.UIThread.Post(
                () =>
                {
                    InstallProgress = e.Progress01;
                    InstallStatus = e.Step;
                },
                DispatcherPriority.Normal);
        });

        _installLogSubscription = _eventBus.Subscribe<InstallationLogLine>(OnInstallLogLinePublished);

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

    [ObservableProperty]
    private string _installLogText = string.Empty;

    partial void OnInstanceNameChanged(string value) => CreateCommand.NotifyCanExecuteChanged();

    partial void OnInstallLogTextChanged(string value) => CopyInstallLogCommand.NotifyCanExecuteChanged();

    private void OnInstallLogLinePublished(InstallationLogLine e)
    {
        _installLogQueue.Enqueue(e);
        if (Interlocked.CompareExchange(ref _installLogDrainScheduled, 1, 0) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(DrainInstallLogQueue, DispatcherPriority.Background);
    }

    /// <summary>
    /// Drena a fila na thread de UI em lotes (uma ou poucas atualizações de propriedade por rajada),
    /// evitando milhares de <see cref="Dispatcher.UIThread.Post"/> e concatenações O(n²) no TextBox.
    /// </summary>
    private void DrainInstallLogQueue()
    {
        Interlocked.Exchange(ref _installLogDrainScheduled, 0);

        while (true)
        {
            var sb = new StringBuilder(Math.Min(4096, MaxInstallLogChars));
            while (_installLogQueue.TryDequeue(out var line))
            {
                var prefix = line.Severity switch
                {
                    InstallationLogSeverity.Error => "[ERRO] ",
                    InstallationLogSeverity.Warning => "[AVISO] ",
                    _ => string.Empty,
                };
                sb.Append($"{DateTime.Now:HH:mm:ss} {prefix}{line.Message}\n");
            }

            if (sb.Length == 0)
            {
                break;
            }

            var chunk = sb.ToString();
            var combined = string.IsNullOrEmpty(InstallLogText) ? chunk : InstallLogText + chunk;
            if (combined.Length > MaxInstallLogChars)
            {
                combined = TrimInstallLog(combined, MaxInstallLogChars);
            }

            InstallLogText = combined;
            CopyInstallLogCommand.NotifyCanExecuteChanged();

            if (_installLogQueue.IsEmpty)
            {
                break;
            }
        }

        if (!_installLogQueue.IsEmpty &&
            Interlocked.CompareExchange(ref _installLogDrainScheduled, 1, 0) == 0)
        {
            Dispatcher.UIThread.Post(DrainInstallLogQueue, DispatcherPriority.Background);
        }
    }

    private static string TrimInstallLog(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        const string head = "… [início do registo omitido]\n\n";
        var budget = maxChars - head.Length;
        if (budget <= 0)
        {
            return text[..maxChars];
        }

        return head + text.Substring(text.Length - budget);
    }

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
        InstallLogText = string.Empty;
        IsInstalling = true;
        InstallProgress = 0;
        InstallStatus = "Preparing...";

        try
        {
            var folder = await _instanceService.AllocateInstanceFolderNameAsync(InstanceName.Trim()).ConfigureAwait(false);

            await _installationService
                .InstallNewInstanceAsync(
                    folder,
                    InstanceName.Trim(),
                    SelectedGameVersion,
                    IsModsMode,
                    IsModsMode && InstallWithLeviLamina && IsSelectedVersionLeviCompatible,
                    IsModsMode && InstallWithLeviLamina ? SelectedLeviLaminaVersion : null,
                    progress: null)
                .ConfigureAwait(false);

            _navigationService.GoBack();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                InstallLogText += $"{DateTime.Now:HH:mm:ss} [ERRO] {ex.Message}\n";
                if (ex.InnerException is not null)
                {
                    InstallLogText += $"{DateTime.Now:HH:mm:ss} [ERRO] Detalhe: {ex.InnerException.Message}\n";
                }
            });
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private bool CanCopyInstallLog() => !string.IsNullOrWhiteSpace(InstallLogText);

    [RelayCommand(CanExecute = nameof(CanCopyInstallLog))]
    private async Task CopyInstallLogAsync()
    {
        if (string.IsNullOrWhiteSpace(InstallLogText))
        {
            return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            var clipboard = window.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(InstallLogText).ConfigureAwait(false);
            }
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

    public void Dispose()
    {
        _progressSubscription.Dispose();
        _installLogSubscription.Dispose();
    }
}
