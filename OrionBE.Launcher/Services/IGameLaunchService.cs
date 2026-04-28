namespace OrionBE.Launcher.Services;

public interface IGameLaunchService
{
    Task LaunchInstanceAsync(string instanceFolderName, CancellationToken cancellationToken = default);
}
