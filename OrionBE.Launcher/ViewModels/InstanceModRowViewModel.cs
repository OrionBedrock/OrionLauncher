using CommunityToolkit.Mvvm.ComponentModel;
using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.ViewModels;

public sealed partial class InstanceModRowViewModel : ViewModelBase
{
    private readonly InstanceSettingsViewModel _owner;
    private readonly bool _suppressPersistence;

    public InstanceModRowViewModel(InstanceSettingsViewModel owner, InstalledModEntry entry)
    {
        _owner = owner;
        GlobalFolderName = entry.GlobalFolderName;
        _suppressPersistence = true;
        IsEnabled = entry.Enabled;
        _suppressPersistence = false;
    }

    public InstanceModRowViewModel(
        InstanceSettingsViewModel owner,
        InstalledModEntry entry,
        ModConfig? config,
        ModCompatibilityReport compatibility)
        : this(owner, entry)
    {
        DisplayName = string.IsNullOrWhiteSpace(config?.Name) ? entry.GlobalFolderName : config.Name;
        Version = config?.Version ?? "unknown";
        RequiresLeviLamina = compatibility.RequiresLeviLamina;
        CompatibilitySummary = compatibility.Summary;
        IsCompatible = compatibility.GameVersionCompatible
                       && compatibility.LeviLaminaCompatible
                       && compatibility.ApiCompatible;
    }

    public string GlobalFolderName { get; }
    public string DisplayName { get; } = string.Empty;
    public string Version { get; } = "unknown";
    public bool RequiresLeviLamina { get; }
    public bool IsCompatible { get; } = true;
    public string CompatibilitySummary { get; } = "Compatible";

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressPersistence)
        {
            return;
        }

        _ = _owner.PersistModEnabledAsync(GlobalFolderName, value);
    }
}
