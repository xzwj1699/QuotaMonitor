using System.Globalization;

namespace QuotaMonitor.Core.Infrastructure;

public sealed class AppLog
{
    private readonly IAppPaths _paths;
    private readonly object _gate = new();

    public AppLog(IAppPaths paths)
    {
        _paths = paths;
    }

    public void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_paths.LogPath)!);
            lock (_gate)
            {
                File.AppendAllText(
                    _paths.LogPath,
                    DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never prevent the monitor from starting.
        }
    }
}
