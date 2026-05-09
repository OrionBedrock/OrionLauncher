namespace OrionBE.Launcher.Services;

public interface IGameLaunchService
{
    bool IsInstanceRunning(string instanceFolderName);
    Task LaunchInstanceAsync(string instanceFolderName, CancellationToken cancellationToken = default);
}
