using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using OrionBE.Launcher.Core.Events;
using OrionBE.Launcher.Services;

namespace OrionBE.Launcher.ViewModels;

public sealed partial class HomeViewModel : ViewModelBase
{
    private readonly IInstanceService _instanceService;
    private readonly INavigationService _navigation;
    private readonly IAppEventBus _eventBus;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly IUiDialogService _uiDialogService;
    private readonly IDisposable _instancesChanged;

    public ObservableCollection<InstanceCardViewModel> Instances { get; } = new();

    public HomeViewModel(
        IInstanceService instanceService,
        INavigationService navigation,
        IAppEventBus eventBus,
        IGameLaunchService gameLaunchService,
        IUiDialogService uiDialogService)
    {
        _instanceService = instanceService;
        _navigation = navigation;
        _eventBus = eventBus;
        _gameLaunchService = gameLaunchService;
        _uiDialogService = uiDialogService;
        _instancesChanged = _eventBus.Subscribe<InstancesChanged>(_ => RequestReload());
    }

    private void RequestReload() => _ = LoadAsync();

    public async Task LoadAsync()
    {
        var list = await _instanceService.ListInstancesAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Instances.Clear();
            foreach (var item in list.OrderBy(static i => i.Config.Name, StringComparer.OrdinalIgnoreCase))
            {
                Instances.Add(new InstanceCardViewModel(item, _navigation, _gameLaunchService, _uiDialogService));
            }
        });
    }

    [RelayCommand]
    private void AddInstance() => _navigation.PushAddInstance();

}
