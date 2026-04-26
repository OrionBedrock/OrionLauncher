using Avalonia.Controls;
using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

public interface IUiDialogService
{
    void AttachMainWindow(Window window);

    Task ShowMessageAsync(string title, string message);

    Task<bool> ConfirmAsync(string title, string message);

    Task<string?> PickOptionAsync(string title, string message, IReadOnlyList<string> options);

    Task<InstanceSummary?> PickModdedInstanceAsync(CancellationToken cancellationToken = default);
}
