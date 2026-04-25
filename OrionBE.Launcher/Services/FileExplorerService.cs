using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OrionBE.Launcher.Services;

public sealed class FileExplorerService : IFileExplorerService
{
    public void RevealInFileManager(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        if (File.Exists(full))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{full}\"",
                    UseShellExecute = true,
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Start(new ProcessStartInfo
                {
                    FileName = "open",
                    UseShellExecute = false,
                }, "-R", full);
            }
            else
            {
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Start(new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        UseShellExecute = false,
                    }, dir);
                }
            }

            return;
        }

        if (!Directory.Exists(full))
        {
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = full,
                UseShellExecute = true,
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Start(new ProcessStartInfo
            {
                FileName = "open",
                UseShellExecute = false,
            }, full);
        }
        else
        {
            Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                UseShellExecute = false,
            }, full);
        }
    }

    private static void Start(ProcessStartInfo baseInfo, string singleArg) =>
        StartWithArgs(baseInfo, [singleArg]);

    private static void Start(ProcessStartInfo baseInfo, string a, string b) =>
        StartWithArgs(baseInfo, [a, b]);

    private static void StartWithArgs(ProcessStartInfo baseInfo, string[] args)
    {
        try
        {
            foreach (var a in args)
            {
                baseInfo.ArgumentList.Add(a);
            }

            _ = Process.Start(baseInfo);
        }
        catch
        {
        }
    }

    private static void Start(ProcessStartInfo psi)
    {
        try
        {
            _ = Process.Start(psi);
        }
        catch
        {
        }
    }
}
