using QuotaMonitor.Core.Config;
using QuotaMonitor.Core.Infrastructure;
using QuotaMonitor.Core.Models;

namespace QuotaMonitor.Core.Services;

public sealed class QuotaReader
{
    private readonly IAppPaths _paths;

    public QuotaReader(IAppPaths paths)
    {
        _paths = paths;
    }

    public QuotaSnapshot Read(MonitorConfig config)
    {
        return new QuotaSnapshot
        {
            UpdatedAt = DateTimeOffset.Now,
            Codex = config.CodexVisible ? (config.useRealtimeApi ? ReadCodexRealtimeOrLocal(config) : ReadCodexLocal(config)) : CodexSnapshot.Hidden(),
            Claude = config.ClaudeVisible ? (config.useRealtimeApi ? ReadClaudeRealtimeOrLocal(config) : ReadClaudeLocal(config)) : ClaudeSnapshot.Hidden()
        };
    }

    private CodexSnapshot ReadCodexRealtimeOrLocal(MonitorConfig config)
    {
        var realtime = ReadCodexRealtime(config);
        if (realtime.Available)
        {
            return realtime;
        }

        var local = ReadCodexLocal(config);
        local.FallbackError = realtime.Error;
        return local;
    }

    private ClaudeSnapshot ReadClaudeRealtimeOrLocal(MonitorConfig config)
    {
        var realtime = ReadClaudeRealtime(config);
        if (realtime.Available)
        {
            return realtime;
        }

        var local = ReadClaudeLocal(config);
        local.FallbackError = realtime.Error;
        return local;
    }

    private CodexSnapshot ReadCodexLocal(MonitorConfig config)
    {
        var root = config.ExpandedCodexSessionsPath(_paths);
        if (!Directory.Exists(root))
        {
            return CodexSnapshot.Missing("not found: " + root);
        }

        try
        {
            var files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Take(40)
                .ToList();

            foreach (var file in files)
            {
                var lines = SharedFile.ReadAllLines(file.FullName);
                for (var i = lines.Count - 1; i >= 0; i--)
                {
                    var obj = JsonUtil.ParseObject(lines[i]);
                    if (obj == null || JsonUtil.String(obj, "type") != "event_msg")
                    {
                        continue;
                    }

                    var payload = JsonUtil.Dict(JsonUtil.Value(obj, "payload"));
                    if (payload == null || JsonUtil.String(payload, "type") != "token_count")
                    {
                        continue;
                    }

                    return ParseCodex(payload, obj);
                }
            }

            return CodexSnapshot.Missing("no token_count event");
        }
        catch (Exception ex)
        {
            return CodexSnapshot.Missing(ex.Message);
        }
    }

    private static CodexSnapshot ParseCodex(Dictionary<string, object> payload, Dictionary<string, object> root)
    {
        var rateLimits = JsonUtil.Dict(JsonUtil.Value(payload, "rate_limits"));
        var info = JsonUtil.Dict(JsonUtil.Value(payload, "info"));
        var total = JsonUtil.Dict(JsonUtil.Value(info, "total_token_usage"));
        var last = JsonUtil.Dict(JsonUtil.Value(info, "last_token_usage"));

        return new CodexSnapshot
        {
            Available = true,
            Source = "local token_count",
            Timestamp = JsonUtil.Date(root, "timestamp") ?? DateTimeOffset.Now,
            PlanType = JsonUtil.String(rateLimits, "plan_type") ?? "unknown",
            LimitId = JsonUtil.String(rateLimits, "limit_id") ?? "codex",
            RateLimitReachedType = JsonUtil.String(rateLimits, "rate_limit_reached_type"),
            Primary = ReadCodexWindow(rateLimits, "primary"),
            Secondary = ReadCodexWindow(rateLimits, "secondary"),
            TotalTokens = JsonUtil.Long(total, "total_tokens") ?? 0,
            LastTurnTokens = JsonUtil.Long(last, "total_tokens") ?? 0
        };
    }

