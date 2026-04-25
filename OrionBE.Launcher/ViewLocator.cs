using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using OrionBE.Launcher.ViewModels;

namespace OrionBE.Launcher;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is not ViewModelBase)
        {
            return null;
        }

        if (param is MainWindowViewModel)
        {
            return null;
        }

        var vmType = param.GetType();
        var vmName = vmType.Name;
        if (!vmName.EndsWith("ViewModel", StringComparison.Ordinal))
        {
            return new TextBlock { Text = "Unknown ViewModel: " + vmName };
        }

        var viewName = string.Concat(vmName.AsSpan(0, vmName.Length - "ViewModel".Length), "View");
        var viewNamespace = vmType.Namespace?.Replace(".ViewModels", ".Views", StringComparison.Ordinal);
        var assemblyName = vmType.Assembly.GetName().Name;
        var fullName = $"{viewNamespace}.{viewName}, {assemblyName}";
        var resolved = Type.GetType(fullName);
        if (resolved is null)
        {
            return new TextBlock { Text = "View not found: " + fullName };
        }

        return (Control)Activator.CreateInstance(resolved)!;
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
