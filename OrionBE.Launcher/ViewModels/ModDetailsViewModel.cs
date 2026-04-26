using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrionBE.Launcher.Core;
using OrionBE.Launcher.Core.Events;
using OrionBE.Launcher.Models;
using OrionBE.Launcher.Services;

namespace OrionBE.Launcher.ViewModels;

public sealed partial class ModDetailsViewModel : ViewModelBase
{
    private readonly IApiService _apiService;
    private readonly IModService _modService;
    private readonly IInstanceService _instanceService;
    private readonly INavigationService _navigationService;
    private readonly IUiDialogService _uiDialogService;
    private readonly IAppEventBus _eventBus;

    public ObservableCollection<ModVersion> Versions { get; } = new();
    public ObservableCollection<string> ScreenshotPaths { get; } = new();

    public ModDetailsViewModel(
        IApiService apiService,
        IModService modService,
        IInstanceService instanceService,
        INavigationService navigationService,
        IUiDialogService uiDialogService,
        IAppEventBus eventBus)
    {
        _apiService = apiService;
        _modService = modService;
        _instanceService = instanceService;
        _navigationService = navigationService;
        _uiDialogService = uiDialogService;
        _eventBus = eventBus;
    }

    [ObservableProperty]
    private ModCatalogItem? _mod;

    [ObservableProperty]
    private string _headerTitle = "Mod details";

    [ObservableProperty]
    private ModVersion? _selectedVersion;

    [ObservableProperty]
    private string _fullDescription = string.Empty;

    public void Attach(string modId) => _ = LoadAsync(modId);

    [RelayCommand]
    private void Back() => _navigationService.GoBack();

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void Install() => _ = InstallCoreAsync();

    private async Task InstallCoreAsync()
    {
        if (Mod is null || SelectedVersion is null)
        {
            return;
        }

        var target = await _uiDialogService.PickModdedInstanceAsync().ConfigureAwait(true);
        if (target is null)
        {
            return;
        }

        var supportedVersions = SelectedVersion.SupportedGameVersions.Count > 0
            ? SelectedVersion.SupportedGameVersions
            : [SelectedVersion.SupportedGameVersion];
        if (!supportedVersions.Any(v => string.Equals(v, target.Config.Version, StringComparison.OrdinalIgnoreCase)))
        {
            var proceed = await _uiDialogService
                .ConfirmAsync(
                    "Version mismatch",
                    $"The instance is on {target.Config.Version}, but this mod targets {string.Join(", ", supportedVersions)}. Continue anyway?")
                .ConfigureAwait(true);

            if (!proceed)
            {
                return;
            }

            _eventBus.Publish(
                new ModInstallWarning(
                    $"Installed with version mismatch: instance {target.Config.Version}, mod {string.Join(", ", supportedVersions)}."));
        }

        if (SelectedVersion.RequiresLeviLamina)
        {
            if (string.IsNullOrWhiteSpace(target.Config.LeviLaminaVersion))
            {
                await _uiDialogService
                    .ShowMessageAsync(
                        "LeviLamina required",
                        "This mod requires LeviLamina, but the instance does not have a LeviLamina version configured in Instance Settings.")
                    .ConfigureAwait(true);
                return;
            }

            if (!VersionRangeMatcher.Matches(target.Config.LeviLaminaVersion, SelectedVersion.LeviLaminaVersionRange))
            {
                var proceed = await _uiDialogService
                    .ConfirmAsync(
                        "LeviLamina mismatch",
                        $"Instance LeviLamina={target.Config.LeviLaminaVersion}, required range={SelectedVersion.LeviLaminaVersionRange}. Continue anyway?")
                    .ConfigureAwait(true);
                if (!proceed)
                {
                    return;
                }
            }
        }

        var globalFolder = await _modService.EnsureGlobalModFromCatalogAsync(Mod, SelectedVersion).ConfigureAwait(true);
        await _modService.CopyGlobalModIntoInstanceAsync(target.FolderName, globalFolder).ConfigureAwait(true);

        var updated = await _instanceService.GetAsync(target.FolderName).ConfigureAwait(true);
        if (updated is null)
        {
            return;
        }

        if (updated.Config.Mods.All(m => m.GlobalFolderName != globalFolder))
        {
            updated.Config.Mods.Add(new InstalledModEntry { GlobalFolderName = globalFolder, Enabled = true });
            await _instanceService.SaveConfigAsync(updated.FolderName, updated.Config).ConfigureAwait(true);
        }

        _eventBus.Publish(new InstancesChanged());
        await _uiDialogService.ShowMessageAsync("Installed", "The mod was copied into the instance mods folder.").ConfigureAwait(true);
        _navigationService.GoBack();
    }

    partial void OnSelectedVersionChanged(ModVersion? value) => InstallCommand.NotifyCanExecuteChanged();

    partial void OnModChanged(ModCatalogItem? value) => InstallCommand.NotifyCanExecuteChanged();

    private bool CanInstall() => Mod is not null && SelectedVersion is not null;

    private async Task LoadAsync(string modId)
    {
        var mod = await _apiService.GetModByIdAsync(modId).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Mod = mod;
            if (mod is null)
            {
                HeaderTitle = "Mod details";
                return;
            }

            HeaderTitle = mod.Name;
            FullDescription = mod.FullDescription;
            Versions.Clear();
            foreach (var v in mod.Versions)
            {
                Versions.Add(v);
            }

            SelectedVersion = Versions.FirstOrDefault();

            ScreenshotPaths.Clear();
            foreach (var relative in mod.ScreenshotRelativePaths)
            {
                var candidates = new[]
                {
                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relative)),
                    Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, relative)),
                };

                var hit = candidates.FirstOrDefault(File.Exists);
                if (hit is not null)
                {
                    ScreenshotPaths.Add(hit);
                }
            }
        });
    }
}