    private static CodexWindow ReadCodexWindow(Dictionary<string, object> rateLimits, string name)
    {
        var window = JsonUtil.Dict(JsonUtil.Value(rateLimits, name));
        if (window == null)
        {
            return null;
        }

        var used = JsonUtil.Double(window, "used_percent");
        return new CodexWindow
        {
            UsedPercent = used,
            RemainingPercent = used.HasValue ? Clamp(100.0 - used.Value, 0, 100) : null,
            ResetsAt = JsonUtil.UnixSeconds(window, "resets_at"),
            WindowMinutes = JsonUtil.Int(window, "window_minutes")
        };
    }

    private CodexSnapshot ReadCodexRealtime(MonitorConfig config)
    {
        try
        {
            var authPath = config.ExpandedCodexAuthPath(_paths);
            if (!File.Exists(authPath))
            {
                return CodexSnapshot.Missing("codex auth not found: " + authPath);
            }

            var auth = JsonUtil.ParseObject(File.ReadAllText(authPath));
            var tokens = JsonUtil.Dict(JsonUtil.Value(auth, "tokens"));
            var accessToken = JsonUtil.String(tokens, "access_token");
            var accountId = JsonUtil.String(tokens, "account_id");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return CodexSnapshot.Missing("codex access token missing");
            }

            var headers = new Dictionary<string, string>
            {
                { "Origin", "https://chatgpt.com" },
                { "Referer", "https://chatgpt.com/" }
            };
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                headers["ChatGPT-Account-Id"] = accountId;
            }

            var response = HttpJsonClient.GetJson(
                "https://chatgpt.com/backend-api/wham/usage",
                accessToken,
                headers,
                Math.Max(5, config.realtimeApiTimeoutSeconds) * 1000);
            if (response == null)
            {
                return CodexSnapshot.Missing("codex wham response empty");
            }

            var primary = ParseRealtimeWindow(response, new[] { "primary", "primary_window", "five_hour", "5h" });
            var secondary = ParseRealtimeWindow(response, new[] { "secondary", "secondary_window", "weekly", "week", "seven_day", "7d" });

            if (primary == null && secondary == null)
            {
                return CodexSnapshot.Missing("codex wham usage shape not recognized");
            }

