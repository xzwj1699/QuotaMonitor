using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuotaMonitor.App.Avalonia;

internal static class PlatformNotifier
{
    public static void Show(string title, string message)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ShowMacOs(title, message);
        }
    }

    private static void ShowMacOs(string title, string message)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            info.ArgumentList.Add("-e");
            info.ArgumentList.Add(
                "display notification " + AppleScriptString(message) +
                " with title " + AppleScriptString(title));
            Process.Start(info)?.Dispose();
        }
        catch
        {
            // Native notifications are best-effort; the in-window notification remains the fallback.
        }
    }

    private static string AppleScriptString(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal) + "\"";
    }
}
