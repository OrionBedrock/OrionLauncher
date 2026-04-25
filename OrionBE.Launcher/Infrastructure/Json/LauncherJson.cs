using System.Text.Json;

namespace OrionBE.Launcher.Infrastructure.Json;

public static class LauncherJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
