namespace OrionBE.Launcher.Services;

public interface IInstallationService
{
    Task InstallNewInstanceAsync(
        string instanceFolderName,
        string displayName,
        string gameVersion,
        bool modsEnabled,
        bool installLeviLamina,
        string? leviLaminaVersion,
        IProgress<(string Step, double Progress01)>? progress,
        CancellationToken cancellationToken = default);
}
