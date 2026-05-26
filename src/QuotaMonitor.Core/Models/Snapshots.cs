namespace QuotaMonitor.Core.Models;

public sealed class QuotaSnapshot
{
    public DateTimeOffset UpdatedAt { get; set; }
    public CodexSnapshot Codex { get; set; }
    public ClaudeSnapshot Claude { get; set; }
}

public sealed class CodexSnapshot
{
    public bool Available { get; set; }
    public string Error { get; set; }
    public string Source { get; set; }
    public string FallbackError { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string PlanType { get; set; }
    public string LimitId { get; set; }
    public string RateLimitReachedType { get; set; }
    public CodexWindow Primary { get; set; }
    public CodexWindow Secondary { get; set; }
    public long TotalTokens { get; set; }
    public long LastTurnTokens { get; set; }

    public static CodexSnapshot Missing(string error)
    {
        return new CodexSnapshot
        {
            Available = false,
            Error = error,
            Source = "none",
            Timestamp = DateTimeOffset.Now
        };
    }

    public static CodexSnapshot Hidden()
    {
        return new CodexSnapshot
        {
            Available = false,
            Error = "hidden",
            Source = "hidden",
            Timestamp = DateTimeOffset.Now
        };
    }
}

public sealed class CodexWindow
{
    public double? UsedPercent { get; set; }
    public double? RemainingPercent { get; set; }
    public DateTimeOffset? ResetsAt { get; set; }
    public int? WindowMinutes { get; set; }
}

public sealed class ClaudeSnapshot
{
    public bool Available { get; set; }
    public string Error { get; set; }
    public string Source { get; set; }
    public string FallbackError { get; set; }
    public string PlanType { get; set; }
    public int WindowMinutes { get; set; }
    public int WeekWindowMinutes { get; set; }
    public int MessageBudget { get; set; }
    public long TokenBudget { get; set; }
    public int WeeklyMessageBudget { get; set; }
    public long WeeklyTokenBudget { get; set; }
    public int MessageCount { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long WeightedTokens { get; set; }
    public int WeeklyMessageCount { get; set; }
    public long WeeklyInputTokens { get; set; }
    public long WeeklyOutputTokens { get; set; }
    public long WeeklyCacheCreationTokens { get; set; }
    public long WeeklyCacheReadTokens { get; set; }
    public long WeeklyWeightedTokens { get; set; }
    public DateTimeOffset? OldestCountedAt { get; set; }
    public DateTimeOffset? OldestWeeklyCountedAt { get; set; }
    public DateTimeOffset? EstimatedResetAt { get; set; }
    public DateTimeOffset? EstimatedWeeklyResetAt { get; set; }
    public CodexWindow RealtimeFiveHour { get; set; }
    public CodexWindow RealtimeWeek { get; set; }
    public HashSet<string> Models { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int? RemainingMessages => MessageBudget <= 0 ? null : Math.Max(0, MessageBudget - MessageCount);
    public long? RemainingTokens => TokenBudget <= 0 ? null : Math.Max(0, TokenBudget - WeightedTokens);
    public int? WeeklyRemainingMessages => WeeklyMessageBudget <= 0 ? null : Math.Max(0, WeeklyMessageBudget - WeeklyMessageCount);
    public long? WeeklyRemainingTokens => WeeklyTokenBudget <= 0 ? null : Math.Max(0, WeeklyTokenBudget - WeeklyWeightedTokens);

    public double? RemainingTokenPercent =>
        TokenBudget <= 0 ? null : Math.Max(0, Math.Min(100, (TokenBudget - WeightedTokens) * 100.0 / TokenBudget));

    public double? WeeklyRemainingTokenPercent =>
        WeeklyTokenBudget <= 0 ? null : Math.Max(0, Math.Min(100, (WeeklyTokenBudget - WeeklyWeightedTokens) * 100.0 / WeeklyTokenBudget));

    public double? RemainingMessagePercent =>
        MessageBudget <= 0 || !RemainingMessages.HasValue ? null : Math.Max(0, Math.Min(100, RemainingMessages.Value * 100.0 / MessageBudget));

    public double? WeeklyRemainingMessagePercent =>
        WeeklyMessageBudget <= 0 || !WeeklyRemainingMessages.HasValue ? null : Math.Max(0, Math.Min(100, WeeklyRemainingMessages.Value * 100.0 / WeeklyMessageBudget));

    public static ClaudeSnapshot Missing(string error)
    {
        return new ClaudeSnapshot
        {
            Available = false,
            Source = "none",
            Error = error
        };
    }

    public static ClaudeSnapshot Hidden()
    {
        return new ClaudeSnapshot
        {
            Available = false,
            Source = "hidden",
            Error = "hidden"
        };
    }
}
