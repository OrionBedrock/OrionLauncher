namespace OrionBE.Launcher.Services;

public interface IBedrockOnlineBootstrapService
{
    /// <summary>
    /// Places CA bundle and libcurl (as Xcurl.dll) for GDK Bedrock online stack (Wine/Linux and Windows).
    /// </summary>
    Task EnsureOnlineSupportFilesAsync(
        string gameRoot,
        string minecraftWindowsExePath,
        Action<string>? logLine,
        CancellationToken cancellationToken = default);
}
