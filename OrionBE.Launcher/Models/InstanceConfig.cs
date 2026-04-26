namespace OrionBE.Launcher.Models;

public sealed class InstanceConfig
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string GamePath { get; set; } = "game";
    public bool ModsEnabled { get; set; }
    public List<InstalledModEntry> Mods { get; set; } = [];

    /// <summary>Absolute path to <c>umu-run</c> on Linux.</summary>
    public string? LinuxUmuRunPath { get; set; }

    /// <summary>Directory used as <c>PROTONPATH</c> for GDK Proton (contains the <c>proton</c> script).</summary>
    public string? LinuxProtonPath { get; set; }

    /// <summary>Wine prefix per instance (Linux), e.g. <c>~/OrionBE/instances/.../wineprefix</c>.</summary>
    public string? LinuxWinePrefixPath { get; set; }

    /// <summary>UUID derivado do URL do pacote (catálogo LukasPAH), para referência.</summary>
    public string? BedrockVersionUuid { get; set; }

    /// <summary>Caminho absoluto para <c>Minecraft.Windows.exe</c> (arranque no Wine ou Windows).</summary>
    public string? BedrockWindowsExecutablePath { get; set; }

    /// <summary>LeviLamina version installed for this instance (for mod compatibility checks).</summary>
    public string? LeviLaminaVersion { get; set; }
}
