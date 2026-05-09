using System.Text.Json;
using OrionBE.Launcher.Core;

namespace OrionBE.Launcher.Services;

public sealed class StartupDependencyCheckService : IStartupDependencyCheckService
{
    private readonly IFileSystemService _fileSystem;

    public StartupDependencyCheckService(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<StartupDependencyCheckReport> RunIfFirstLaunchAsync(CancellationToken cancellationToken = default)
    {
        await _fileSystem.EnsureDirectoryAsync(OrionPaths.Root, cancellationToken).ConfigureAwait(false);

        if (File.Exists(OrionPaths.FirstLaunchDependencyCheckMarker))
        {
            return new StartupDependencyCheckReport { IsFirstLaunch = false };
        }

        var missing = DetectMissingDependencies();
        var payload = JsonSerializer.Serialize(
            new
            {
                checkedAtUtc = DateTime.UtcNow,
                missingCount = missing.Count,
                missing,
            });
        await File.WriteAllTextAsync(OrionPaths.FirstLaunchDependencyCheckMarker, payload, cancellationToken)
            .ConfigureAwait(false);

        return new StartupDependencyCheckReport
        {
            IsFirstLaunch = true,
            MissingItems = missing,
        };
    }

    private static List<string> DetectMissingDependencies()
    {
        var missing = new List<string>();

        if (OperatingSystem.IsLinux())
        {
            if (!IsCommandAvailable("tar"))
            {
                missing.Add("Missing command: tar");
            }

            if (!IsCommandAvailable("chmod"))
            {
                missing.Add("Missing command: chmod");
            }

            if (!IsCommandAvailable("pgrep"))
            {
                missing.Add("Missing command: pgrep");
            }
        }

        var combasePath = Path.Combine(AppContext.BaseDirectory, "SystemFiles", "system32", "combase.dll");
        if (!File.Exists(combasePath))
        {
            missing.Add("Missing runtime asset: SystemFiles/system32/combase.dll");
        }

        return missing;
    }

    private static bool IsCommandAvailable(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir, command);
                if (File.Exists(full))
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }
}
