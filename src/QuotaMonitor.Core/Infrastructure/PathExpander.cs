namespace QuotaMonitor.Core.Infrastructure;

public static class PathExpander
{
    public static string Expand(string path, IAppPaths paths)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path ?? string.Empty);
        if (expanded.StartsWith("~/", StringComparison.Ordinal) ||
            expanded.StartsWith("~\\", StringComparison.Ordinal))
        {
            expanded = Path.Combine(paths.HomeDirectory, expanded[2..]);
        }
        else if (string.Equals(expanded, "~", StringComparison.Ordinal))
        {
            expanded = paths.HomeDirectory;
        }

        return expanded;
    }
}
