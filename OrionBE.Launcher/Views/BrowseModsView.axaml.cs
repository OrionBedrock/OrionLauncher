using Avalonia.Controls;
using Avalonia.Interactivity;
using OrionBE.Launcher.Models;
using OrionBE.Launcher.ViewModels;

namespace OrionBE.Launcher.Views;

public partial class BrowseModsView : UserControl
{
    public BrowseModsView()
    {
        InitializeComponent();
    }

    private void Details_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModCatalogItem mod })
        {
            return;
        }

        if (DataContext is BrowseModsViewModel vm)
        {
            vm.OpenMod(mod);
        }
    }
}
