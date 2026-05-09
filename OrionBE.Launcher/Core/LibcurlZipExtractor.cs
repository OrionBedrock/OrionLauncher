using System.IO.Compression;

namespace OrionBE.Launcher.Core;

/// <summary>
/// Extrai a DLL do libcurl dos zips oficiais Windows (curl.se / curl-for-win).
/// Builds recentes usam <c>bin/libcurl-x64.dll</c>; pacotes MSYS2/antigos usavam <c>bin/libcurl-4.dll</c>.
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
                "Não foi encontrado libcurl no zip (esperado bin/libcurl-4.dll ou bin/libcurl-x64.dll, ou outro bin/libcurl-*.dll).");
        }

        using var input = entry.Open();
        using var output = File.Create(destinationDllPath);
        input.CopyTo(output);
    }

    /// <summary>Para testes e diagnóstico.</summary>
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

    /// <summary>Maior = melhor. Zero = ignorar.</summary>
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
