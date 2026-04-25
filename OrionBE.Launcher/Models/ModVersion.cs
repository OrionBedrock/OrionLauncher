namespace OrionBE.Launcher.Models;

public sealed class ModVersion
{
    public string Version { get; set; } = string.Empty;
    public string SupportedGameVersion { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
}
