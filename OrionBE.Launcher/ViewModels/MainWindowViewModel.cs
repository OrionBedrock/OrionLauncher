using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrionBE.Launcher.Services;

namespace OrionBE.Launcher.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;

    public MainWindowViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        _navigation.Navigated += OnNavigated;
        _navigation.SetRootHome();
    }

    public ViewModelBase? CurrentPage => _navigation.CurrentViewModel;

    public bool IsMainSelected => _navigation.ActiveSection == SidebarSection.Main;

    public bool IsBrowseModsSelected => _navigation.ActiveSection == SidebarSection.BrowseMods;

    [RelayCommand]
    private void GoMain() => _navigation.SetRootHome();

    [RelayCommand]
    private void GoBrowseMods() => _navigation.SetRootBrowseMods();

    private void OnNavigated(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(IsMainSelected));
        OnPropertyChanged(nameof(IsBrowseModsSelected));
    }
}
