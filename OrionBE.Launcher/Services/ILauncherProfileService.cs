namespace OrionBE.Launcher.Services;

/// <summary>
/// Local launcher profile (nickname, short message, optional avatar image). Persisted under ~/OrionBE.
/// </summary>
public interface ILauncherProfileService
{
    LauncherProfileSnapshot GetSnapshot();

    /// <summary>Persists nickname and tagline after trimming and enforcing max lengths.</summary>
    void SaveNicknameAndTagline(string? nickname, string? tagline);

    /// <summary>Copies a user-picked image into the launcher cache and stores its extension in settings.</summary>
    /// <returns>Full path to the cached avatar file, or null if copy failed.</returns>
    string? InstallAvatarFromUserFile(string sourcePath);

    /// <summary>Deletes cached avatar and clears the extension in settings.</summary>
    void ClearAvatar();
}

/// <param name="Nickname">Display name in the sidebar.</param>
/// <param name="Tagline">Short line below the nickname.</param>
/// <param name="AvatarFullPath">Path to the cached image file if it exists.</param>
public sealed record LauncherProfileSnapshot(string? Nickname, string? Tagline, string? AvatarFullPath);
