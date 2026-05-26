using System.Runtime.InteropServices;

namespace QuotaMonitor.Core.Infrastructure;

public interface IAppPaths
{
    string HomeDirectory { get; }
    string AppDataDirectory { get; }
    string ConfigPath { get; }
    string HistoryPath { get; }
    string LogPath { get; }
    string SnapshotPath { get; }
    string LegacyBaseDirectory { get; }
}

public sealed class DefaultAppPaths : IAppPaths
{
    private DefaultAppPaths(
        string homeDirectory,
        string appDataDirectory,
        string legacyBaseDirectory)
    {
        HomeDirectory = homeDirectory;
        AppDataDirectory = appDataDirectory;
        LegacyBaseDirectory = legacyBaseDirectory;
    }

    public string HomeDirectory { get; }
    public string AppDataDirectory { get; }
    public string LegacyBaseDirectory { get; }

    public string ConfigPath => Path.Combine(AppDataDirectory, "quota-monitor.config.json");
    public string HistoryPath => Path.Combine(AppDataDirectory, "quota-monitor-history.jsonl");
    public string LogPath => Path.Combine(AppDataDirectory, "quota-monitor.log");
    public string SnapshotPath => Path.Combine(AppDataDirectory, "quota-monitor.snapshot.txt");

    public static DefaultAppPaths Create(string legacyBaseDirectory = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = ResolveAppDataDirectory(home);
        return new DefaultAppPaths(home, appData, legacyBaseDirectory ?? AppContext.BaseDirectory);
    }

    private static string ResolveAppDataDirectory(string home)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Path.Combine(home, "Library", "Application Support", "QuotaMonitor");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                return Path.Combine(appData, "QuotaMonitor");
            }
        }

        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            return Path.Combine(xdgConfigHome, "QuotaMonitor");
        }

        return Path.Combine(home, ".config", "QuotaMonitor");
    }
}
