using System.Globalization;
using QuotaMonitor.Core.Infrastructure;
using QuotaMonitor.Core.Models;

namespace QuotaMonitor.Core.Services;

public sealed class SnapshotWriter
{
    private readonly IAppPaths _paths;

    public SnapshotWriter(IAppPaths paths)
    {
        _paths = paths;
    }

    public void Write(QuotaSnapshot snapshot)
    {
        Directory.CreateDirectory(_paths.AppDataDirectory);
        var lines = new[]
        {
            "updatedAt=" + snapshot.UpdatedAt.ToString("o", CultureInfo.InvariantCulture),
            "codex.available=" + snapshot.Codex.Available,
            "codex.source=" + snapshot.Codex.Source,
            "codex.planType=" + Sanitize(snapshot.Codex.PlanType),
            "codex.fallbackError=" + Sanitize(snapshot.Codex.FallbackError),
            "codex.primaryRemaining=" + NullableDouble(snapshot.Codex.Primary == null ? null : snapshot.Codex.Primary.RemainingPercent),
            "codex.secondaryRemaining=" + NullableDouble(snapshot.Codex.Secondary == null ? null : snapshot.Codex.Secondary.RemainingPercent),
            "codex.totalTokens=" + snapshot.Codex.TotalTokens,
            "claude.available=" + snapshot.Claude.Available,
            "claude.source=" + snapshot.Claude.Source,
            "claude.planType=" + Sanitize(snapshot.Claude.PlanType),
            "claude.fallbackError=" + Sanitize(snapshot.Claude.FallbackError),
            "claude.realtimeFiveHourRemaining=" + NullableDouble(snapshot.Claude.RealtimeFiveHour == null ? null : snapshot.Claude.RealtimeFiveHour.RemainingPercent),
            "claude.realtimeWeekRemaining=" + NullableDouble(snapshot.Claude.RealtimeWeek == null ? null : snapshot.Claude.RealtimeWeek.RemainingPercent),
            "claude.messageCount=" + snapshot.Claude.MessageCount,
            "claude.remainingMessages=" + NullableInt(snapshot.Claude.RemainingMessages),
            "claude.weightedTokens=" + snapshot.Claude.WeightedTokens,
            "claude.weeklyMessageCount=" + snapshot.Claude.WeeklyMessageCount,
            "claude.weeklyRemainingMessages=" + NullableInt(snapshot.Claude.WeeklyRemainingMessages),
            "claude.weeklyWeightedTokens=" + snapshot.Claude.WeeklyWeightedTokens
        };
        File.WriteAllLines(_paths.SnapshotPath, lines);
    }

    private static string NullableDouble(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string NullableInt(int? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string Sanitize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace("\r", " ").Replace("\n", " ");
    }
}
