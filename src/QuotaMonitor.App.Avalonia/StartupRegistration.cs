using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace QuotaMonitor.App.Avalonia;

internal sealed record StartupRegistrationResult(bool Success, string Message);

internal static class StartupRegistration
{
    private const string AppName = "QuotaMonitor";
    private const string LaunchAgentIdentifier = "dev.quotamonitor.app";

    public static StartupRegistrationResult Apply(bool enabled)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return ApplyMacOs(enabled);
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return ApplyWindows(enabled);
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return ApplyLinux(enabled);
            }

            return new StartupRegistrationResult(false, "Start with system is not supported on this platform.");
        }
        catch (Exception ex)
        {
            return new StartupRegistrationResult(false, ex.Message);
        }
    }

    private static StartupRegistrationResult ApplyMacOs(bool enabled)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var launchAgents = Path.Combine(home, "Library", "LaunchAgents");
        var plistPath = Path.Combine(launchAgents, LaunchAgentIdentifier + ".plist");

        if (!enabled)
        {
            DeleteIfExists(plistPath);
            return new StartupRegistrationResult(true, "Start with system disabled.");
        }

        Directory.CreateDirectory(launchAgents);
        var appBundle = FindMacAppBundle();
        var arguments = appBundle == null
            ? BuildPlistStringArray(ResolveProcessPath())
            : BuildPlistStringArray("/usr/bin/open", "-n", appBundle);

        var plist =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">" + Environment.NewLine +
            "<plist version=\"1.0\">" + Environment.NewLine +
            "<dict>" + Environment.NewLine +
            "  <key>Label</key>" + Environment.NewLine +
            "  <string>" + EscapeXml(LaunchAgentIdentifier) + "</string>" + Environment.NewLine +
            "  <key>ProgramArguments</key>" + Environment.NewLine +
            arguments +
            "  <key>RunAtLoad</key>" + Environment.NewLine +
            "  <true/>" + Environment.NewLine +
            "</dict>" + Environment.NewLine +
            "</plist>" + Environment.NewLine;

        File.WriteAllText(plistPath, plist);
        return new StartupRegistrationResult(true, "Start with system enabled.");
    }

    [SupportedOSPlatform("windows")]
    private static StartupRegistrationResult ApplyWindows(bool enabled)
    {
        const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        using var runKey = Registry.CurrentUser.CreateSubKey(runKeyPath, writable: true);
        if (runKey == null)
        {
            return new StartupRegistrationResult(false, "Could not open the Windows Run registry key.");
        }

        if (!enabled)
        {
            runKey.DeleteValue(AppName, throwOnMissingValue: false);
            return new StartupRegistrationResult(true, "Start with system disabled.");
        }

        runKey.SetValue(AppName, QuoteCommand(ResolveProcessPath()));
        return new StartupRegistrationResult(true, "Start with system enabled.");
    }

    private static StartupRegistrationResult ApplyLinux(bool enabled)
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
        {
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        var autostartDir = Path.Combine(configHome, "autostart");
        var desktopPath = Path.Combine(autostartDir, LaunchAgentIdentifier + ".desktop");
        if (!enabled)
        {
            DeleteIfExists(desktopPath);
            return new StartupRegistrationResult(true, "Start with system disabled.");
        }

        Directory.CreateDirectory(autostartDir);
        var desktopEntry =
            "[Desktop Entry]" + Environment.NewLine +
            "Type=Application" + Environment.NewLine +
            "Name=Quota Monitor" + Environment.NewLine +
            "Exec=" + QuoteCommand(ResolveProcessPath()) + Environment.NewLine +
            "Terminal=false" + Environment.NewLine +
            "X-GNOME-Autostart-enabled=true" + Environment.NewLine;
        File.WriteAllText(desktopPath, desktopEntry);
        return new StartupRegistrationResult(true, "Start with system enabled.");
    }

    private static string ResolveProcessPath()
    {
        return Environment.ProcessPath ?? throw new InvalidOperationException("Could not resolve the current executable path.");
    }

    private static string? FindMacAppBundle()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (string.Equals(directory.Extension, ".app", StringComparison.OrdinalIgnoreCase))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string BuildPlistStringArray(params string[] values)
    {
        return "  <array>" + Environment.NewLine +
            string.Concat(values.Select(value => "    <string>" + EscapeXml(value) + "</string>" + Environment.NewLine)) +
            "  </array>" + Environment.NewLine;
    }

    private static string QuoteCommand(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string EscapeXml(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
