namespace OrionBE.Launcher.Core;

public static class BedrockGameLayout
{
    public static string? FindWindowsExecutable(string gameRoot)
    {
        if (!Directory.Exists(gameRoot))
        {
            return null;
        }

        var direct = Path.Combine(gameRoot, "Minecraft.Windows.exe");
        if (File.Exists(direct))
        {
            return direct;
        }

        try
        {
            return Directory.EnumerateFiles(gameRoot, "Minecraft.Windows.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
