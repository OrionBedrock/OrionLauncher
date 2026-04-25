using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

/// <summary>Replaceable mock API. Returns deterministic example data.</summary>
public sealed class MockApiService : IApiService
{
    private static readonly IReadOnlyList<string> GameVersions =
    [
        "1.21.50",
        "1.21.40",
        "1.20.80",
        "1.20.50",
    ];

    private static readonly IReadOnlyList<ModCatalogItem> Catalog =
    [
        new()
        {
            Id = "ore-hud",
            Name = "Ore HUD",
            ShortDescription = "Highlights nearby ores (mock).",
            IconResource = "/Assets/Icons/nav_browse_mods.ico",
            FullDescription =
                "Mock mod for the launcher UI. In a real build this would describe behavior, compatibility, and install notes.",
            ScreenshotRelativePaths = ["docs/prints/screen1.png", "docs/prints/screen2.png"],
            Versions =
            [
                new ModVersion
                {
                    Version = "1.0.0",
                    SupportedGameVersion = "1.21.50",
                    DownloadUrl = "https://example.invalid/ore-hud-1.0.0.zip",
                },
                new ModVersion
                {
                    Version = "0.9.2",
                    SupportedGameVersion = "1.20.80",
                    DownloadUrl = "https://example.invalid/ore-hud-0.9.2.zip",
                },
            ],
        },
        new()
        {
            Id = "bedrock-patches",
            Name = "Bedrock Patches",
            ShortDescription = "Small client-side tweaks (mock).",
            IconResource = "/Assets/Icons/nav_main.ico",
            FullDescription = "Another mock entry used to populate the Browse Mods screen.",
            ScreenshotRelativePaths = [],
            Versions =
            [
                new ModVersion
                {
                    Version = "2.1.0",
                    SupportedGameVersion = "1.21.40",
                    DownloadUrl = "https://example.invalid/bedrock-patches-2.1.0.zip",
                },
            ],
        },
    ];

    public Task<IReadOnlyList<string>> GetGameVersionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GameVersions);
    }

    public Task<string?> GetLatestGameVersionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(GameVersions[0]);
    }

    public Task<IReadOnlyList<ModCatalogItem>> GetModsCatalogAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Catalog);
    }

    public Task<ModCatalogItem?> GetModByIdAsync(string modId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Catalog.FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.OrdinalIgnoreCase)));
    }
}
