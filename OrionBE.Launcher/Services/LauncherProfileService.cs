using OrionBE.Launcher.Core;
using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

/// <inheritdoc />
public sealed class LauncherProfileService : ILauncherProfileService
{
    public const int MaxNicknameLength = 24;

    public const int MaxTaglineLength = 120;

    private readonly ILauncherSettingsService _settings;

    public LauncherProfileService(ILauncherSettingsService settings)
    {
        _settings = settings;
    }

    /// <inheritdoc />
    public LauncherProfileSnapshot GetSnapshot()
    {
        var s = _settings.Load();
        string? avatarPath = null;
        if (!string.IsNullOrEmpty(s.ProfileAvatarExtension))
        {
            var p = AvatarCachePath(s.ProfileAvatarExtension);
            if (File.Exists(p))
            {
                avatarPath = p;
            }
        }

        return new LauncherProfileSnapshot(s.ProfileNickname, s.ProfileTagline, avatarPath);
    }

    /// <inheritdoc />
    public void SaveNicknameAndTagline(string? nickname, string? tagline)
    {
        var s = _settings.Load();
        s.ProfileNickname = Clamp(nickname, MaxNicknameLength);
        s.ProfileTagline = Clamp(tagline, MaxTaglineLength);
        _settings.Save(s);
    }

    /// <inheritdoc />
    public string? InstallAvatarFromUserFile(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(OrionPaths.Cache);
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext.Length < 2 || ext.Length > 5 || !ext.StartsWith(".", StringComparison.Ordinal))
            {
                ext = ".png";
            }

            // Allowed raster-ish extensions for avatar display
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp",
            };
            if (!allowed.Contains(ext))
            {
                ext = ".png";
            }

            var dest = AvatarCachePath(ext);
            File.Copy(sourcePath, dest, overwrite: true);

            var s = _settings.Load();
            s.ProfileAvatarExtension = ext;
            _settings.Save(s);
            return dest;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void ClearAvatar()
    {
        var s = _settings.Load();
        if (!string.IsNullOrEmpty(s.ProfileAvatarExtension))
        {
            TryDelete(AvatarCachePath(s.ProfileAvatarExtension));
        }

        s.ProfileAvatarExtension = null;
        _settings.Save(s);
    }

    private static string AvatarCachePath(string extension) =>
        Path.Combine(OrionPaths.Cache, "local_profile_avatar" + extension);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignored
        }
    }

    private static string? Clamp(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text.Trim();
        return t.Length <= max ? t : t[..max];
    }
}
