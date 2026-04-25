namespace OrionBE.Launcher.Services;

/// <summary>
/// Ensures Linux GDK runtime components used by Amethyst Launcher are present:
/// <see href="https://github.com/raonygamer/gdk-proton">gdk-proton</see> and
/// <see href="https://github.com/raonygamer/umu-launcher">umu-launcher</see>, then resolves <c>umu-run</c> and <c>PROTONPATH</c>.
/// </summary>
public interface IGdkLinuxRuntimeService
{
    bool IsSupported { get; }

    /// <summary>
    /// Downloads or reuses cached tools, extracts archives, applies chmod 755 recursively (Amethyst post-install), returns resolved paths.</summary>
    Task<GdkLinuxRuntimePaths?> EnsureRuntimeAsync(
        IProgress<(string Step, double Progress01)>? progress,
        CancellationToken cancellationToken = default);
}

public sealed record GdkLinuxRuntimePaths(string UmuRunPath, string ProtonDirectoryPath);
