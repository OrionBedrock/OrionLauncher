using System.Collections.ObjectModel;
using Avalonia.Threading;
using OrionBE.Launcher.Models;
using OrionBE.Launcher.Services;

namespace OrionBE.Launcher.ViewModels;

public sealed class BrowseModsViewModel : ViewModelBase
{
    private readonly IApiService _apiService;
    private readonly INavigationService _navigationService;

    public ObservableCollection<ModCatalogItem> Mods { get; } = new();

    public BrowseModsViewModel(IApiService apiService, INavigationService navigationService)
    {
        _apiService = apiService;
        _navigationService = navigationService;
    }

    public async Task LoadAsync()
    {
        var mods = await _apiService.GetModsCatalogAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Mods.Clear();
            foreach (var m in mods)
            {
                Mods.Add(m);
            }
        });
    }

    public void OpenMod(ModCatalogItem mod) => _navigationService.PushModDetails(mod.Id);
}
