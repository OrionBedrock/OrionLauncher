using OrionBE.Launcher.Core;
using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

/// <summary>Catálogo real de versões Bedrock + catálogo de mods mock (UI).</summary>
public sealed class LauncherApiService : IApiService
{
    private static readonly IReadOnlyList<ModCatalogItem> ModCatalog =
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
                    SupportedGameVersions = ["1.21.50", "1.21.40"],
                    DownloadUrl = "https://example.invalid/ore-hud-1.0.0.zip",
                    RequiresLeviLamina = true,
                    ApiName = "LeviLamina",
                    LeviLaminaVersionRange = ">=1.0.0,<2.0.0",
                    ApiVersionRange = ">=1.0.0,<2.0.0",
                },
                new ModVersion
                {
                    Version = "0.9.2",
                    SupportedGameVersion = "1.20.80",
                    SupportedGameVersions = ["1.20.80"],
                    DownloadUrl = "https://example.invalid/ore-hud-0.9.2.zip",
                    RequiresLeviLamina = true,
                    ApiName = "LeviLamina",
                    LeviLaminaVersionRange = ">=0.9.0,<1.0.0",
                    ApiVersionRange = ">=0.9.0,<1.0.0",
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
                    SupportedGameVersions = ["1.21.40"],
                    DownloadUrl = "https://example.invalid/bedrock-patches-2.1.0.zip",
                    RequiresLeviLamina = false,
                },
            ],
        },
    ];

    private readonly IBedrockVersionCatalogService _bedrockCatalog;

    public LauncherApiService(IBedrockVersionCatalogService bedrockCatalog)
    {
        _bedrockCatalog = bedrockCatalog;
    }

    public async Task<IReadOnlyList<string>> GetGameVersionsAsync(CancellationToken cancellationToken = default)
    {
        var list = await _bedrockCatalog.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        return list.Select(static e => e.DropdownLabel).ToList();
    }

    public async Task<string?> GetLatestGameVersionAsync(CancellationToken cancellationToken = default)
    {
        var list = await _bedrockCatalog.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        if (list.Count == 0)
        {
            return null;
        }

        var releases = list.Where(static e => string.Equals(e.Type, "release", StringComparison.OrdinalIgnoreCase)).ToList();
        if (releases.Count == 0)
        {
            return list[0].DropdownLabel;
        }

        return releases
            .OrderByDescending(static e => e.Version, Comparer<string>.Create(BedrockVersionOrdering.CompareAscending))
            .First()
            .DropdownLabel;
    }

    public Task<IReadOnlyList<ModCatalogItem>> GetModsCatalogAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ModCatalog);
    }

    public Task<ModCatalogItem?> GetModByIdAsync(string modId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ModCatalog.FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.OrdinalIgnoreCase)));
    }
}
