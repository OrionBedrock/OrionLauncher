using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrionBE.Launcher.Models;
using OrionBE.Launcher.Services;

namespace OrionBE.Launcher.ViewModels;

public sealed partial class InstanceCardViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly IUiDialogService _uiDialogService;

    public InstanceCardViewModel(
        InstanceSummary summary,
        INavigationService navigation,
        IGameLaunchService gameLaunchService,
        IUiDialogService uiDialogService)
    {
        _navigation = navigation;
        _gameLaunchService = gameLaunchService;
        _uiDialogService = uiDialogService;
        FolderName = summary.FolderName;
        DisplayName = summary.Config.Name;
        GameVersion = summary.Config.Version;
        IsModded = summary.Config.ModsEnabled;
    }

    public string FolderName { get; }

    public string DisplayName { get; }

    public string GameVersion { get; }

    public bool IsModded { get; }

    public string ModeLabel => IsModded ? "Modded" : "Vanilla";

    [RelayCommand]
    private async Task PlayAsync()
    {
        try
        {
            await _gameLaunchService.LaunchInstanceAsync(FolderName).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _uiDialogService.ShowMessageAsync("OrionBE", ex.Message).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void OpenSettings() => _navigation.PushInstanceSettings(FolderName);
}
