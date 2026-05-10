using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using OrionBE.Launcher.Services;
using OrionBE.Launcher.ViewModels;
using OrionBe.ViewModel.Shared;

namespace OrionBe.ViewModel.Game;

public partial class GameViewModel : MainWindowViewModelBase
{
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private ViewModelBase? _shellPage;

    public GameViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        _navigation.Navigated += (_, _) => ShellPage = _navigation.CurrentViewModel;
    }

    public override Task OnNavigatedToAsync(CancellationToken ctx)
    {
        _navigation.SetRootHome();
        ShellPage = _navigation.CurrentViewModel;
        return Task.CompletedTask;
    }
}
