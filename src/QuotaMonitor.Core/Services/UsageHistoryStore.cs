using System.Globalization;
using QuotaMonitor.Core.Infrastructure;
using QuotaMonitor.Core.Models;

namespace QuotaMonitor.Core.Services;

public sealed class UsageHistoryStore
{
    private readonly IAppPaths _paths;
    private readonly object _gate = new();

    public UsageHistoryStore(IAppPaths paths)
    {
        _paths = paths;
    }

    public void AppendSnapshot(QuotaSnapshot snapshot)
    {
        var samples = BuildSamples(snapshot);
        if (samples.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(_paths.AppDataDirectory);
        lock (_gate)
        {
            using var writer = new StreamWriter(_paths.HistoryPath, true);
            foreach (var sample in samples)
            {
                writer.WriteLine(JsonUtil.Serialize(sample));
            }
        }
    }

    public List<UsageSample> Load(string service, string window, DateTimeOffset resetAt)
    {
        var result = new List<UsageSample>();
        if (!File.Exists(_paths.HistoryPath))
        {
            return result;
        }

        lock (_gate)
        {
            foreach (var line in File.ReadLines(_paths.HistoryPath).Reverse().Take(2000).Reverse())
            {
                var dict = JsonUtil.ParseObject(line);
                if (dict == null)
                {
                    continue;
                }

                var sampleService = JsonUtil.String(dict, "Service");
                var sampleWindow = JsonUtil.String(dict, "Window");
                if (!string.Equals(sampleService, service, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(sampleWindow, window, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sampleReset = JsonUtil.FlexibleDate(JsonUtil.Value(dict, "ResetAt"));
                if (!sampleReset.HasValue || Math.Abs((sampleReset.Value - resetAt).TotalMinutes) > 2)
                {
                    continue;
                }

                var sample = ParseUsageSample(dict, sampleService, sampleWindow, sampleReset.Value);
                if (sample != null)
                {
                    result.Add(sample);
                }
            }
        }

        return result
            .GroupBy(s => s.Timestamp)
            .Select(g => g.Last())
            .OrderBy(s => s.TimestampValue)
            .ToList();
    }

    public List<UsageHistoryPoint> LoadUsageHistory(string service, string window, HistoryAggregation aggregation)
    {
        return BuildUsageHistory(LoadAll(service, window), aggregation);
    }

    private List<UsageSample> LoadAll(string service, string window)
    {
        var result = new List<UsageSample>();
        if (!File.Exists(_paths.HistoryPath))
        {
            return result;
        }

        lock (_gate)
        {
            foreach (var line in File.ReadLines(_paths.HistoryPath).Reverse().Take(50000).Reverse())
            {
                var dict = JsonUtil.ParseObject(line);
                if (dict == null)
                {
                    continue;
                }

                var sampleService = JsonUtil.String(dict, "Service");
                var sampleWindow = JsonUtil.String(dict, "Window");
                if (!string.Equals(sampleService, service, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(sampleWindow, window, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var resetAt = JsonUtil.FlexibleDate(JsonUtil.Value(dict, "ResetAt"));
                if (!resetAt.HasValue)
                {
                    continue;
                }

                var sample = ParseUsageSample(dict, sampleService, sampleWindow, resetAt.Value);
                if (sample != null)
                {
                    result.Add(sample);
                }
            }
        }

        return result
            .GroupBy(s => s.Timestamp)
            .Select(g => g.Last())
            .OrderBy(s => s.TimestampValue)
            .ToList();
    }

    private static UsageSample ParseUsageSample(
        Dictionary<string, object> dict,
        string sampleService,
        string sampleWindow,
        DateTimeOffset resetAt)
    {
        var timestamp = JsonUtil.FlexibleDate(JsonUtil.Value(dict, "Timestamp"));
        var used = JsonUtil.Double(dict, "UsedPercent");
        var windowMinutes = JsonUtil.Int(dict, "WindowMinutes") ?? 0;
        if (!timestamp.HasValue || !used.HasValue || windowMinutes <= 0)
        {
            return null;
        }

        return new UsageSample
        {
            Service = sampleService,
            Window = sampleWindow,
            Timestamp = timestamp.Value.ToString("o", CultureInfo.InvariantCulture),
            UsedPercent = Math.Max(0, Math.Min(100, used.Value)),
            ResetAt = resetAt.ToString("o", CultureInfo.InvariantCulture),
            WindowMinutes = windowMinutes
        };
    }

    private static List<UsageHistoryPoint> BuildUsageHistory(List<UsageSample> samples, HistoryAggregation aggregation)
    {
        var bucketLimit = aggregation == HistoryAggregation.Day ? 14 : aggregation == HistoryAggregation.Week ? 8 : 6;
        var cleanedSamples = CleanSamplesForHistory(samples);
        if (cleanedSamples.Count == 0)
        {
            return new List<UsageHistoryPoint>();
        }

        var currentBucket = BucketStart(DateTime.Now, aggregation);
        var observedBuckets = cleanedSamples
            .Select(s => BucketStart(s.TimestampValue.LocalDateTime, aggregation))
            .Where(bucket => bucket <= currentBucket)
            .Distinct()
            .OrderBy(bucket => bucket)
            .ToList();

        if (observedBuckets.Count == 0)
        {
            return new List<UsageHistoryPoint>();
        }

        if (observedBuckets.Count > bucketLimit)
        {
            observedBuckets = observedBuckets.Skip(observedBuckets.Count - bucketLimit).ToList();
        }

        var bucketSet = new HashSet<DateTime>(observedBuckets);
        var buckets = new SortedDictionary<DateTime, double>();
        foreach (var bucket in observedBuckets)
        {
            buckets[bucket] = 0;
        }

        UsageSample previous = null;
        foreach (var current in cleanedSamples)
        {
            if (previous != null)
            {
                var sameWindow = Math.Abs((current.ResetAtValue - previous.ResetAtValue).TotalMinutes) <= 2;
                var delta = current.UsedPercent - previous.UsedPercent;
                if (sameWindow && delta > 0.01)
                {
                    var bucket = BucketStart(current.TimestampValue.LocalDateTime, aggregation);
                    if (bucketSet.Contains(bucket))
                    {
                        buckets[bucket] += delta;
                    }
                }
            }

            previous = current;
        }

        return buckets
            .Select(bucket => new UsageHistoryPoint
            {
                PeriodStart = bucket.Key,
                Label = FormatBucketLabel(bucket.Key, aggregation),
                UsedPercent = Math.Max(0, bucket.Value)
            })
            .ToList();
    }

    private static List<UsageSample> CleanSamplesForHistory(List<UsageSample> samples)
    {
        var cleaned = new List<UsageSample>();
        foreach (var sample in samples)
        {
            if (cleaned.Count > 0 && IsTemporaryHighJump(cleaned.Last(), sample, samples))
            {
                continue;
            }

            cleaned.Add(sample);
        }

        return cleaned;
    }

    private static bool IsTemporaryHighJump(UsageSample previous, UsageSample current, List<UsageSample> allSamples)
    {
        var sameWindow = Math.Abs((current.ResetAtValue - previous.ResetAtValue).TotalMinutes) <= 2;
        var jump = current.UsedPercent - previous.UsedPercent;
        if (!sameWindow || current.UsedPercent < 90 || jump < 40)
        {
            return false;
        }

        foreach (var future in allSamples)
        {
            if (future.TimestampValue <= current.TimestampValue)
            {
                continue;
            }
            if (Math.Abs((future.ResetAtValue - current.ResetAtValue).TotalMinutes) > 2)
            {
                break;
            }
            if (future.UsedPercent <= previous.UsedPercent + 10)
            {
                return true;
            }
        }

        return false;
    }

    private static DateTime BucketStart(DateTime value, HistoryAggregation aggregation)
    {
        var date = value.Date;
        if (aggregation == HistoryAggregation.Day)
        {
            return date;
        }
        if (aggregation == HistoryAggregation.Week)
        {
            var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
            return date.AddDays(-daysSinceMonday);
        }

        return new DateTime(date.Year, date.Month, 1);
    }

    private static string FormatBucketLabel(DateTime value, HistoryAggregation aggregation)
    {
        if (aggregation == HistoryAggregation.Month)
        {
            return value.ToString("yyyy/M", CultureInfo.InvariantCulture);
        }

        return value.ToString("M/d", CultureInfo.InvariantCulture);
    }

    private static List<UsageSample> BuildSamples(QuotaSnapshot snapshot)
    {
        var samples = new List<UsageSample>();
        AddSample(samples, "Codex", "5h", snapshot.Codex.Primary, snapshot.UpdatedAt);
        AddSample(samples, "Codex", "Week", snapshot.Codex.Secondary, snapshot.UpdatedAt);
        AddSample(samples, "Claude", "5h", snapshot.Claude.RealtimeFiveHour, snapshot.UpdatedAt);
        AddSample(samples, "Claude", "7d", snapshot.Claude.RealtimeWeek, snapshot.UpdatedAt);
        return samples;
    }

    private static void AddSample(List<UsageSample> samples, string service, string window, CodexWindow quotaWindow, DateTimeOffset timestamp)
    {
        if (quotaWindow == null ||
            !quotaWindow.UsedPercent.HasValue ||
            !quotaWindow.ResetsAt.HasValue ||
            !quotaWindow.WindowMinutes.HasValue ||
            quotaWindow.WindowMinutes.Value <= 0)
        {
            return;
        }

        samples.Add(new UsageSample
        {
            Service = service,
            Window = window,
            Timestamp = timestamp.ToString("o", CultureInfo.InvariantCulture),
            UsedPercent = Math.Max(0, Math.Min(100, quotaWindow.UsedPercent.Value)),
            ResetAt = quotaWindow.ResetsAt.Value.ToString("o", CultureInfo.InvariantCulture),
            WindowMinutes = quotaWindow.WindowMinutes.Value
        });
    }
}
