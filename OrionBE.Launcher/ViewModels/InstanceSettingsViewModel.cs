using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrionBE.Launcher.Core;
using OrionBE.Launcher.Core.Events;
using OrionBE.Launcher.Services;

namespace OrionBE.Launcher.ViewModels;

public sealed partial class InstanceSettingsViewModel : ViewModelBase
{
    private readonly IInstanceService _instanceService;
    private readonly INavigationService _navigationService;
    private readonly IUiDialogService _uiDialogService;
    private readonly IAppEventBus _appEventBus;
    private readonly IFileExplorerService _fileExplorerService;

    public ObservableCollection<InstanceModRowViewModel> ModRows { get; } = new();

    public InstanceSettingsViewModel(
        IInstanceService instanceService,
        INavigationService navigationService,
        IUiDialogService uiDialogService,
        IAppEventBus appEventBus,
        IFileExplorerService fileExplorerService)
    {
        _instanceService = instanceService;
        _navigationService = navigationService;
        _uiDialogService = uiDialogService;
        _appEventBus = appEventBus;
        _fileExplorerService = fileExplorerService;
    }

    [ObservableProperty]
    private string _folderName = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _gameVersion = string.Empty;

    [ObservableProperty]
    private bool _modsEnabled;

    [ObservableProperty]
    private string? _linuxUmuRunPath;

    [ObservableProperty]
    private string? _linuxProtonPath;

    [ObservableProperty]
    private string? _linuxWinePrefixPath;

    [ObservableProperty]
    private string? _bedrockWindowsExecutablePath;

    [ObservableProperty]
    private string? _bedrockVersionUuid;

    [ObservableProperty]
    private string? _instanceRootPath;

    public bool ShowLinuxGdkSection => OperatingSystem.IsLinux();

    public void Attach(string instanceFolderName)
    {
        FolderName = instanceFolderName;
        _ = LoadAsync();
    }

    [RelayCommand]
    private void Back() => _navigationService.GoBack();

    [RelayCommand]
    private void OpenInExplorer(string? path) => _fileExplorerService.RevealInFileManager(path);

    [RelayCommand]
    private async Task DeleteInstanceAsync()
    {
        var title = "Excluir instância";
        var message =
            $"A pasta e todos os ficheiros desta instância serão apagados de disco.\n\n" +
            $"“{DisplayName}”\n" +
            $"Isto não pode ser desfeito. Continuar?";
        if (!await _uiDialogService.ConfirmAsync(title, message).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            await _instanceService.DeleteInstanceAsync(FolderName).ConfigureAwait(false);
            _appEventBus.Publish(new InstancesChanged());
            _navigationService.GoBack();
        }
        catch (Exception ex)
        {
            await _uiDialogService.ShowMessageAsync("OrionBE", ex.Message).ConfigureAwait(true);
        }
    }

    public async Task PersistModEnabledAsync(string globalFolderName, bool enabled)
    {
        var summary = await _instanceService.GetAsync(FolderName).ConfigureAwait(false);
        if (summary is null)
        {
            return;
        }

        var entry = summary.Config.Mods.FirstOrDefault(m => m.GlobalFolderName == globalFolderName);
        if (entry is null)
        {
            return;
        }

        entry.Enabled = enabled;
        await _instanceService.SaveConfigAsync(FolderName, summary.Config).ConfigureAwait(false);
    }

    private async Task LoadAsync()
    {
        var summary = await _instanceService.GetAsync(FolderName).ConfigureAwait(false);
        if (summary is null)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            DisplayName = summary.Config.Name;
            GameVersion = summary.Config.Version;
            ModsEnabled = summary.Config.ModsEnabled;
            InstanceRootPath = OrionPaths.InstanceRoot(FolderName);
            LinuxUmuRunPath = summary.Config.LinuxUmuRunPath;
            LinuxProtonPath = summary.Config.LinuxProtonPath;
            LinuxWinePrefixPath = summary.Config.LinuxWinePrefixPath;
            BedrockWindowsExecutablePath = summary.Config.BedrockWindowsExecutablePath;
            BedrockVersionUuid = summary.Config.BedrockVersionUuid;

            ModRows.Clear();
            foreach (var mod in summary.Config.Mods)
            {
                ModRows.Add(new InstanceModRowViewModel(this, mod));
            }
        });
    }
}
