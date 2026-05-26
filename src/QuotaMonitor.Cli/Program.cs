using QuotaMonitor.Core.Config;
using QuotaMonitor.Core.Infrastructure;
using QuotaMonitor.Core.Services;

var paths = DefaultAppPaths.Create();
var log = new AppLog(paths);

try
{
    log.Write("cli start args=" + string.Join(" ", args));
    var config = MonitorConfig.LoadOrCreate(paths);
    var snapshot = new QuotaReader(paths).Read(config);
    new SnapshotWriter(paths).Write(snapshot);

    Console.WriteLine("Quota Monitor snapshot written:");
    Console.WriteLine(paths.SnapshotPath);
    Console.WriteLine("Codex: " + ShortState(snapshot.Codex.Available, snapshot.Codex.Source, snapshot.Codex.Error));
    Console.WriteLine("Claude: " + ShortState(snapshot.Claude.Available, snapshot.Claude.Source, snapshot.Claude.Error));
    return 0;
}
catch (Exception ex)
{
    log.Write("cli fatal: " + ex);
    Console.Error.WriteLine(ex);
    return 1;
}

static string ShortState(bool available, string source, string error)
{
    return available ? source ?? "available" : error ?? "no data";
}
