using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace OrionBE.Launcher.I18n;

/// <summary>
/// Loads UI strings from JSON (flattened keys). Lookup order: active language → English (en-US) → raw key.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    private const string IndexerName = "Item";
    private const string IndexerArrayName = "Item[]";
    private const string FallbackLanguage = "en-US";

    private readonly Dictionary<string, string> _primary = new(512);
    private readonly Dictionary<string, string> _fallback = new(512);

    public static Localizer Instance { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Fired after language load or manual refresh so view-models can re-evaluate translated properties.</summary>
    public event EventHandler? CultureChanged;

    public string Language { get; private set; } = FallbackLanguage;

    public string this[string key]
    {
        get
        {
            if (_primary.TryGetValue(key, out var p) && !string.IsNullOrEmpty(p))
            {
                return p;
            }

            if (_fallback.TryGetValue(key, out var f) && !string.IsNullOrEmpty(f))
            {
                return f;
            }

            return key;
        }
    }

    public string Format(string key, params object?[] args)
    {
        var template = this[key];
        if (args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>Loads English fallback, then the requested language as primary. Always reloads fallback from disk.</summary>
    public bool LoadLanguage(string language)
    {
        Language = string.IsNullOrWhiteSpace(language) ? FallbackLanguage : language.Trim();

        var fallbackPath = FindLanguageFile(FallbackLanguage);
        if (fallbackPath is not null)
        {
            ReloadDictionary(fallbackPath, _fallback);
        }
        else
        {
            _fallback.Clear();
        }

        if (_fallback.Count == 0)
        {
            TryLoadEmbeddedDictionary(FallbackLanguage, _fallback);
        }

        var primaryPath = FindLanguageFile(Language);
        if (primaryPath is null || string.Equals(Language, FallbackLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _primary.Clear();
        }
        else
        {
            ReloadDictionary(primaryPath, _primary);
        }

        if (_primary.Count == 0 && !string.Equals(Language, FallbackLanguage, StringComparison.OrdinalIgnoreCase))
        {
            TryLoadEmbeddedDictionary(Language, _primary);
        }

        Invalidate();
        RaiseCultureChanged();
        return primaryPath is not null
               || string.Equals(Language, FallbackLanguage, StringComparison.OrdinalIgnoreCase)
               || _fallback.Count > 0;
    }

    /// <summary>Carrega traduções embutidas na assembly (backup quando não há ficheiros em disco).</summary>
    private static bool TryLoadEmbeddedDictionary(string languageCode, Dictionary<string, string> target)
    {
        var asm = typeof(Localizer).Assembly;
        var suffix = $"{languageCode}.json";
        foreach (var fullName in asm.GetManifestResourceNames())
        {
            if (!fullName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = asm.GetManifestResourceStream(fullName);
            if (stream is null)
            {
                continue;
            }

            try
            {
                target.Clear();
                using var doc = JsonDocument.Parse(stream);
                Flatten(doc.RootElement, "", target);
                return target.Count > 0;
            }
            catch
            {
                target.Clear();
            }
        }

        return false;
    }

    /// <summary>Procura <c>I18n/en-US.json</c> em vários sítios (single-file, symlink, cwd).</summary>
    private static string? FindLanguageFile(string languageCode)
    {
        foreach (var root in GetCandidateI18nRoots())
        {
            var path = Path.Combine(root, $"{languageCode}.json");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateI18nRoots()
    {
        var roots = new List<string>();
        roots.Add(Path.Combine(AppContext.BaseDirectory, "I18n"));

        TryAddPath(roots, static () =>
        {
            var proc = Environment.ProcessPath;
            if (string.IsNullOrEmpty(proc))
            {
                return null;
            }

            var dir = Path.GetDirectoryName(proc);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "I18n");
        });

        TryAddPath(roots, static () => Path.Combine(Directory.GetCurrentDirectory(), "I18n"));

        return roots;
    }

    private static void TryAddPath(List<string> roots, Func<string?> getPath)
    {
        try
        {
            var p = getPath();
            if (!string.IsNullOrEmpty(p))
            {
                roots.Add(p);
            }
        }
        catch
        {
            // ignored
        }
    }

    private static void ReloadDictionary(string path, Dictionary<string, string> target)
    {
        target.Clear();
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Flatten(doc.RootElement, "", target);
        }
        catch
        {
            // Leave target empty; lookups will fall back or return keys.
        }
    }

    public void Invalidate()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerArrayName));
    }

    public void RaiseCultureChanged() => CultureChanged?.Invoke(this, EventArgs.Empty);

    private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var newPrefix = prefix.Length == 0
                        ? prop.Name
                        : string.Concat(prefix, "_", prop.Name);

                    Flatten(prop.Value, newPrefix, result);
                }

                break;

            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var newPrefix = string.Concat(prefix, "_", i++);
                    Flatten(item, newPrefix, result);
                }

                break;

            default:
                result[prefix] = element.GetString() ?? "";
                break;
        }
    }
}
