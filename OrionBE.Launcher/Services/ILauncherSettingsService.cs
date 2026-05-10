using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

public interface ILauncherSettingsService
{
    LauncherUserSettings Load();

    void Save(LauncherUserSettings settings);
}
