using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrionBE.Launcher.Core;
using OrionBE.Launcher.Core.Events;
using OrionBE.Launcher.Models;
using OrionBE.Launcher.Services;

namespace OrionBE.Launcher.ViewModels;

public sealed partial class InstanceSettingsViewModel : ViewModelBase
{
    private readonly IInstanceService _instanceService;
    private readonly INavigationService _navigationService;
    private readonly IUiDialogService _uiDialogService;
    private readonly IAppEventBus _appEventBus;
    private readonly IFileExplorerService _fileExplorerService;
    private readonly IModService _modService;

    public ObservableCollection<InstanceModRowViewModel> ModRows { get; } = new();

    public InstanceSettingsViewModel(
        IInstanceService instanceService,
        INavigationService navigationService,
        IUiDialogService uiDialogService,
        IAppEventBus appEventBus,
        IFileExplorerService fileExplorerService,
        IModService modService)
    {
        _instanceService = instanceService;
        _navigationService = navigationService;
        _uiDialogService = uiDialogService;
        _appEventBus = appEventBus;
        _fileExplorerService = fileExplorerService;
        _modService = modService;
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

    [ObservableProperty]
    private string? _leviLaminaVersion;

    [ObservableProperty]
    private string _importPath = string.Empty;

    [ObservableProperty]
    private string _importDisplayName = string.Empty;

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
    private async Task SaveLeviLaminaVersionAsync()
    {
        var summary = await _instanceService.GetAsync(FolderName).ConfigureAwait(false);
        if (summary is null)
        {
            return;
        }

        summary.Config.LeviLaminaVersion = string.IsNullOrWhiteSpace(LeviLaminaVersion)
            ? null
            : LeviLaminaVersion.Trim();
        await _instanceService.SaveConfigAsync(FolderName, summary.Config).ConfigureAwait(false);
        await LoadAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ImportZipAsync()
    {
        await ImportCoreAsync(isDll: false).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ImportDllAsync()
    {
        await ImportCoreAsync(isDll: true).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task DeleteInstanceAsync()
    {
        var title = "Delete instance";
        var message =
            $"The folder and all files for this instance will be deleted from disk.\n\n" +
            $"“{DisplayName}”\n" +
            $"This cannot be undone. Continue?";
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
        await SyncSingleModDeploymentAsync(summary, entry).ConfigureAwait(false);
        await _instanceService.SaveConfigAsync(FolderName, summary.Config).ConfigureAwait(false);
    }

    private async Task ImportCoreAsync(bool isDll)
    {
        var summary = await _instanceService.GetAsync(FolderName).ConfigureAwait(false);
        if (summary is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ImportPath))
        {
            await _uiDialogService.ShowMessageAsync("OrionBE", "Informe um caminho de arquivo local.").ConfigureAwait(true);
            return;
        }

        try
        {
            var folder = string.Empty;
            if (isDll)
            {
                folder = await _modService.ImportGlobalModDllAsync(
                        ImportPath.Trim(),
                        string.IsNullOrWhiteSpace(ImportDisplayName) ? Path.GetFileNameWithoutExtension(ImportPath) : ImportDisplayName.Trim(),
                        summary.Config.Version)
                    .ConfigureAwait(false);
            }
            else
            {
                var inspection = await _modService.AnalyzeModZipAsync(ImportPath.Trim()).ConfigureAwait(false);
                string? selectedDll = null;
                if (inspection.DllRelativePaths.Count > 1)
                {
                    selectedDll = await _uiDialogService.PickOptionAsync(
                            "Select primary DLL",
                            "Multiple .dll files were found in this ZIP. Choose the main one used by this mod:",
                            inspection.DllRelativePaths)
                        .ConfigureAwait(true);
                    if (selectedDll is null)
                    {
                        return;
                    }
                }
                else if (inspection.DllRelativePaths.Count == 1)
                {
                    selectedDll = inspection.DllRelativePaths[0];
                }

                var normalize = false;
                if (inspection.NeedsNormalization)
                {
                    normalize = await _uiDialogService.ConfirmAsync(
                            "Fix mod folder layout?",
                            "Current mod structure places .dll files in a nested folder.\n\n" +
                            $"Current example: {inspection.CurrentLayoutExample}\n" +
                            $"Recommended: {inspection.RecommendedLayoutExample}\n\n" +
                            "Do you want OrionBE to normalize this ZIP layout before import?")
                        .ConfigureAwait(true);
                }

                folder = await _modService.ImportGlobalModZipAsync(
                        ImportPath.Trim(),
                        string.IsNullOrWhiteSpace(ImportDisplayName) ? "Imported mod" : ImportDisplayName.Trim(),
                        summary.Config.Version,
                        new ModZipImportOptions
                        {
                            NormalizeToDllDirectory = normalize,
                            PrimaryDllRelativePath = selectedDll,
                        })
                    .ConfigureAwait(false);
            }

            await _modService.CopyGlobalModIntoInstanceAsync(FolderName, folder).ConfigureAwait(false);
            if (summary.Config.Mods.All(m => m.GlobalFolderName != folder))
            {
                summary.Config.Mods.Add(new InstalledModEntry
                {
                    GlobalFolderName = folder,
                    Enabled = true,
                });
            }

            var imported = summary.Config.Mods.First(m => m.GlobalFolderName == folder);
            await SyncSingleModDeploymentAsync(summary, imported).ConfigureAwait(false);
            await _instanceService.SaveConfigAsync(FolderName, summary.Config).ConfigureAwait(false);
            _appEventBus.Publish(new InstancesChanged());
            await LoadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _uiDialogService.ShowMessageAsync("OrionBE", ex.Message).ConfigureAwait(true);
        }
    }

    private async Task LoadAsync()
    {
        var summary = await _instanceService.GetAsync(FolderName).ConfigureAwait(false);
        if (summary is null)
        {
            return;
        }

        var rows = new List<InstanceModRowViewModel>();
        await SyncAllModDeploymentsAsync(summary).ConfigureAwait(false);
        await _instanceService.SaveConfigAsync(FolderName, summary.Config).ConfigureAwait(false);
        foreach (var mod in summary.Config.Mods)
        {
            var cfg = await _modService.GetGlobalModConfigAsync(mod.GlobalFolderName).ConfigureAwait(false);
            var report = _modService.EvaluateCompatibility(summary.Config, cfg);
            rows.Add(new InstanceModRowViewModel(this, mod, cfg, report));
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
            LeviLaminaVersion = summary.Config.LeviLaminaVersion;

            ModRows.Clear();
            foreach (var row in rows)
            {
                ModRows.Add(row);
            }
        });
    }

    private async Task SyncAllModDeploymentsAsync(InstanceSummary summary)
    {
        foreach (var mod in summary.Config.Mods)
        {
            await SyncSingleModDeploymentAsync(summary, mod).ConfigureAwait(false);
        }
    }

    private async Task SyncSingleModDeploymentAsync(InstanceSummary summary, InstalledModEntry mod)
    {
        var instanceModsRoot = OrionPaths.InstanceMods(summary.FolderName);
        var gameModsRoot = Path.Combine(OrionPaths.InstanceGame(summary.FolderName), "mods");
        var source = Path.Combine(instanceModsRoot, mod.GlobalFolderName);
        var target = Path.Combine(gameModsRoot, mod.GlobalFolderName);

        if (mod.Enabled)
        {
            if (!Directory.Exists(source))
            {
                await _modService.CopyGlobalModIntoInstanceAsync(summary.FolderName, mod.GlobalFolderName).ConfigureAwait(false);
            }

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.CreateDirectory(gameModsRoot);
            CopyDirectoryRecursive(source, target);
            return;
        }

        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            var sub = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, sub);
        }
    }
}
