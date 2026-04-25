using Avalonia.Controls;
using Avalonia.Interactivity;
using OrionBE.Launcher.Models;
using OrionBE.Launcher.ViewModels;

namespace OrionBE.Launcher.Views;

public partial class InstancePickerWindow : Window
{
    public InstanceSummary? SelectedResult { get; private set; }

    public InstancePickerWindow()
    {
        InitializeComponent();
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InstancePickerViewModel vm)
        {
            SelectedResult = vm.SelectedItem;
        }

        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        SelectedResult = null;
        Close();
    }
}
