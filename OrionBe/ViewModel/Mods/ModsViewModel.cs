using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using OrionBE.Launcher.Services;
using OrionBE.Launcher.ViewModels;
using OrionBe.ViewModel.Shared;

namespace OrionBe.ViewModel.Mods;

public partial class ModsViewModel : MainWindowViewModelBase
{
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private ViewModelBase? _shellPage;

    public ModsViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        _navigation.Navigated += (_, _) => ShellPage = _navigation.CurrentViewModel;
    }

    public override Task OnNavigatedToAsync(CancellationToken ctx)
    {
        _navigation.SetRootBrowseMods();
        ShellPage = _navigation.CurrentViewModel;
        return Task.CompletedTask;
    }
}
