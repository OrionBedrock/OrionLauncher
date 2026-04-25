using System.Text.Json.Serialization;

namespace OrionBE.Launcher.Models;

public sealed class BedrockVersionEntry
{
    /// <summary>Normalized version label, e.g. <c>1.21.124.02</c> (no "Release "/"Preview " prefix).</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("urls")]
    public List<string> Urls { get; set; } = [];

    /// <summary>Human label for UI lists (unique across preview/release).</summary>
    public string DropdownLabel =>
        string.Equals(Type, "preview", StringComparison.OrdinalIgnoreCase)
            ? $"{Version} (Preview)"
            : Version;
}
