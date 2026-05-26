using System.Globalization;
using QuotaMonitor.Core.Config;
using QuotaMonitor.Core.Models;

namespace QuotaMonitor.Core.Services;

public sealed class AlertEvaluator
{
    private readonly HashSet<string> _shownAlertKeys = new(StringComparer.OrdinalIgnoreCase);

    public AlertEvaluation Evaluate(MonitorConfig config, QuotaSnapshot snapshot)
    {
        if (!config.alertsEnabled)
        {
            return AlertEvaluation.None;
        }

        var activeAlerts = new List<string>();
        var newAlerts = new List<string>();

        if (config.CodexVisible && snapshot.Codex.Available)
        {
            AddWindowAlert("Codex", "5h", snapshot.Codex.Primary, config.alertFiveHourRemainingPercent, activeAlerts, newAlerts);
            AddWindowAlert("Codex", "Week", snapshot.Codex.Secondary, config.alertLongWindowRemainingPercent, activeAlerts, newAlerts);
        }

        if (config.ClaudeVisible && snapshot.Claude.Available)
        {
            AddWindowAlert("Claude", "5h", snapshot.Claude.RealtimeFiveHour, config.alertFiveHourRemainingPercent, activeAlerts, newAlerts);
            AddWindowAlert("Claude", "7d", snapshot.Claude.RealtimeWeek, config.alertLongWindowRemainingPercent, activeAlerts, newAlerts);

            if (snapshot.Claude.RealtimeFiveHour == null)
            {
                AddRemainingAlert(
                    "Claude",
                    "5h",
                    snapshot.Claude.RemainingTokenPercent ?? snapshot.Claude.RemainingMessagePercent,
                    snapshot.Claude.EstimatedResetAt,
                    config.alertFiveHourRemainingPercent,
                    activeAlerts,
                    newAlerts);
            }

            if (snapshot.Claude.RealtimeWeek == null)
            {
                AddRemainingAlert(
                    "Claude",
                    "Week",
                    snapshot.Claude.WeeklyRemainingTokenPercent ?? snapshot.Claude.WeeklyRemainingMessagePercent,
                    snapshot.Claude.EstimatedWeeklyResetAt,
                    config.alertLongWindowRemainingPercent,
                    activeAlerts,
                    newAlerts);
            }
        }

        var summary = activeAlerts.Count == 0
            ? null
            : string.Join("; ", activeAlerts.Take(3)) + (activeAlerts.Count > 3 ? "; ..." : string.Empty);

        return new AlertEvaluation(activeAlerts, newAlerts, summary);
    }

    private void AddWindowAlert(
        string service,
        string windowName,
        CodexWindow window,
        double threshold,
        List<string> activeAlerts,
        List<string> newAlerts)
    {
        if (window == null)
        {
            return;
        }

        AddRemainingAlert(service, windowName, window.RemainingPercent, window.ResetsAt, threshold, activeAlerts, newAlerts);
    }

    private void AddRemainingAlert(
        string service,
        string windowName,
        double? remainingPercent,
        DateTimeOffset? resetAt,
        double threshold,
        List<string> activeAlerts,
        List<string> newAlerts)
    {
        if (!remainingPercent.HasValue || threshold <= 0)
        {
            return;
        }

        var remaining = Math.Max(0, Math.Min(100, remainingPercent.Value));
        var prefix = service + "|" + windowName + "|";
        if (remaining > threshold)
        {
            ClearAlertKeys(prefix);
            return;
        }

        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} {2:0}% left",
            service,
            windowName,
            remaining);
        activeAlerts.Add(summary);

        var key = prefix +
            (resetAt.HasValue ? resetAt.Value.UtcDateTime.ToString("o", CultureInfo.InvariantCulture) : "no-reset") +
            "|" + threshold.ToString("0.##", CultureInfo.InvariantCulture);
        if (!_shownAlertKeys.Contains(key))
        {
            _shownAlertKeys.Add(key);
            newAlerts.Add(summary + " (threshold " + threshold.ToString("0", CultureInfo.InvariantCulture) + "%)");
        }
    }

    private void ClearAlertKeys(string prefix)
    {
        var remove = _shownAlertKeys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in remove)
        {
            _shownAlertKeys.Remove(key);
        }
    }
}

public sealed class AlertEvaluation
{
    public static readonly AlertEvaluation None = new(new List<string>(), new List<string>(), null);

    public AlertEvaluation(
        IReadOnlyList<string> activeAlerts,
        IReadOnlyList<string> newAlerts,
        string summary)
    {
        ActiveAlerts = activeAlerts;
        NewAlerts = newAlerts;
        Summary = summary;
    }

    public IReadOnlyList<string> ActiveAlerts { get; }
    public IReadOnlyList<string> NewAlerts { get; }
    public string Summary { get; }
}
