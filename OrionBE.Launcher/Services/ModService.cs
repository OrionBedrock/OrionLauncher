using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrionBE.Launcher.Core;
using OrionBE.Launcher.Infrastructure.Json;
using OrionBE.Launcher.Models;

namespace OrionBE.Launcher.Services;

public sealed class ModService : IModService
{
    private readonly IFileSystemService _fileSystem;
    private readonly IDownloadService _downloadService;

    public ModService(IFileSystemService fileSystem, IDownloadService downloadService)
    {
        _fileSystem = fileSystem;
        _downloadService = downloadService;
    }

    public async Task<string> EnsureGlobalModFromCatalogAsync(
        ModCatalogItem mod,
        ModVersion selectedVersion,
        CancellationToken cancellationToken = default)
    {
        var folder = GlobalModFolderName(mod.Name, selectedVersion.Version);
        var root = OrionPaths.GlobalModFolder(folder);
        await _fileSystem.EnsureDirectoryAsync(root, cancellationToken).ConfigureAwait(false);

        var modConfig = new ModConfig
        {
            Name = mod.Name,
            Version = selectedVersion.Version,
            SupportedGameVersion = selectedVersion.SupportedGameVersion,
            SupportedGameVersions = selectedVersion.SupportedGameVersions.ToList(),
            RequiresLeviLamina = selectedVersion.RequiresLeviLamina,
            LeviLaminaVersionRange = selectedVersion.LeviLaminaVersionRange,
            ApiName = selectedVersion.ApiName,
            ApiVersionRange = selectedVersion.ApiVersionRange,
            Source = "catalog",
        };

        var json = JsonSerializer.Serialize(modConfig, LauncherJson.Options);
        await _fileSystem.WriteAllTextAsync(OrionPaths.GlobalModConfigPath(folder), json, cancellationToken).ConfigureAwait(false);

        var payloadPath = Path.Combine(root, "payload.mock.bin");
        await _downloadService
            .DownloadToFileAsync(new Uri(selectedVersion.DownloadUrl), payloadPath, null, cancellationToken)
            .ConfigureAwait(false);

        return folder;
    }

