namespace OrionBE.Launcher.Models;

public sealed class ModZipImportInspection
{
    public List<string> DllRelativePaths { get; set; } = [];
    public bool NeedsNormalization { get; set; }
    public string CurrentLayoutExample { get; set; } = string.Empty;
    public string RecommendedLayoutExample { get; set; } = string.Empty;
}

public sealed class ModZipImportOptions
{
    public bool NormalizeToDllDirectory { get; set; }
    public string? PrimaryDllRelativePath { get; set; }
}
