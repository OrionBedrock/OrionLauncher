namespace OrionBE.Launcher.Core;

public static class MinecraftOptionsPatcher
{
    private const string SafetyWarningKey = "do_not_show_multiplayer_online_safety_warning";

    public static void EnsureSafetyWarningDisabledInFile(string optionsFilePath)
    {
        var current = File.Exists(optionsFilePath) ? File.ReadAllText(optionsFilePath) : string.Empty;
        var updated = EnsureSafetyWarningDisabled(current);
        var dir = Path.GetDirectoryName(optionsFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(optionsFilePath, updated);
    }

    public static string EnsureSafetyWarningDisabled(string content)
    {
        var normalized = content.Replace("\r\n", "\n");
        var lines = normalized.Split('\n').ToList();
        var found = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!line.StartsWith(SafetyWarningKey, StringComparison.Ordinal))
            {
                continue;
            }

            lines[i] = $"{SafetyWarningKey}:1";
            found = true;
            break;
        }

        if (!found)
        {
            if (lines.Count == 1 && lines[0].Length == 0)
            {
                lines[0] = $"{SafetyWarningKey}:1";
            }
            else
            {
                lines.Add($"{SafetyWarningKey}:1");
            }
        }

        return string.Join(Environment.NewLine, lines.Where(static l => l is not null));
    }
}
