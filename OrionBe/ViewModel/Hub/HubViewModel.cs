using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrionBE.Launcher.I18n;
using OrionBE.Launcher.Models;
using OrionBE.Launcher.Services;
using OrionBe.Infrastructure.Interfaces;
using OrionBe.Infrastructure.Services.Microsoft;
using OrionBe.Router;
using OrionBe.ViewModel.Shared;

namespace OrionBe.ViewModel.Hub;

public partial class HubViewModel : MainWindowViewModelBase
{
    private readonly IMinecraftNewService _minecraftNewService;
    private readonly IInstanceService _instanceService;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly ILauncherSettingsService _launcherSettings;
    private readonly INavigationService _launcherNavigation;
    private readonly IUiDialogService _uiDialogs;
    private readonly RouterHistory<MainWindowViewModelBase> _mainRouter;
    private readonly DispatcherTimer _runningPollTimer;
    private bool _runningPollStarted;

    public ObservableCollection<NewInfo> Infos { get; set; } = new();

    public ObservableCollection<InstanceSummary> HubInstances { get; } = new();

    [ObservableProperty]
    private InstanceSummary? _selectedHubInstance;

    [ObservableProperty]
    private string _playButtonLabel = string.Empty;

    [ObservableProperty]
    private bool _isSelectedInstanceRunning;

    [ObservableProperty]
    private bool _isHubLaunching;

    public bool HasHubInstances => HubInstances.Count > 0;

    public HubViewModel(
        IMinecraftNewService minecraftNewService,
        IInstanceService instanceService,
        IGameLaunchService gameLaunchService,
        ILauncherSettingsService launcherSettings,
        INavigationService launcherNavigation,
        IUiDialogService uiDialogs,
        RouterHistory<MainWindowViewModelBase> mainRouter)
    {
        _minecraftNewService = minecraftNewService;
        _instanceService = instanceService;
        _gameLaunchService = gameLaunchService;
        _launcherSettings = launcherSettings;
        _launcherNavigation = launcherNavigation;
        _uiDialogs = uiDialogs;
        _mainRouter = mainRouter;
        Localizer.Instance.CultureChanged += OnLocalizationChanged;

        _playButtonLabel = Localizer.Instance["hub_play"];

        _runningPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500),
        };
        _runningPollTimer.Tick += (_, _) => RefreshPrimaryButtonUi();
    }

    private void OnLocalizationChanged(object? sender, EventArgs e) => _ = RefreshHubInstancesAsync();

    public override async Task OnNavigatedToAsync(CancellationToken ctx)
    {
        Infos.Clear();

        var news = await _minecraftNewService.GetNewsAsync(Localizer.Instance.Language).ConfigureAwait(true);

        foreach (var newInfo in news)
        {
            Infos.Add(newInfo);
        }

        await RefreshHubInstancesAsync().ConfigureAwait(true);

        if (!_runningPollStarted)
        {
            _runningPollTimer.Start();
            _runningPollStarted = true;
        }

        RefreshPrimaryButtonUi();
    }

    private async Task RefreshHubInstancesAsync()
    {
        try
        {
            var list = await _instanceService.ListInstancesAsync(CancellationToken.None).ConfigureAwait(false);
            var ordered = list
                .OrderBy(static i => i.Config.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                HubInstances.Clear();
                foreach (var i in ordered)
                {
                    HubInstances.Add(i);
                }

                var settings = _launcherSettings.Load();
                var preferred = settings.LastPlayedInstanceFolderName;
                InstanceSummary? pick = null;
                if (!string.IsNullOrWhiteSpace(preferred))
                {
                    pick = HubInstances.FirstOrDefault(x =>
                        string.Equals(x.FolderName, preferred, StringComparison.OrdinalIgnoreCase));
                }

                pick ??= HubInstances.FirstOrDefault();

                SelectedHubInstance = pick;

                OnPropertyChanged(nameof(HasHubInstances));
                RefreshPrimaryButtonUi();
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                HubInstances.Clear();
                SelectedHubInstance = null;
                IsSelectedInstanceRunning = false;
                OnPropertyChanged(nameof(HasHubInstances));
                RefreshPrimaryButtonUi();
            });
        }
    }

    /// <summary>Atualiza rótulo do botão principal, estado “em execução” e CanExecute do comando Play.</summary>
    private void RefreshPrimaryButtonUi()
    {
        if (HubInstances.Count == 0)
        {
            IsSelectedInstanceRunning = false;
            PlayButtonLabel = Localizer.Instance["hub_create_new_instance"];
            HubPrimaryActionCommand.NotifyCanExecuteChanged();
            return;
        }

        if (SelectedHubInstance is null)
        {
            IsSelectedInstanceRunning = false;
            PlayButtonLabel = Localizer.Instance["hub_play"];
            HubPrimaryActionCommand.NotifyCanExecuteChanged();
            return;
        }

        var folder = SelectedHubInstance.FolderName;
        var running = _gameLaunchService.IsInstanceRunning(folder);
        if (IsSelectedInstanceRunning != running)
        {
            IsSelectedInstanceRunning = running;
        }

        string label;
        if (IsHubLaunching)
        {
            label = Localizer.Instance["hub_launching"];
        }
        else if (running)
        {
            label = Localizer.Instance["hub_running"];
        }
        else
        {
            label = Localizer.Instance["hub_play"];
        }

        if (PlayButtonLabel != label)
        {
            PlayButtonLabel = label;
        }

        HubPrimaryActionCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedHubInstanceChanged(InstanceSummary? value)
    {
        RefreshPrimaryButtonUi();
    }

    partial void OnIsHubLaunchingChanged(bool value)
    {
        HubPrimaryActionCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSelectedInstanceRunningChanged(bool value)
    {
        HubPrimaryActionCommand.NotifyCanExecuteChanged();
    }

    private bool CanHubPrimaryAction()
    {
        if (HubInstances.Count == 0)
        {
            return true;
        }

        if (SelectedHubInstance is null)
        {
            return false;
        }

        if (IsHubLaunching)
        {
            return false;
        }

        if (IsSelectedInstanceRunning)
        {
            return false;
        }

        return true;
    }

    [RelayCommand(CanExecute = nameof(CanHubPrimaryAction))]
    private async Task HubPrimaryActionAsync()
    {
        try
        {
            if (HubInstances.Count == 0)
            {
                _mainRouter.Navigate("game", null, Localizer.Instance["titles_game"]);
                _launcherNavigation.PushAddInstance();
                return;
            }

            if (SelectedHubInstance is null)
            {
                return;
            }

            if (_gameLaunchService.IsInstanceRunning(SelectedHubInstance.FolderName))
            {
                await Dispatcher.UIThread.InvokeAsync(RefreshPrimaryButtonUi);
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsHubLaunching = true;
                RefreshPrimaryButtonUi();
            });

            try
            {
                await _gameLaunchService.LaunchInstanceAsync(SelectedHubInstance.FolderName).ConfigureAwait(false);
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsHubLaunching = false;
                    RefreshPrimaryButtonUi();
                });
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsHubLaunching = false;
                RefreshPrimaryButtonUi();
            });
            await _uiDialogs.ShowMessageAsync(Localizer.Instance["app_brand"], ex.Message).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void LinkClick(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }
}
