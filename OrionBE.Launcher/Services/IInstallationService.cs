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

    /// <summary>
    /// Re-downloads and extracts the newest Bedrock build allowed for this instance (same preview/release channel; never downgrade).
    /// </summary>
    Task UpgradeInstanceToLatestEligibleAsync(
        string instanceFolderName,
        IProgress<(string Step, double Progress01)>? progress,
        CancellationToken cancellationToken = default);
}
