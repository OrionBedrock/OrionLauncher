namespace OrionBE.Launcher.Services;

public sealed class StartupDependencyCheckReport
{
    public bool IsFirstLaunch { get; init; }
    public List<string> MissingItems { get; init; } = [];

    public bool HasMissingItems => MissingItems.Count > 0;
}
