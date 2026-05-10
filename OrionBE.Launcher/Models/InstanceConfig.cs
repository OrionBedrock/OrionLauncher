namespace OrionBE.Launcher.Models;

public sealed class InstanceConfig
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string GamePath { get; set; } = "game";
    public bool ModsEnabled { get; set; }
    public List<InstalledModEntry> Mods { get; set; } = [];

    public string? LinuxUmuRunPath { get; set; }

    public string? LinuxProtonPath { get; set; }

    public string? LinuxWinePrefixPath { get; set; }

    public string? BedrockVersionUuid { get; set; }

    public string? BedrockWindowsExecutablePath { get; set; }

    public string? LeviLaminaVersion { get; set; }
}
