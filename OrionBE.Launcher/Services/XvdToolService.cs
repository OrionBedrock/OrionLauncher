using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrionBE.Launcher.Core;

namespace OrionBE.Launcher.Services;

public sealed class XvdToolService : IXvdToolService
{
    public const string Repository = "AmethystAPI/xvdtool";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDownloadService _downloadService;
    private readonly IFileSystemService _fileSystem;
    private readonly ILogger<XvdToolService> _logger;

    public XvdToolService(
        IHttpClientFactory httpClientFactory,
        IDownloadService downloadService,
        IFileSystemService fileSystem,
        ILogger<XvdToolService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _downloadService = downloadService;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<string?> EnsureToolAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsMacOS())
        {
            _logger.LogWarning("XVDTool não suporta macOS.");
            return null;
        }

        if (OperatingSystem.IsLinux() && System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
            != System.Runtime.InteropServices.Architecture.X64)
        {
            _logger.LogWarning("XVDTool oficial é Linux x64; arquitetura atual não suportada.");
            return null;
        }

        if (OperatingSystem.IsWindows() && System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
            != System.Runtime.InteropServices.Architecture.X64)
        {
            _logger.LogWarning("XVDTool oficial é Windows x64; arquitetura atual não suportada.");
            return null;
        }

        await _fileSystem.EnsureDirectoryAsync(OrionPaths.Cache, cancellationToken).ConfigureAwait(false);
        var toolsRoot = Path.Combine(OrionPaths.Cache, "tools", "xvdtool");
        await _fileSystem.EnsureDirectoryAsync(toolsRoot, cancellationToken).ConfigureAwait(false);

        var exeName = OperatingSystem.IsWindows() ? "XVDTool.exe" : "XVDTool";
        var existing = FindExecutable(toolsRoot, exeName);
        if (existing is not null)
        {
            return existing;
        }

        var client = _httpClientFactory.CreateClient("GitHub");
        var url = $"https://api.github.com/repos/{Repository}/releases/latest";
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GitHub API falhou para {Repo}: {Status}", Repository, response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagEl))
        {
            return null;
        }

        var tag = tagEl.GetString() ?? "unknown";
        if (!root.TryGetProperty("assets", out var assets) || assets.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement chosen = default;
        JsonElement fallbackArchive = default;
        foreach (var a in assets.EnumerateArray())
        {
            if (!a.TryGetProperty("name", out var nameEl))
            {
                continue;
            }

            var name = nameEl.GetString() ?? "";
            if (!IsSupportedArchive(name))
            {
                continue;
            }

            if (fallbackArchive.ValueKind == JsonValueKind.Undefined)
            {
                fallbackArchive = a;
            }

            var lower = name.ToLowerInvariant();
            if (OperatingSystem.IsLinux() && lower.Contains("linux") && lower.Contains("x64"))
            {
                chosen = a;
                break;
            }

            if (OperatingSystem.IsWindows() && (lower.Contains("win") || lower.Contains("windows")) && lower.Contains("x64"))
            {
                chosen = a;
                break;
            }
        }

        if (chosen.ValueKind == JsonValueKind.Undefined)
        {
            chosen = fallbackArchive;
        }

        if (chosen.ValueKind == JsonValueKind.Undefined)
        {
            _logger.LogError("Nenhum asset xvdtool compatível com esta plataforma.");
            return null;
        }

        var downloadUrl = chosen.GetProperty("browser_download_url").GetString();
        var assetName = chosen.GetProperty("name").GetString();
        if (string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(assetName))
        {
            return null;
        }

        var versionDir = Path.Combine(toolsRoot, SanitizeTag(tag));
        await _fileSystem.EnsureDirectoryAsync(versionDir, cancellationToken).ConfigureAwait(false);
        var extractDir = Path.Combine(versionDir, "extracted");
        if (Directory.Exists(extractDir))
        {
            Directory.Delete(extractDir, recursive: true);
        }

        await _fileSystem.EnsureDirectoryAsync(extractDir, cancellationToken).ConfigureAwait(false);
        var archivePath = Path.Combine(versionDir, assetName);
        await _downloadService
            .DownloadToFileAsync(new Uri(downloadUrl), archivePath, null, cancellationToken)
            .ConfigureAwait(false);

        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, extractDir, overwriteFiles: true);
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            || archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            await RunProcessAsync("tar", ["-xzf", archivePath, "-C", extractDir], cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new InvalidOperationException($"Formato de arquivo xvdtool não suportado: {assetName}");
        }

        try
        {
            File.Delete(archivePath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível apagar arquivo {Path}", archivePath);
        }

        if (OperatingSystem.IsLinux())
        {
            await RunProcessAsync("chmod", ["-R", "755", extractDir], cancellationToken).ConfigureAwait(false);
        }

        return FindExecutable(extractDir, exeName);
    }

    public async Task DecryptMsixvcInPlaceAsync(string xvdtoolPath, string msixvcPath, CancellationToken cancellationToken = default)
    {
        Exception? last = null;
        foreach (var (uuid, hex) in BedrockCikKeys.UuidToHex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await RunXvdToolAsync(
                        xvdtoolPath,
                        ["-nd", "-eu", "-cik", uuid, "-cikdata", hex, msixvcPath],
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                _logger.LogDebug(ex, "CIK {Uuid} falhou para {File}", uuid, msixvcPath);
            }
        }

        throw new InvalidOperationException(
            "Falha ao desencriptar o pacote .msixvc com as chaves CIK conhecidas. "
            + (last is not null ? last.Message : ""));
    }

    public Task ExtractMsixvcAsync(string xvdtoolPath, string msixvcPath, string outputFolder, CancellationToken cancellationToken = default) =>
        RunXvdToolAsync(xvdtoolPath, ["-nd", "-xf", outputFolder, msixvcPath], cancellationToken);

    private static string SanitizeTag(string tag) =>
        string.Concat(tag.Select(static c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));

    private static bool IsSupportedArchive(string name) =>
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

    private static string? FindExecutable(string root, string exeName)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(file), exeName, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static async Task RunProcessAsync(string fileName, string[] args, CancellationToken cancellationToken)
    {
        using var proc = new Process();
        proc.StartInfo.FileName = fileName;
        proc.StartInfo.RedirectStandardOutput = true;
        proc.StartInfo.RedirectStandardError = true;
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.CreateNoWindow = true;
        foreach (var a in args)
        {
            proc.StartInfo.ArgumentList.Add(a);
        }

        proc.Start();
        await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"{fileName} saiu com código {proc.ExitCode}: {err}");
        }
    }

    private static async Task RunXvdToolAsync(string xvdtoolPath, string[] args, CancellationToken cancellationToken)
    {
        using var proc = new Process();
        proc.StartInfo.FileName = xvdtoolPath;
        proc.StartInfo.RedirectStandardOutput = true;
        proc.StartInfo.RedirectStandardError = true;
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.CreateNoWindow = true;
        foreach (var a in args)
        {
            proc.StartInfo.ArgumentList.Add(a);
        }

        proc.Start();
        var stdout = proc.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = proc.StandardError.ReadToEndAsync(cancellationToken);
        await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        _ = await stdout.ConfigureAwait(false);
        var err = await stderr.ConfigureAwait(false);
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"XVDTool saiu com código {proc.ExitCode}. {err}".Trim());
        }
    }
}
