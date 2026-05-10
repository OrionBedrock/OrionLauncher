using System.Diagnostics;
using System.Text.Json;
using LiveMarkdown.Avalonia;
using OrionBe.Infrastructure.Interfaces;

namespace OrionBe.Infrastructure.Services.Microsoft;

public class MinecraftNewService : IMinecraftNewService
{
    public Task<ICollection<NewInfo>> GetNewsAsync(string language)
    {
        try
        {
            var roots = EnumerateUpdatesRoots().ToList();
            if (roots.Count == 0)
            {
                return Task.FromResult((ICollection<NewInfo>)Array.Empty<NewInfo>());
            }

            string? langDir = null;
            foreach (var root in roots)
            {
                langDir = ResolveLanguageDirectory(root, language);
                if (langDir is not null)
                {
                    break;
                }
            }

            if (langDir is null)
            {
                return Task.FromResult((ICollection<NewInfo>)Array.Empty<NewInfo>());
            }

            string? versionsPath = null;
            foreach (var root in roots)
            {
                var p = Path.Combine(root, "versions.json");
                if (File.Exists(p))
                {
                    versionsPath = p;
                    break;
                }
            }

            if (versionsPath is null || !File.Exists(versionsPath))
            {
                return Task.FromResult((ICollection<NewInfo>)Array.Empty<NewInfo>());
            }

            var json = File.ReadAllText(versionsPath);
            var versions = JsonSerializer.Deserialize<ICollection<string>>(json);
            if (versions is null || versions.Count == 0)
            {
                return Task.FromResult((ICollection<NewInfo>)Array.Empty<NewInfo>());
            }

            var response = new List<NewInfo>();

            foreach (var version in versions)
            {
                var mdPath = Path.Combine(langDir, $"{version}.md");
                if (!File.Exists(mdPath))
                {
                    continue;
                }

                var lines = File.ReadAllText(mdPath).Split('\n');
                var builder = new ObservableStringBuilder();

                foreach (var line in lines)
                {
                    builder.AppendLine(line);
                }

                response.Add(new NewInfo
                {
                    Title = version,
                    Content = builder,
                });
            }

            return Task.FromResult((ICollection<NewInfo>)response);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetNewsAsync failed: {ex}");
            return Task.FromResult((ICollection<NewInfo>)Array.Empty<NewInfo>());
        }
    }

    /// <summary>
    /// <c>Updates</c> next to the single-file extract / app folder (<see cref="AppContext.BaseDirectory"/>).
    /// Do not use <c>Assembly.Location</c>: it is empty for bundled assemblies (IL3000).
    /// </summary>
    private static IEnumerable<string> EnumerateUpdatesRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Consider(string? baseDir)
        {
            if (string.IsNullOrEmpty(baseDir))
            {
                return;
            }

            var updates = Path.Combine(baseDir, "Updates");
            if (Directory.Exists(updates))
            {
                seen.Add(updates);
            }
        }

        Consider(AppContext.BaseDirectory);

        return seen;
    }

    private static string? ResolveLanguageDirectory(string updatesRoot, string language)
    {
        var tag = string.IsNullOrWhiteSpace(language) ? "en-US" : language.Trim();
        var upper = tag.ToUpperInvariant();

        var langPath = Path.Combine(updatesRoot, upper);
        if (Directory.Exists(langPath))
        {
            return langPath;
        }

        var fallback = Path.Combine(updatesRoot, "EN-US");
        return Directory.Exists(fallback) ? fallback : null;
    }
}

public class NewInfo
{
    public required string Title { get; set; }

    public required ObservableStringBuilder Content { get; set; }
}
