namespace OrionBE.Launcher.Models;

/// <summary>
/// Persisted launcher preferences (UI culture, etc.).
/// </summary>
public sealed class LauncherUserSettings
{
    /// <summary>BCP 47 language tag, e.g. <c>en-US</c>, <c>pt-BR</c>.</summary>
    public string UiLanguage { get; set; } = "en-US";

    /// <summary>Last launched or selected instance folder name under <c>~/OrionBE/instances</c>.</summary>
    public string? LastPlayedInstanceFolderName { get; set; }

    /// <summary>Sidebar display name (local profile).</summary>
    public string? ProfileNickname { get; set; }

    /// <summary>Short message below the nickname (character limit enforced when saving).</summary>
    public string? ProfileTagline { get; set; }

    /// <summary>Avatar file extension only, e.g. <c>.png</c>; full path is <c>~/OrionBE/cache/local_profile_avatar{extension}</c>.</summary>
    public string? ProfileAvatarExtension { get; set; }
}
