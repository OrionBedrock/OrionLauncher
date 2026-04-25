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

    public string GlobalFolderName { get; }

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
