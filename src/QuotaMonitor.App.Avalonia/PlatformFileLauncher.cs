using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace QuotaMonitor.App.Avalonia;

internal static class PlatformFileLauncher
{
    public static void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { path },
                UseShellExecute = false
            });
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = Directory.Exists(path) ? path : Path.GetFullPath(path),
            UseShellExecute = true
        });
    }
}
