namespace OrionBE.Launcher.Core;

public static class BedrockGameLayout
{
    /// <summary>
    /// Recent builds may ship more than one <c>Minecraft.Windows.exe</c> (e.g. legacy copies / staging).
    /// The GDK executable is usually <c>…/Content/Minecraft.Windows.exe</c>.
    /// </summary>
    public static string? FindWindowsExecutable(string gameRoot)
    {
        if (!Directory.Exists(gameRoot))
        {
            return null;
        }

        var inContent = Path.Combine(gameRoot, "Content", "Minecraft.Windows.exe");
        if (File.Exists(inContent))
        {
            return inContent;
        }

        var atRoot = Path.Combine(gameRoot, "Minecraft.Windows.exe");
        if (File.Exists(atRoot))
        {
            return atRoot;
        }

        try
        {
            var all = Directory.EnumerateFiles(gameRoot, "Minecraft.Windows.exe", SearchOption.AllDirectories).ToArray();
            if (all.Length == 0)
            {
                return null;
            }

            if (all.Length == 1)
            {
                return all[0];
            }

            return PickBestMinecraftExeCandidate(all);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Deterministic choice when multiple candidates exist (testable).</summary>
    public static string PickBestMinecraftExeCandidate(IReadOnlyList<string> absolutePaths)
    {
        if (absolutePaths.Count == 0)
        {
            throw new ArgumentException("At least one path is required.", nameof(absolutePaths));
        }

        if (absolutePaths.Count == 1)
        {
            return absolutePaths[0];
        }

        return absolutePaths
            .OrderByDescending(ScoreMinecraftExePath)
            .ThenBy(static p => p.Length)
            .ThenBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static int ScoreMinecraftExePath(string absolutePath)
    {
        var n = absolutePath.Replace('\\', '/');
        var lower = n.ToLowerInvariant();
        var score = 0;

        if (n.EndsWith("/Content/Minecraft.Windows.exe", StringComparison.OrdinalIgnoreCase))
        {
            score += 10_000;
        }
        else if (n.Contains("/Content/", StringComparison.OrdinalIgnoreCase))
        {
            score += 5000;
        }

        if (lower.Contains("/staging/", StringComparison.Ordinal)
            || lower.Contains("/backup/", StringComparison.Ordinal)
            || lower.Contains("/old/", StringComparison.Ordinal)
            || lower.Contains("/temp/", StringComparison.Ordinal)
            || lower.Contains("/cache/", StringComparison.Ordinal))
        {
            score -= 8000;
        }

        return score;
    }

    /// <summary>
    /// GDK package root: parent folder of <c>Content</c> (contains <c>Content\Minecraft.Windows.exe</c> and
    /// sibling <c>etc\ssl\certs\</c> for Wine/Linux HTTPS — see mcbe-on-linux). Falls back to <paramref name="gameRoot"/>.
    /// </summary>
    public static string ResolveBedrockPackageRoot(string gameRoot, string minecraftWindowsExePath)
    {
        var gameFull = Path.GetFullPath(gameRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var exeFull = Path.GetFullPath(minecraftWindowsExePath);
        var exeDir = Path.GetDirectoryName(exeFull);
        if (string.IsNullOrEmpty(exeDir))
        {
            return gameFull;
        }

        var current = exeDir;
        while (!string.IsNullOrEmpty(current))
        {
            if (string.Equals(Path.GetFileName(current), "Content", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(current);
                if (!string.IsNullOrEmpty(parent))
                {
                    return parent;
                }

                break;
            }

            if (string.Equals(current, gameFull, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var parentDir = Path.GetDirectoryName(current);
            if (parentDir == null || string.Equals(parentDir, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parentDir;
        }

        return gameFull;
    }

    public static string GetContentDirectory(string bedrockPackageRoot) =>
        Path.Combine(bedrockPackageRoot, "Content");
}
