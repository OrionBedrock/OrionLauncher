using Microsoft.Extensions.DependencyInjection;
using OrionBE.Launcher.ViewModels;

namespace OrionBE.Launcher.Services;

public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Stack<ViewModelBase?> _stack = new();

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public event EventHandler? Navigated;

    public ViewModelBase? CurrentViewModel { get; private set; }

    public SidebarSection ActiveSection { get; private set; } = SidebarSection.Main;

    public void SetRootHome()
    {
        TryDisposeLeaving(CurrentViewModel);
        _stack.Clear();
        var home = _serviceProvider.GetRequiredService<HomeViewModel>();
        CurrentViewModel = home;
        ActiveSection = SidebarSection.Main;
        _ = home.LoadAsync();
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    public void SetRootBrowseMods()
    {
        TryDisposeLeaving(CurrentViewModel);
        _stack.Clear();
        var browse = _serviceProvider.GetRequiredService<BrowseModsViewModel>();
        CurrentViewModel = browse;
        ActiveSection = SidebarSection.BrowseMods;
        _ = browse.LoadAsync();
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    public void PushAddInstance()
    {
        _stack.Push(CurrentViewModel);
        CurrentViewModel = _serviceProvider.GetRequiredService<AddInstanceViewModel>();
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    public void PushInstanceSettings(string instanceFolderName)
    {
        _stack.Push(CurrentViewModel);
        var vm = _serviceProvider.GetRequiredService<InstanceSettingsViewModel>();
        vm.Attach(instanceFolderName);
        CurrentViewModel = vm;
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    public void PushModDetails(string modId)
    {
        _stack.Push(CurrentViewModel);
        var vm = _serviceProvider.GetRequiredService<ModDetailsViewModel>();
        vm.Attach(modId);
        CurrentViewModel = vm;
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    public bool GoBack()
    {
        if (_stack.Count == 0)
        {
            return false;
        }

        TryDisposeLeaving(CurrentViewModel);
        CurrentViewModel = _stack.Pop();
        Navigated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static void TryDisposeLeaving(ViewModelBase? viewModel)
    {
        if (viewModel is HomeViewModel or BrowseModsViewModel)
        {
            return;
        }

        TryDispose(viewModel);
    }

    private static void TryDispose(object? viewModel)
    {
        if (viewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
