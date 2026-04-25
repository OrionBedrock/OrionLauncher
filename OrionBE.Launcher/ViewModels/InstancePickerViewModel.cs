using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.ViewModels;

public sealed partial class InstancePickerViewModel : ViewModelBase
{
    public ObservableCollection<InstanceSummary> Instances { get; }

    [ObservableProperty]
    private InstanceSummary? _selectedItem;

    public InstancePickerViewModel(IEnumerable<InstanceSummary> instances)
    {
        Instances = new ObservableCollection<InstanceSummary>(instances);
    }
}
