using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using QuotaMonitor.Core.Infrastructure;

namespace QuotaMonitor.App.Avalonia;

internal static class CrashReporter
{
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Write("Unhandled exception", ex);
            }
            else
            {
                Write("Unhandled exception object: " + Convert.ToString(args.ExceptionObject, CultureInfo.InvariantCulture));
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Write("Unobserved task exception", args.Exception);
        };
    }

    public static void Write(string message)
    {
        WriteRaw(message);
    }

    public static void Write(string message, Exception exception)
    {
        WriteRaw(message + Environment.NewLine + exception);
    }

    private static void WriteRaw(string text)
    {
        var entry = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture) + " " + text + Environment.NewLine;
        foreach (var path in ResolveCrashLogPaths())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, entry);
            }
            catch
            {
                // Try the next crash log path.
            }
        }
    }

    private static IEnumerable<string> ResolveCrashLogPaths()
    {
        var paths = new List<string>();
        try
        {
            paths.Add(Path.Combine(DefaultAppPaths.Create().AppDataDirectory, "quota-monitor-crash.log"));
        }
        catch
        {
            // Keep fallback paths below.
        }

        paths.Add(Path.Combine(AppContext.BaseDirectory, "quota-monitor-crash.log"));
        paths.Add(Path.Combine(Path.GetTempPath(), "quota-monitor-crash.log"));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