            return new CodexSnapshot
            {
                Available = true,
                Source = "codex wham/usage",
                Timestamp = DateTimeOffset.Now,
                PlanType = FirstString(response, new[] { "plan_type", "planType", "plan" }) ?? "unknown",
                LimitId = "codex",
                Primary = primary,
                Secondary = secondary
            };
        }
        catch (Exception ex)
        {
            return CodexSnapshot.Missing("codex realtime: " + ex.Message);
        }
    }

    private ClaudeSnapshot ReadClaudeRealtime(MonitorConfig config)
    {
        try
        {
            var credentialsPath = config.ExpandedClaudeCredentialsPath(_paths);
            if (!File.Exists(credentialsPath))
            {
                return ClaudeSnapshot.Missing("claude credentials not found: " + credentialsPath);
            }

            var credentials = JsonUtil.ParseObject(File.ReadAllText(credentialsPath));
            var oauth = JsonUtil.Dict(JsonUtil.Value(credentials, "claudeAiOauth"));
            var accessToken = JsonUtil.String(oauth, "accessToken");
            var planType = FirstString(oauth, new[] { "subscriptionType", "subscription_type", "planType", "plan_type", "plan" });
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return ClaudeSnapshot.Missing("claude oauth access token missing");
            }

            var response = HttpJsonClient.GetJson(
                "https://api.anthropic.com/api/oauth/usage",
                accessToken,
                new Dictionary<string, string>
                {
                    { "anthropic-beta", "oauth-2025-04-20" }
                },
                Math.Max(5, config.realtimeApiTimeoutSeconds) * 1000);
            if (response == null)
            {
                return ClaudeSnapshot.Missing("claude oauth usage response empty");
            }

            var fiveHour = ParseRealtimeWindow(response, new[] { "five_hour", "fiveHour", "5h", "primary" });
            var sevenDay = ParseRealtimeWindow(response, new[] { "seven_day", "sevenDay", "7d", "weekly", "week", "secondary" });
            if (fiveHour == null && sevenDay == null)
            {
                return ClaudeSnapshot.Missing("claude oauth usage shape not recognized");
            }

            return new ClaudeSnapshot
            {
                Available = true,
                Source = "claude oauth usage",
                PlanType = planType ?? FirstString(response, new[] { "subscription_type", "subscriptionType", "plan_type", "planType", "plan" }) ?? "unknown",
                WindowMinutes = 300,
                WeekWindowMinutes = 10080,
                RealtimeFiveHour = fiveHour,
                RealtimeWeek = sevenDay
            };
        }
        catch (Exception ex)
        {
            return ClaudeSnapshot.Missing("claude realtime: " + ex.Message);
        }
    }

    private string ReadClaudePlanType(MonitorConfig config)
    {
        try
        {
            var credentialsPath = config.ExpandedClaudeCredentialsPath(_paths);
            if (!File.Exists(credentialsPath))
            {
                return null;
            }

            var credentials = JsonUtil.ParseObject(File.ReadAllText(credentialsPath));
            var oauth = JsonUtil.Dict(JsonUtil.Value(credentials, "claudeAiOauth"));
            return FirstString(oauth, new[] { "subscriptionType", "subscription_type", "planType", "plan_type", "plan", "rateLimitTier" });
        }
        catch
        {
            return null;
        }
    }

    private ClaudeSnapshot ReadClaudeLocal(MonitorConfig config)
    {
        var root = config.ExpandedClaudeProjectsPath(_paths);
        if (!Directory.Exists(root))
        {
            return ClaudeSnapshot.Missing("not found: " + root);
        }

        var now = DateTimeOffset.Now;
        var windowMinutes = Math.Max(1, config.claudeWindowMinutes);
        var weekWindowMinutes = Math.Max(windowMinutes, config.claudeWeekWindowMinutes <= 0 ? 10080 : config.claudeWeekWindowMinutes);
        var threshold = now.AddMinutes(-windowMinutes);
        var weekThreshold = now.AddMinutes(-weekWindowMinutes);
        var fileThreshold = weekThreshold.UtcDateTime.AddHours(-2);
        var result = new ClaudeSnapshot
        {
            Available = true,
            Source = "local jsonl",
            PlanType = ReadClaudePlanType(config) ?? "unknown",
            WindowMinutes = windowMinutes,
            WeekWindowMinutes = weekWindowMinutes,
            MessageBudget = config.claudeFiveHourMessageBudget,
            TokenBudget = config.claudeFiveHourTokenBudget,
            WeeklyMessageBudget = config.claudeWeeklyMessageBudget,
            WeeklyTokenBudget = config.claudeWeeklyTokenBudget
        };

        try
        {
            var files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .Where(info => info.LastWriteTimeUtc >= fileThreshold)
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ToList();

            foreach (var file in files)
            {
                foreach (var line in SharedFile.ReadAllLines(file.FullName))
                {
                    var obj = JsonUtil.ParseObject(line);
                    var message = JsonUtil.Dict(JsonUtil.Value(obj, "message"));
                    var usage = JsonUtil.Dict(JsonUtil.Value(message, "usage"));
                    if (obj == null || message == null || usage == null)
                    {
                        continue;
                    }

                    var timestamp = JsonUtil.Date(obj, "timestamp");
                    if (!timestamp.HasValue ||
                        timestamp.Value < weekThreshold ||
                        timestamp.Value > now.AddMinutes(5))
                    {
                        continue;
                    }

                    var model = JsonUtil.String(message, "model") ?? string.Empty;
                    if (!config.claudeIncludeNonClaudeModels &&
                        !model.StartsWith("claude", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var inputTokens = JsonUtil.Long(usage, "input_tokens") ?? 0;
                    var outputTokens = JsonUtil.Long(usage, "output_tokens") ?? 0;
                    var cacheCreationTokens = JsonUtil.Long(usage, "cache_creation_input_tokens") ?? 0;
                    var cacheReadTokens = JsonUtil.Long(usage, "cache_read_input_tokens") ?? 0;

                    result.WeeklyMessageCount++;
                    result.WeeklyInputTokens += inputTokens;
                    result.WeeklyOutputTokens += outputTokens;
                    result.WeeklyCacheCreationTokens += cacheCreationTokens;
                    result.WeeklyCacheReadTokens += cacheReadTokens;

                    if (timestamp.Value >= threshold)
                    {
                        result.MessageCount++;
                        result.InputTokens += inputTokens;
                        result.OutputTokens += outputTokens;
                        result.CacheCreationTokens += cacheCreationTokens;
                        result.CacheReadTokens += cacheReadTokens;

                        if (!result.OldestCountedAt.HasValue || timestamp.Value < result.OldestCountedAt.Value)
                        {
                            result.OldestCountedAt = timestamp.Value;
                        }
                    }

                    if (model.Length > 0)
                    {
                        result.Models.Add(model);
                    }
                    if (!result.OldestWeeklyCountedAt.HasValue || timestamp.Value < result.OldestWeeklyCountedAt.Value)
                    {
                        result.OldestWeeklyCountedAt = timestamp.Value;
                    }
                }
            }

            var weight = Clamp(config.claudeCacheReadWeight, 0, 1);
            result.WeightedTokens =
                result.InputTokens +
                result.OutputTokens +
                result.CacheCreationTokens +
                (long)Math.Round(result.CacheReadTokens * weight);
            result.WeeklyWeightedTokens =
                result.WeeklyInputTokens +
                result.WeeklyOutputTokens +
                result.WeeklyCacheCreationTokens +
                (long)Math.Round(result.WeeklyCacheReadTokens * weight);

            if (result.OldestCountedAt.HasValue)
            {
                result.EstimatedResetAt = result.OldestCountedAt.Value.AddMinutes(result.WindowMinutes);
            }
            if (result.OldestWeeklyCountedAt.HasValue)
            {
                result.EstimatedWeeklyResetAt = result.OldestWeeklyCountedAt.Value.AddMinutes(result.WeekWindowMinutes);
            }

            return result;
        }
        catch (Exception ex)
        {
            return ClaudeSnapshot.Missing(ex.Message);
        }
    }

    private static CodexWindow ParseRealtimeWindow(Dictionary<string, object> root, string[] candidateNames)
    {
        var window = FindWindowObject(root, candidateNames);
        if (window == null)
        {
            return null;
        }

        var remaining = FirstDouble(window, new[]
        {
            "remaining_percent",
            "remainingPercent",
            "percent_remaining",
            "percentRemaining"
        }, out var remainingField);
        if (remaining.HasValue)
        {
            remaining = NormalizePercent(remainingField, remaining.Value);
        }

        var used = FirstDouble(window, new[]
        {
            "used_percent",
            "usedPercent",
            "utilization",
            "utilisation",
            "usage_percent",
            "usagePercent",
            "used"
        }, out var usedField);
        if (used.HasValue)
        {
            used = NormalizePercent(usedField, used.Value);
        }

        if (!remaining.HasValue && used.HasValue)
        {
            remaining = Clamp(100.0 - used.Value, 0, 100);
        }
        if (!used.HasValue && remaining.HasValue)
        {
            used = Clamp(100.0 - remaining.Value, 0, 100);
        }

        var resetsAt = FirstDate(window, new[]
        {
            "reset_at",
            "resets_at",
            "resetAt",
            "resetsAt",
            "next_reset_at",
            "nextResetAt"
        });
        if (!resetsAt.HasValue)
        {
            var resetAfterSeconds = FirstLong(window, new[]
            {
                "reset_after_seconds",
                "resetAfterSeconds",
                "seconds_until_reset",
                "secondsUntilReset"
            });
            if (resetAfterSeconds.HasValue)
            {
                resetsAt = DateTimeOffset.Now.AddSeconds(resetAfterSeconds.Value);
            }
        }

        var windowMinutes = FirstInt(window, new[]
        {
            "window_minutes",
            "windowMinutes",
            "limit_window_minutes",
            "limitWindowMinutes"
        });
        if (!windowMinutes.HasValue)
        {
            var windowSeconds = FirstLong(window, new[]
            {
                "window_seconds",
                "windowSeconds",
                "limit_window_seconds",
                "limitWindowSeconds"
            });
            if (windowSeconds.HasValue)
            {
                windowMinutes = (int)Math.Round(windowSeconds.Value / 60.0);
            }
        }
        if (!windowMinutes.HasValue)
        {
            var joined = string.Join(" ", candidateNames).ToLowerInvariant();
            if (joined.Contains("five") || joined.Contains("5h") || joined.Contains("primary"))
            {
                windowMinutes = 300;
            }
            else if (joined.Contains("seven") || joined.Contains("7d") || joined.Contains("week") || joined.Contains("secondary"))
            {
                windowMinutes = 10080;
            }
        }

        if (!remaining.HasValue && !used.HasValue && !resetsAt.HasValue)
        {
            return null;
        }

        return new CodexWindow
        {
            UsedPercent = used,
            RemainingPercent = remaining,
            ResetsAt = resetsAt,
            WindowMinutes = windowMinutes
        };
    }

    private static Dictionary<string, object> FindWindowObject(Dictionary<string, object> root, string[] candidateNames)
    {
        if (root == null)
        {
            return null;
        }

        foreach (var candidate in candidateNames)
        {
            var direct = FindObjectByKey(root, candidate);
            if (direct != null)
            {
                return direct;
            }
        }

        return null;
    }

    private static Dictionary<string, object> FindObjectByKey(object value, string key)
    {
        var dict = JsonUtil.Dict(value);
        if (dict != null)
        {
            foreach (var pair in dict)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    var matched = JsonUtil.Dict(pair.Value);
                    if (matched != null)
                    {
                        return matched;
                    }
                }
            }

            foreach (var pair in dict)
            {
                var nested = FindObjectByKey(pair.Value, key);
                if (nested != null)
                {
                    return nested;
                }
            }
        }

        if (value is object[] array)
        {
            foreach (var item in array)
            {
                var nested = FindObjectByKey(item, key);
                if (nested != null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string FirstString(Dictionary<string, object> dict, string[] names)
    {
        foreach (var name in names)
        {
            var value = JsonUtil.String(dict, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static double? FirstDouble(Dictionary<string, object> dict, string[] names, out string matchedName)
    {
        matchedName = null;
        foreach (var name in names)
        {
            var value = JsonUtil.Double(dict, name);
            if (value.HasValue)
            {
                matchedName = name;
                return value;
            }
        }

        return null;
    }

    private static long? FirstLong(Dictionary<string, object> dict, string[] names)
    {
        foreach (var name in names)
        {
            var value = JsonUtil.Long(dict, name);
            if (value.HasValue)
            {
                return value;
            }
        }

        return null;
    }

    private static int? FirstInt(Dictionary<string, object> dict, string[] names)
    {
        foreach (var name in names)
        {
            var value = JsonUtil.Int(dict, name);
            if (value.HasValue)
            {
                return value;
            }
        }

        return null;
    }

    private static DateTimeOffset? FirstDate(Dictionary<string, object> dict, string[] names)
    {
        foreach (var name in names)
        {
            var parsed = JsonUtil.FlexibleDate(JsonUtil.Value(dict, name));
            if (parsed.HasValue)
            {
                return parsed;
            }
        }

        return null;
    }

    private static double NormalizePercent(string fieldName, double value)
    {
        var name = (fieldName ?? string.Empty).ToLowerInvariant();
        if ((name.Contains("ratio") || name.Contains("fraction")) &&
            value >= 0 &&
            value <= 1)
        {
            value *= 100.0;
        }

        return Clamp(value, 0, 100);
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}
