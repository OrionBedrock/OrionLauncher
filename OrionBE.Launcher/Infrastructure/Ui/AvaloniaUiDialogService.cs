using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OrionBE.Launcher.Models;
using OrionBE.Launcher.Services;
using OrionBE.Launcher.ViewModels;
using OrionBE.Launcher.Views;

namespace OrionBE.Launcher.Infrastructure.Ui;

public sealed class AvaloniaUiDialogService : IUiDialogService
{
    private readonly IInstanceService _instanceService;
    private Window? _mainWindow;

    public AvaloniaUiDialogService(IInstanceService instanceService)
    {
        _instanceService = instanceService;
    }

    public void AttachMainWindow(Window window) => _mainWindow = window;

    public Task ShowMessageAsync(string title, string message) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var owner = _mainWindow ?? throw new InvalidOperationException("Main window is not attached.");
            var dlg = new Window
            {
                Title = title,
                Width = 480,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                        new Button
                        {
                            Content = "OK",
                            MinWidth = 96,
                            HorizontalAlignment = HorizontalAlignment.Right,
                        },
                    },
                },
            };

            if (dlg.Content is StackPanel root && root.Children[1] is Button ok)
            {
                ok.Click += (_, _) => dlg.Close();
            }

            await dlg.ShowDialog(owner).ConfigureAwait(true);
        });

    public Task<bool> ConfirmAsync(string title, string message) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var owner = _mainWindow ?? throw new InvalidOperationException("Main window is not attached.");
            var result = false;

            var dlg = new Window
            {
                Title = title,
                Width = 480,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
            };

            var ok = new Button { Content = "OK", MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Cancel", MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Right };

            ok.Click += (_, _) =>
            {
                result = true;
                dlg.Close();
            };

            cancel.Click += (_, _) =>
            {
                result = false;
                dlg.Close();
            };

            dlg.Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancel, ok },
                    },
                },
            };

            await dlg.ShowDialog(owner).ConfigureAwait(true);
            return result;
        });

    public Task<InstanceSummary?> PickModdedInstanceAsync(CancellationToken cancellationToken = default) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var owner = _mainWindow ?? throw new InvalidOperationException("Main window is not attached.");
            var all = await _instanceService.ListInstancesAsync(cancellationToken).ConfigureAwait(true);
            var moddable = all.Where(i => i.Config.ModsEnabled).ToList();

            if (moddable.Count == 0)
            {
                await ShowMessageAsync(
                        "No instances",
                        "There are no instances with mods enabled. Enable mods on an instance or create a new mod-enabled instance.")
                    .ConfigureAwait(true);
                return null;
            }

            var window = new InstancePickerWindow
            {
                Title = "Select instance",
                DataContext = new InstancePickerViewModel(moddable),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            await window.ShowDialog(owner).ConfigureAwait(true);
            return window.SelectedResult;
        });
}
