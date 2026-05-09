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
        LeviLaminaVersion = summary.Config.LeviLaminaVersion;
    }

    public string FolderName { get; }

    public string DisplayName { get; }

    public string GameVersion { get; }

    public bool IsModded { get; }
    public string? LeviLaminaVersion { get; }

    public string ModeLabel => IsModded ? "Modded" : "Vanilla";
    public string RuntimeLabel => string.IsNullOrWhiteSpace(LeviLaminaVersion)
        ? "Runtime: none"
        : $"Runtime: LeviLamina {LeviLaminaVersion}";

    [ObservableProperty]
    private bool isLaunching;

    public bool IsLaunchControlsEnabled => !IsLaunching;
    public string PlayButtonText => IsLaunching ? "Launching..." : "Play";

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (IsLaunching)
        {
            return;
        }

        IsLaunching = true;
        try
        {
            if (_gameLaunchService.IsInstanceRunning(FolderName))
            {
                await _uiDialogService.ShowMessageAsync("OrionBE", "This instance is already running.").ConfigureAwait(false);
                return;
            }

            await _gameLaunchService.LaunchInstanceAsync(FolderName).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _uiDialogService.ShowMessageAsync("OrionBE", ex.Message).ConfigureAwait(false);
        }
        finally
        {
            IsLaunching = false;
        }
    }

    [RelayCommand]
    private void OpenSettings() => _navigation.PushInstanceSettings(FolderName);

    private bool CanPlay() => !IsLaunching;

    partial void OnIsLaunchingChanged(bool value)
    {
        PlayCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsLaunchControlsEnabled));
        OnPropertyChanged(nameof(PlayButtonText));
    }
}