    public async Task CopyGlobalModIntoInstanceAsync(
        string instanceFolderName,
        string globalModFolderName,
        CancellationToken cancellationToken = default)
    {
        var source = OrionPaths.GlobalModFolder(globalModFolderName);
        var dest = Path.Combine(OrionPaths.InstanceMods(instanceFolderName), globalModFolderName);
        await _fileSystem.EnsureDirectoryAsync(Path.GetDirectoryName(dest)!, cancellationToken).ConfigureAwait(false);
        await _fileSystem.CopyDirectoryRecursiveAsync(source, dest, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ImportGlobalModZipAsync(
        string zipPath,
        string fallbackName,
        string instanceGameVersion,
        ModZipImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException(".zip file not found.", zipPath);
        }

        if (!zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .zip files are supported in this flow.");
        }

        var temp = Path.Combine(OrionPaths.Cache, "mod-import", Guid.NewGuid().ToString("N"));
        await _fileSystem.EnsureDirectoryAsync(temp, cancellationToken).ConfigureAwait(false);

        try
        {
            ExtractZipSafely(zipPath, temp);
            var root = ResolveImportedRoot(temp);
            var dllCandidates = FindDllCandidates(root);
            var primaryDll = ResolvePrimaryDllPath(dllCandidates, options?.PrimaryDllRelativePath);
            if (options?.NormalizeToDllDirectory == true && primaryDll is not null)
            {
                root = ResolveNormalizedRootByDll(root, primaryDll);
            }

            var manifest = await TryReadManifestFromFolderAsync(root, cancellationToken).ConfigureAwait(false);
            EnsureManifestIsImportable(manifest);

            var name = manifest!.Name!.Trim();
            var version = manifest.Version!.Trim();
            var folder = ImportedModFolderName(name);
            var dest = OrionPaths.GlobalModFolder(folder);
            if (Directory.Exists(dest))
            {
                Directory.Delete(dest, recursive: true);
            }

            await _fileSystem.CopyDirectoryRecursiveAsync(root, dest, cancellationToken).ConfigureAwait(false);

            var config = new ModConfig
            {
                Name = name!,
                Version = version,
                SupportedGameVersion = instanceGameVersion,
                SupportedGameVersions = [instanceGameVersion],
                EntryFile = manifest.Entry!.Trim(),
                RequiresLeviLamina = ParseRequiresLeviLamina(manifest),
                LeviLaminaVersionRange = ParseLeviLaminaRange(manifest),
                ApiName = ParseApiName(manifest),
                ApiVersionRange = ParseApiVersionRange(manifest),
                Source = "zip",
            };
            var json = JsonSerializer.Serialize(config, LauncherJson.Options);
            await _fileSystem.WriteAllTextAsync(OrionPaths.GlobalModConfigPath(folder), json, cancellationToken).ConfigureAwait(false);
            return folder;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    public Task<ModZipImportInspection> AnalyzeModZipAsync(
        string zipPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("ZIP file not found.", zipPath);
        }

        if (!zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .zip files are supported.");
        }

        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .Select(e => e.FullName.Replace('\\', '/'))
            .Where(p => !p.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var dlls = entries
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var primary = dlls.FirstOrDefault();
        var recommended = primary is null
            ? "(no dll found)"
            : $"<mod-root>/{Path.GetFileName(primary)}";
        var current = primary ?? "(no dll found)";
        var needsNormalization = dlls.Any(p =>
        {
            var depth = p.Count(ch => ch == '/');
            return depth > 0;
        });

        var report = new ModZipImportInspection
        {
            DllRelativePaths = dlls,
            NeedsNormalization = needsNormalization,
            CurrentLayoutExample = current,
            RecommendedLayoutExample = recommended,
        };
        return Task.FromResult(report);
    }

    public async Task<string> ImportGlobalModDllAsync(
        string dllPath,
        string displayName,
        string instanceGameVersion,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException(".dll file not found.", dllPath);
        }

        if (!dllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .dll files are supported in this flow.");
        }

        var fileName = Path.GetFileName(dllPath);
        var sourceDir = Path.GetDirectoryName(dllPath) ?? string.Empty;
        var siblingManifest = await TryReadManifestFromFolderAsync(sourceDir, cancellationToken).ConfigureAwait(false);
        EnsureManifestIsImportable(siblingManifest);

        var baseName = siblingManifest!.Name!.Trim();

        var resolvedVersion = siblingManifest.Version!.Trim();
        var folder = ImportedModFolderName(baseName);
        var root = OrionPaths.GlobalModFolder(folder);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        await _fileSystem.EnsureDirectoryAsync(root, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
        {
            throw new InvalidOperationException(
                $"Unable to resolve source folder for DLL import. dllPath='{dllPath}', sourceDir='{sourceDir}'.");
        }

        await _fileSystem.CopyDirectoryRecursiveAsync(sourceDir, root, cancellationToken).ConfigureAwait(false);

        var config = new ModConfig
        {
            Name = baseName,
            Version = resolvedVersion,
            SupportedGameVersion = instanceGameVersion,
            SupportedGameVersions = [instanceGameVersion],
            EntryFile = siblingManifest.Entry!.Trim(),
            RequiresLeviLamina = ParseRequiresLeviLamina(siblingManifest),
            LeviLaminaVersionRange = ParseLeviLaminaRange(siblingManifest),
            ApiName = ParseApiName(siblingManifest),
            ApiVersionRange = ParseApiVersionRange(siblingManifest),
            Source = "dll",
        };
        var json = JsonSerializer.Serialize(config, LauncherJson.Options);
        await _fileSystem.WriteAllTextAsync(OrionPaths.GlobalModConfigPath(folder), json, cancellationToken).ConfigureAwait(false);
        return folder;
    }

    public async Task<ModConfig?> GetGlobalModConfigAsync(
        string globalModFolderName,
        CancellationToken cancellationToken = default)
    {
        var json = await _fileSystem.ReadAllTextIfExistsAsync(
                OrionPaths.GlobalModConfigPath(globalModFolderName),
                cancellationToken)
            .ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ModConfig>(json, LauncherJson.Options);
    }

    public ModCompatibilityReport EvaluateCompatibility(InstanceConfig instanceConfig, ModConfig? modConfig)
    {
        if (modConfig is null)
        {
            return new ModCompatibilityReport
            {
                GameVersionCompatible = false,
                LeviLaminaCompatible = false,
                ApiCompatible = false,
                Summary = "Metadata not found (mod.json).",
            };
        }

        var supportedVersions = modConfig.SupportedGameVersions.Count > 0
            ? modConfig.SupportedGameVersions
            : [modConfig.SupportedGameVersion];
        var gameOk = supportedVersions.Count == 0
                     || supportedVersions.Any(v => string.Equals(v, instanceConfig.Version, StringComparison.OrdinalIgnoreCase));

        var requiresLevi = modConfig.RequiresLeviLamina
                           || (!string.IsNullOrWhiteSpace(modConfig.ApiName)
                               && modConfig.ApiName.Contains("levi", StringComparison.OrdinalIgnoreCase));
        var leviInstalled = !string.IsNullOrWhiteSpace(instanceConfig.LeviLaminaVersion);
        var leviRangeOk = !requiresLevi || (leviInstalled
                                            && VersionRangeMatcher.Matches(
                                                instanceConfig.LeviLaminaVersion,
                                                modConfig.LeviLaminaVersionRange));

        var apiOk = string.IsNullOrWhiteSpace(modConfig.ApiVersionRange)
                    || VersionRangeMatcher.Matches(instanceConfig.LeviLaminaVersion, modConfig.ApiVersionRange);

        var messages = new List<string>();
        if (!gameOk)
        {
            messages.Add($"game version mismatch (instance {instanceConfig.Version})");
        }

        if (requiresLevi && !leviInstalled)
        {
            messages.Add("LeviLamina required but not configured");
        }
        else if (!leviRangeOk)
        {
            messages.Add($"LeviLamina range mismatch ({modConfig.LeviLaminaVersionRange})");
        }

        if (!apiOk)
        {
            messages.Add($"API range mismatch ({modConfig.ApiVersionRange})");
        }

        return new ModCompatibilityReport
        {
            GameVersionCompatible = gameOk,
            LeviLaminaCompatible = leviRangeOk,
            ApiCompatible = apiOk,
            RequiresLeviLamina = requiresLevi,
            Summary = messages.Count == 0 ? "Compatible" : string.Join("; ", messages),
        };
    }

    public Task<IReadOnlyList<string>> ListGlobalModFoldersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(OrionPaths.GlobalMods))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var names = Directory
            .GetDirectories(OrionPaths.GlobalMods)
            .Select(static d => Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar)))
            .ToArray();
        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    private static string GlobalModFolderName(string modName, string version)
    {
        var safeName = string.Concat(modName.Where(static c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        if (string.IsNullOrEmpty(safeName))
        {
            safeName = "mod";
        }

        var safeVer = string.Concat(version.Where(static c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'));
        return $"{safeName}-{safeVer}";
    }

    private static string ImportedModFolderName(string manifestName)
    {
        var folderName = manifestName.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = string.Concat(folderName.Where(c => !invalid.Contains(c))).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new InvalidOperationException("Invalid manifest.json: field 'name' cannot produce a valid folder name.");
        }

        return cleaned;
    }

    private static void ExtractZipSafely(string zipPath, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var fullPath = Path.GetFullPath(Path.Combine(destinationDir, entry.FullName));
            if (!fullPath.StartsWith(Path.GetFullPath(destinationDir), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Invalid zip contents (path traversal).");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            entry.ExtractToFile(fullPath, overwrite: true);
        }
    }

    private static string ResolveImportedRoot(string extractedRoot)
    {
        var dirs = Directory
            .GetDirectories(extractedRoot)
            .Where(static d =>
            {
                var name = Path.GetFileName(d);
                return !string.Equals(name, "__MACOSX", StringComparison.OrdinalIgnoreCase)
                       && !name.StartsWith(".", StringComparison.Ordinal);
            })
            .ToArray();
        var files = Directory
            .GetFiles(extractedRoot)
            .Where(static f =>
            {
                var name = Path.GetFileName(f);
                return !name.StartsWith(".", StringComparison.Ordinal)
                       && !string.Equals(name, "Thumbs.db", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        if (files.Length == 0 && dirs.Length == 1)
        {
            return dirs[0];
        }

        return extractedRoot;
    }

    private static List<string> FindDllCandidates(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(root, "*.dll", SearchOption.AllDirectories)
            .Select(d => Path.GetRelativePath(root, d).Replace('\\', '/'))
            .ToList();
    }

    private static string? ResolvePrimaryDllPath(IReadOnlyList<string> dllCandidates, string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var hit = dllCandidates.FirstOrDefault(p =>
                string.Equals(p, preferred, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                return hit;
            }
        }

        return dllCandidates.FirstOrDefault();
    }

    private static string ResolveNormalizedRootByDll(string root, string dllRelativePath)
    {
        var relDir = Path.GetDirectoryName(dllRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(relDir))
        {
            return root;
        }

        var normalizedRoot = Path.Combine(root, relDir);
        return Directory.Exists(normalizedRoot) ? normalizedRoot : root;
    }

    private static async Task<ImportedManifest?> TryReadManifestFromFolderAsync(string folder, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(folder, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ImportedManifest>(json, LauncherJson.Options);
    }

    private static void EnsureManifestIsImportable(ImportedManifest? manifest)
    {
        if (manifest is null)
        {
            throw new InvalidOperationException(
                "Import rejected: manifest.json is required and must include 'name', 'entry', and 'version'.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name)
            || string.IsNullOrWhiteSpace(manifest.Entry)
            || string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidOperationException(
                "Import rejected: manifest.json must have non-empty 'name', 'entry', and 'version'.");
        }
    }

    private static bool ParseRequiresLeviLamina(ImportedManifest? manifest)
    {
        if (manifest?.Dependencies is null)
        {
            return false;
        }

        return manifest.Dependencies.Any(d =>
            (!string.IsNullOrWhiteSpace(d.Name)
             && d.Name.Contains("levilamina", StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(d.Package)
                && d.Package.Contains("levilamina", StringComparison.OrdinalIgnoreCase)));
    }

    private static string? ParseLeviLaminaRange(ImportedManifest? manifest)
    {
        var dep = manifest?.Dependencies?.FirstOrDefault(d =>
            (!string.IsNullOrWhiteSpace(d.Name)
             && d.Name.Contains("levilamina", StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(d.Package)
                && d.Package.Contains("levilamina", StringComparison.OrdinalIgnoreCase)));
        return dep?.VersionRange;
    }

    private static string? ParseApiName(ImportedManifest? manifest)
    {
        var dep = manifest?.Dependencies?.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.Name) || !string.IsNullOrWhiteSpace(d.Package));
        if (dep is null)
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(dep.Name) ? dep.Name : dep.Package;
    }

    private static string? ParseApiVersionRange(ImportedManifest? manifest)
    {
        var dep = manifest?.Dependencies?.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.VersionRange));
        return dep?.VersionRange;
    }

    private sealed class ImportedManifest
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("entry")]
        public string? Entry { get; set; }

        [JsonPropertyName("dependencies")]
        public List<ImportedDependency>? Dependencies { get; set; }
    }

    private sealed class ImportedDependency
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("package")]
        public string? Package { get; set; }

        [JsonPropertyName("version")]
        public string? VersionRange { get; set; }
    }
}
