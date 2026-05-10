using System.Text.Json;
using OrionBE.Launcher.Core;
using OrionBE.Launcher.Infrastructure.Json;
using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

public sealed class LauncherSettingsService : ILauncherSettingsService
{
    public LauncherUserSettings Load()
    {
        try
        {
            var path = OrionPaths.LauncherSettingsFile;
            if (!File.Exists(path))
            {
                return new LauncherUserSettings();
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<LauncherUserSettings>(json, LauncherJson.Options);
            return loaded ?? new LauncherUserSettings();
        }
        catch
        {
            return new LauncherUserSettings();
        }
    }

    public void Save(LauncherUserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(OrionPaths.Root);
            var json = JsonSerializer.Serialize(settings, LauncherJson.Options);
            File.WriteAllText(OrionPaths.LauncherSettingsFile, json);
        }
        catch
        {
            // Settings must never crash the launcher.
        }
    }
}
