namespace OrionBE.Launcher.Models;

public sealed class ModVersion
{
    public string Version { get; set; } = string.Empty;
    public string SupportedGameVersion { get; set; } = string.Empty;
    public IReadOnlyList<string> SupportedGameVersions { get; set; } = [];
    public string DownloadUrl { get; set; } = string.Empty;
    public bool RequiresLeviLamina { get; set; }
    public string? LeviLaminaVersionRange { get; set; }
    public string? ApiName { get; set; }
    public string? ApiVersionRange { get; set; }
}
