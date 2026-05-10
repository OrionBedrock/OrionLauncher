using System.IO.Compression;

namespace OrionBE.Launcher.Core;

/// <summary>
/// Extracts libcurl DLLs from official Windows zip bundles (curl.se / curl-for-win).
/// Recent builds use <c>bin/libcurl-x64.dll</c>; older MSYS2 packages used <c>bin/libcurl-4.dll</c>.
/// </summary>
public static class LibcurlZipExtractor
{
    public static void ExtractLibcurlDllFromWindowsMingwZip(string zipPath, string destinationDllPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = FindLibcurlDllEntry(zip);
        if (entry is null)
        {
            throw new InvalidOperationException(
                "libcurl was not found in the zip (expected bin/libcurl-4.dll or bin/libcurl-x64.dll, or another bin/libcurl-*.dll).");
        }

        using var input = entry.Open();
        using var output = File.Create(destinationDllPath);
        input.CopyTo(output);
    }

    /// <summary>For tests and diagnostics.</summary>
    public static ZipArchiveEntry? FindLibcurlDllEntry(ZipArchive zip)
    {
        ZipArchiveEntry? best = null;
        var bestScore = 0;

        foreach (var entry in zip.Entries)
        {
            if (entry.Length == 0)
            {
                continue;
            }

            var normalized = entry.FullName.Replace('\\', '/');
            if (!TryGetBinFilename(normalized, out var fileName))
            {
                continue;
            }

            var score = ScoreLibcurlDllName(fileName);
            if (score > bestScore)
            {
                bestScore = score;
                best = entry;
            }
        }

        return best;
    }

    private static bool TryGetBinFilename(string normalizedFullName, out string fileName)
    {
        fileName = "";
        const string marker = "/bin/";
        var i = normalizedFullName.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            return false;
        }

        var rest = normalizedFullName[(i + marker.Length)..];
        if (rest.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        fileName = rest;
        return !string.IsNullOrEmpty(fileName);
    }

    /// <summary>Higher score wins. Zero means ignore.</summary>
    private static int ScoreLibcurlDllName(string fileName)
    {
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (fileName.Equals("libcurl-4.dll", StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (fileName.Equals("libcurl-x64.dll", StringComparison.OrdinalIgnoreCase))
        {
            return 90;
        }

        if (fileName.StartsWith("libcurl-", StringComparison.OrdinalIgnoreCase))
        {
            return 50;
        }

        return 0;
    }
}
