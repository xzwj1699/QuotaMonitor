using System.Globalization;
using System.Text.Json.Serialization;

namespace QuotaMonitor.Core.Models;

public sealed class UsageSample
{
    public string Service { get; set; }
    public string Window { get; set; }
    public string Timestamp { get; set; }
    public double UsedPercent { get; set; }
    public string ResetAt { get; set; }
    public int WindowMinutes { get; set; }

    [JsonIgnore]
    public DateTimeOffset TimestampValue => DateTimeOffset.Parse(Timestamp, CultureInfo.InvariantCulture);

    [JsonIgnore]
    public DateTimeOffset ResetAtValue => DateTimeOffset.Parse(ResetAt, CultureInfo.InvariantCulture);
}

public enum HistoryAggregation
{
    Day,
    Week,
    Month
}

public sealed class UsageHistoryPoint
{
    public DateTime PeriodStart { get; set; }
    public string Label { get; set; }
    public double UsedPercent { get; set; }
}
