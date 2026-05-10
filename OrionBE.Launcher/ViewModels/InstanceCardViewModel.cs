using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrionBE.Launcher.Models;
using OrionBE.Launcher.Services;

namespace OrionBE.Launcher.ViewModels;

public sealed partial class InstanceCardViewModel : ViewModelBase, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly IUiDialogService _uiDialogService;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _runningPollCts;

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

        _ = InitializeRunningStateAsync();
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

    [ObservableProperty]
    private bool isGameRunning;

    /// <summary>Play stays disabled while launching or while the game process for this instance is detected.</summary>
    public bool IsPlayEnabled => !IsLaunching && !IsGameRunning;

    public string PlayButtonText =>
        IsLaunching ? "Launching..." :
        IsGameRunning ? "Running..." :
        "Play";

    public void Dispose()
    {
        try
        {
            _lifetimeCts.Cancel();
        }
        catch
        {
        }

        try
        {
            _runningPollCts?.Cancel();
            _runningPollCts?.Dispose();
            _runningPollCts = null;
        }
        catch
        {
        }

        try
        {
            _lifetimeCts.Dispose();
        }
        catch
        {
        }
    }

    private async Task InitializeRunningStateAsync()
    {
        try
        {
            await Task.Delay(50, _lifetimeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_gameLaunchService.IsInstanceRunning(FolderName))
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => IsGameRunning = true);
        StartRunningExitPoll(fromColdStart: true);
    }

    /// <summary>
    /// Polls until the game is no longer detected. After a fresh launch, a short grace window avoids false negatives
    /// while Wine/umu spawn child processes.
    /// </summary>
    private void StartRunningExitPoll(bool fromColdStart)
    {
        _runningPollCts?.Cancel();
        _runningPollCts?.Dispose();
        _runningPollCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var token = _runningPollCts.Token;
        _ = PollGameRunningUntilExitAsync(fromColdStart, token);
    }

    private async Task PollGameRunningUntilExitAsync(bool fromColdStart, CancellationToken ct)
    {
        var graceUntil = fromColdStart ? (DateTimeOffset?)null : DateTimeOffset.UtcNow.AddSeconds(30);
        var misses = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1500, ct).ConfigureAwait(false);
                var running = _gameLaunchService.IsInstanceRunning(FolderName);
                if (running)
                {
                    misses = 0;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!IsGameRunning)
                        {
                            IsGameRunning = true;
                        }
                    });
                    continue;
                }

                if (graceUntil is not null && DateTimeOffset.UtcNow < graceUntil.Value)
                {
                    misses = 0;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!IsGameRunning)
                        {
                            IsGameRunning = true;
                        }
                    });
                    continue;
                }

                misses++;
                if (misses >= 2)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => IsGameRunning = false);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (IsLaunching || IsGameRunning)
        {
            return;
        }

        IsLaunching = true;
        try
        {
            if (_gameLaunchService.IsInstanceRunning(FolderName))
            {
                IsGameRunning = true;
                StartRunningExitPoll(fromColdStart: true);
                return;
            }

            await _gameLaunchService.LaunchInstanceAsync(FolderName).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsGameRunning = true;
            });
            StartRunningExitPoll(fromColdStart: false);
        }
        catch (Exception ex)
        {
            // Dialog service marshals to UI thread; do not use ConfigureAwait(false) before this await.
            await _uiDialogService.ShowMessageAsync("OrionBE", ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLaunching = false;
            });
        }
    }

    [RelayCommand]
    private void OpenSettings() => _navigation.PushInstanceSettings(FolderName);

    private bool CanPlay() => IsPlayEnabled;

    partial void OnIsLaunchingChanged(bool value)
    {
        PlayCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsPlayEnabled));
        OnPropertyChanged(nameof(PlayButtonText));
    }

    partial void OnIsGameRunningChanged(bool value)
    {
        PlayCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsPlayEnabled));
        OnPropertyChanged(nameof(PlayButtonText));
    }
}
