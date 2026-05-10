using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using OrionBE.Launcher.Services;
using OrionBE.Launcher.I18n;
using OrionBe.ViewModel.Shared;

namespace OrionBe.ViewModel.Settings;

public partial class SettingsViewModel : MainWindowViewModelBase
{
    private readonly ILauncherSettingsService _launcherSettings;
    private readonly string _baseDirectory = Path.Combine(AppContext.BaseDirectory, "I18n");

    [ObservableProperty]
    private LanguageItem? _selectedLanguage;

    public ObservableCollection<LanguageItem> Languages { get; } = new();

    public SettingsViewModel(ILauncherSettingsService launcherSettings)
    {
        _launcherSettings = launcherSettings;
    }

    public override Task OnNavigatedToAsync(CancellationToken ctx)
    {
        Languages.Clear();

        foreach (var file in Directory.GetFiles(_baseDirectory, "*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(file);

            try
            {
                var culture = new CultureInfo(code);
                var lang = new LanguageItem(culture.NativeName, code);
                Languages.Add(lang);

                if (string.Equals(code, Localizer.Instance.Language, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedLanguage = lang;
                }
            }
            catch
            {
                // Ignore invalid culture filenames.
            }
        }

        return Task.CompletedTask;
    }

    partial void OnSelectedLanguageChanged(LanguageItem? value)
    {
        if (value is null)
        {
            return;
        }

        Localizer.Instance.LoadLanguage(value.Code);
        var merged = _launcherSettings.Load();
        merged.UiLanguage = value.Code;
        _launcherSettings.Save(merged);
    }
}

public sealed class LanguageItem
{
    public string Name { get; }

    public string Code { get; }

    public LanguageItem(string name, string code)
    {
        Name = name;
        Code = code;
    }
}
