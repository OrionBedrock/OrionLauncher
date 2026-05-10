using System.IO;

namespace OrionBE.Launcher.Core;

/// <summary>
/// Centralized launcher data paths. All launcher data lives under ~/OrionBE.
/// </summary>
public static class OrionPaths
{
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OrionBE");

    public static string Instances => Path.Combine(Root, "instances");
    public static string GlobalMods => Path.Combine(Root, "mods");
    public static string Cache => Path.Combine(Root, "cache");
    public static string Assets => Path.Combine(Root, "assets");

    /// <summary>Cached Bedrock GDK version list from the upstream catalog.</summary>
    public static string BedrockVersionCacheFile => Path.Combine(Cache, "bedrock_versions_cache.json");

    /// <summary>Shared encrypted <c>.msixvc</c> downloads (one file per game version string).</summary>
    public static string BedrockMsixvcCacheDir => Path.Combine(Cache, "bedrock_msixvc");

    /// <summary>cacert.pem + curl zip cache for Bedrock HTTPS/bootstrap (Xcurl.dll).</summary>
    public static string BedrockOnlineSupportCacheDir => Path.Combine(Cache, "bedrock_online_support");

    public static string InstanceRoot(string instanceFolderName) =>
        Path.Combine(Instances, instanceFolderName);

    public static string InstanceGame(string instanceFolderName) =>
        Path.Combine(InstanceRoot(instanceFolderName), "game");

    public static string InstanceMods(string instanceFolderName) =>
        Path.Combine(InstanceRoot(instanceFolderName), "mods");

    public static string InstanceConfigPath(string instanceFolderName) =>
        Path.Combine(InstanceRoot(instanceFolderName), "config.json");

    public static string GlobalModFolder(string modFolderName) =>
        Path.Combine(GlobalMods, modFolderName);

    public static string GlobalModConfigPath(string modFolderName) =>
        Path.Combine(GlobalModFolder(modFolderName), "mod.json");

    /// <summary>Optional screenshots path relative to repository / deployment (see requirements).</summary>
    public static string DocsPrintsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "docs", "prints");

    /// <summary>Marker file indicating first-launch dependency check has already run.</summary>
    public static string FirstLaunchDependencyCheckMarker =>
        Path.Combine(Root, "first_launch_dependency_check_done.json");

    /// <summary>Persisted launcher UI preferences (language, etc.).</summary>
    public static string LauncherSettingsFile => Path.Combine(Root, "launcher_settings.json");

}
