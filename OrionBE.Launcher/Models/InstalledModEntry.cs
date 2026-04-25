namespace OrionBE.Launcher.Models;

/// <summary>Reference to a globally stored mod folder name (no spaces), duplicated into instance /mods/.</summary>
public sealed class InstalledModEntry
{
    public string GlobalFolderName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
