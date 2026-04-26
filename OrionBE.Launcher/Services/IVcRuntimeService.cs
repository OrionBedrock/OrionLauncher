namespace OrionBE.Launcher.Services;

public interface IVcRuntimeService
{
    /// <summary>Ensures vcruntime140_1.dll exists in game root and exe directory.</summary>
    Task EnsureForGameAsync(string gameRoot, string exePath, CancellationToken cancellationToken = default);
}
