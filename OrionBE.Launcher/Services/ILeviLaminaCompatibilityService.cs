namespace OrionBE.Launcher.Services;

public interface ILeviLaminaCompatibilityService
{
    /// <summary>Returns supported LeviLamina versions for a selected game version label.</summary>
    Task<IReadOnlyList<string>> GetSupportedVersionsAsync(
        string selectedGameVersionLabel,
        CancellationToken cancellationToken = default);
}
