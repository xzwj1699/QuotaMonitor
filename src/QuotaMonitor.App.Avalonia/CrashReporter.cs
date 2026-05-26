using System;
using System.Globalization;
using System.IO;
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
        try
        {
            var path = ResolveCrashLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(
                path,
                DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture) + " " + text + Environment.NewLine);
        }
        catch
        {
            // Crash logging must never cause a second startup failure.
        }
    }

    private static string ResolveCrashLogPath()
    {
        try
        {
            return Path.Combine(DefaultAppPaths.Create().AppDataDirectory, "quota-monitor-crash.log");
        }
        catch
        {
            return Path.Combine(AppContext.BaseDirectory, "quota-monitor-crash.log");
        }
    }
}
