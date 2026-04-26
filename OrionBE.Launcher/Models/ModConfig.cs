namespace OrionBE.Launcher.Models;

public sealed class ModConfig
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string SupportedGameVersion { get; set; } = string.Empty;
    public List<string> SupportedGameVersions { get; set; } = [];
    public bool RequiresLeviLamina { get; set; }
    public string? LeviLaminaVersionRange { get; set; }
    public string? ApiName { get; set; }
    public string? ApiVersionRange { get; set; }
    public string? EntryFile { get; set; }
    public string Source { get; set; } = "catalog";
}
