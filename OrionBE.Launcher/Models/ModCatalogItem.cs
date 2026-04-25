namespace OrionBE.Launcher.Models;

public sealed class ModCatalogItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    /// <summary>App-relative Avalonia resource path (for example /Assets/Icons/foo.ico).</summary>
    public string IconResource { get; set; } = string.Empty;
    public string FullDescription { get; set; } = string.Empty;
    public IReadOnlyList<string> ScreenshotRelativePaths { get; set; } = [];
    public IReadOnlyList<ModVersion> Versions { get; set; } = [];
}
