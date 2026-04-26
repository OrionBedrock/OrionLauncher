namespace OrionBE.Launcher.Models;

public sealed class ModCompatibilityReport
{
    public bool GameVersionCompatible { get; set; }
    public bool LeviLaminaCompatible { get; set; }
    public bool ApiCompatible { get; set; }
    public bool RequiresLeviLamina { get; set; }
    public string Summary { get; set; } = string.Empty;
}
