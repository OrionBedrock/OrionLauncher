namespace OrionBE.Launcher.Models;

public sealed class InstanceSummary
{
    public string FolderName { get; set; } = string.Empty;
    public InstanceConfig Config { get; set; } = new();
}
