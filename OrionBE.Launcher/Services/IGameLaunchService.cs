namespace OrionBE.Launcher.Services;

public interface IGameLaunchService
{
    /// <summary>Inicia o executável Bedrock (Linux via umu-run + Proton; Windows nativo).</summary>
    Task LaunchInstanceAsync(string instanceFolderName, CancellationToken cancellationToken = default);
}
