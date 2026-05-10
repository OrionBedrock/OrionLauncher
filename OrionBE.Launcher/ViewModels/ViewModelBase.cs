using CommunityToolkit.Mvvm.ComponentModel;
using OrionBE.Launcher.I18n;

namespace OrionBE.Launcher.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected static string L(string key) => Localizer.Instance[key];

    protected static string LF(string key, params object?[] args) => Localizer.Instance.Format(key, args);
}