using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class QuotaMonitorApp
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            AppLog.Write("start args=" + string.Join(" ", args));
            NativeMethods.EnableDpiAwareness();
            if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                var config = MonitorConfig.LoadOrCreate();
                var snapshot = QuotaReader.Read(config);
                SelfTest.Write(snapshot);
                AppLog.Write("self-test completed");
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                AppLog.Write("ui exception: " + e.Exception);
                MessageBox.Show(e.Exception.Message, "Quota Monitor error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                AppLog.Write("unhandled exception: " + e.ExceptionObject);
            };

            Application.Run(new MainForm());
            AppLog.Write("exit");
        }
        catch (Exception ex)
        {
            AppLog.Write("fatal: " + ex);
            MessageBox.Show(ex.Message, "Quota Monitor failed to start", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class MonitorConfig
{
    public int pollIntervalSeconds { get; set; }
    public bool alwaysOnTop { get; set; }
    public bool startAtTopRight { get; set; }
    public bool showCodex { get; set; }
    public bool showClaude { get; set; }
    public bool minimizeToTray { get; set; }
    public bool startWithWindows { get; set; }
    public bool compactMode { get; set; }
    public string theme { get; set; }
    public bool alertsEnabled { get; set; }
    public double alertFiveHourRemainingPercent { get; set; }
    public double alertLongWindowRemainingPercent { get; set; }
    public string codexSessionsPath { get; set; }
    public string codexAuthPath { get; set; }
    public string claudeProjectsPath { get; set; }
    public string claudeCredentialsPath { get; set; }
    public bool useRealtimeApi { get; set; }
    public int realtimeApiTimeoutSeconds { get; set; }
    public int claudeWindowMinutes { get; set; }
    public int claudeWeekWindowMinutes { get; set; }
    public int claudeFiveHourMessageBudget { get; set; }
    public long claudeFiveHourTokenBudget { get; set; }
    public int claudeWeeklyMessageBudget { get; set; }
    public long claudeWeeklyTokenBudget { get; set; }
    public double claudeCacheReadWeight { get; set; }
    public bool claudeIncludeNonClaudeModels { get; set; }

    public static string AppDir
    {
        get { return AppDomain.CurrentDomain.BaseDirectory; }
    }

    public static string ConfigPath
    {
        get { return Path.Combine(AppDir, "quota-monitor.config.json"); }
    }

    public static MonitorConfig Default()
    {
        return new MonitorConfig
        {
            pollIntervalSeconds = 300,
            alwaysOnTop = true,
            startAtTopRight = true,
            showCodex = true,
            showClaude = true,
            minimizeToTray = true,
            startWithWindows = false,
            compactMode = false,
            theme = "light",
            alertsEnabled = true,
            alertFiveHourRemainingPercent = 20,
            alertLongWindowRemainingPercent = 30,
            codexSessionsPath = "%USERPROFILE%\\.codex\\sessions",
            codexAuthPath = "%USERPROFILE%\\.codex\\auth.json",
            claudeProjectsPath = "%USERPROFILE%\\.claude\\projects",
            claudeCredentialsPath = "%USERPROFILE%\\.claude\\.credentials.json",
            useRealtimeApi = true,
            realtimeApiTimeoutSeconds = 15,
            claudeWindowMinutes = 300,
            claudeWeekWindowMinutes = 10080,
            claudeFiveHourMessageBudget = 45,
            claudeFiveHourTokenBudget = 0,
            claudeWeeklyMessageBudget = 0,
            claudeWeeklyTokenBudget = 0,
            claudeCacheReadWeight = 0.10,
            claudeIncludeNonClaudeModels = false
        };
    }

    public static MonitorConfig LoadOrCreate()
    {
        var serializer = Json.NewSerializer();
        if (!File.Exists(ConfigPath))
        {
            var created = Default();
            File.WriteAllText(ConfigPath, serializer.Serialize(created));
            return created;
        }

        try
        {
            var raw = File.ReadAllText(ConfigPath);
            var config = serializer.Deserialize<MonitorConfig>(raw);
            if (config == null)
            {
                return Default();
            }

            if (!raw.Contains("\"showCodex\""))
            {
                config.showCodex = true;
            }
            if (!raw.Contains("\"showClaude\""))
            {
                config.showClaude = true;
            }
            if (!raw.Contains("\"minimizeToTray\""))
            {
                config.minimizeToTray = true;
            }
            if (!raw.Contains("\"startWithWindows\""))
            {
                config.startWithWindows = false;
            }
            if (!raw.Contains("\"compactMode\""))
            {
                config.compactMode = false;
            }
            if (!raw.Contains("\"theme\"") || string.IsNullOrWhiteSpace(config.theme))
            {
                config.theme = "light";
            }
            if (!raw.Contains("\"alertsEnabled\""))
            {
                config.alertsEnabled = true;
            }
            if (!raw.Contains("\"alertFiveHourRemainingPercent\""))
            {
                config.alertFiveHourRemainingPercent = 20;
            }
            if (!raw.Contains("\"alertLongWindowRemainingPercent\""))
            {
                config.alertLongWindowRemainingPercent = 30;
            }

            config.pollIntervalSeconds = Math.Max(3, config.pollIntervalSeconds);
            config.realtimeApiTimeoutSeconds = Math.Max(3, config.realtimeApiTimeoutSeconds);
            config.alertFiveHourRemainingPercent = ClampPercent(config.alertFiveHourRemainingPercent);
            config.alertLongWindowRemainingPercent = ClampPercent(config.alertLongWindowRemainingPercent);
            config.theme = NormalizeTheme(config.theme);

            return config;
        }
        catch
        {
            return Default();
        }
    }

    public void Save()
    {
        var serializer = Json.NewSerializer();
        File.WriteAllText(ConfigPath, serializer.Serialize(this));
    }

    [ScriptIgnore]
    public bool CodexVisible
    {
        get { return showCodex; }
    }

    [ScriptIgnore]
    public bool ClaudeVisible
    {
        get { return showClaude; }
    }

    [ScriptIgnore]
    public string ExpandedCodexSessionsPath
    {
        get { return ExpandPath(codexSessionsPath); }
    }

    [ScriptIgnore]
    public string ExpandedCodexAuthPath
    {
        get { return ExpandPath(codexAuthPath); }
    }

    [ScriptIgnore]
    public string ExpandedClaudeProjectsPath
    {
        get { return ExpandPath(claudeProjectsPath); }
    }

    [ScriptIgnore]
    public string ExpandedClaudeCredentialsPath
    {
        get { return ExpandPath(claudeCredentialsPath); }
    }

    private static string ExpandPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path ?? "");
        if (expanded.StartsWith("~\\", StringComparison.Ordinal) ||
            expanded.StartsWith("~/", StringComparison.Ordinal))
        {
            expanded = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                expanded.Substring(2));
        }

        return expanded;
    }

    private static double ClampPercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Max(0, Math.Min(100, value));
    }

    public static string NormalizeTheme(string value)
    {
        if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase))
        {
            return "dark";
        }

        return "light";
    }
}

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuotaMonitor";

    public static void Sync(bool enabled)
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null)
                {
                    return;
                }

                if (enabled)
                {
                    key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"", RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("startup sync error: " + ex.Message);
        }
    }
}

internal sealed class UiTheme
{
    public string Name;
    public Color Background;
    public Color Text;
    public Color MutedText;
    public Color Border;
    public Color Grid;
    public Color Track;
    public Color Accent;
    public Color AccentText;
    public Color Danger;
    public Color Warning;
    public Color ButtonBack;
    public Color ButtonHover;
    public Color ButtonDown;
    public Color PrimarySelectedBack;
    public Color SecondarySelectedBack;
    public Color ChartBar;
}

internal static class UiThemes
{
    public static UiTheme FromConfig(string theme)
    {
        return string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) ? Dark : Light;
    }

    public static readonly UiTheme Light = new UiTheme
    {
        Name = "light",
        Background = Color.FromArgb(248, 249, 250),
        Text = Color.FromArgb(30, 34, 40),
        MutedText = Color.FromArgb(92, 98, 108),
        Border = Color.FromArgb(214, 219, 226),
        Grid = Color.FromArgb(229, 233, 238),
        Track = Color.FromArgb(225, 229, 235),
        Accent = Color.FromArgb(37, 120, 214),
        AccentText = Color.FromArgb(24, 78, 148),
        Danger = Color.FromArgb(209, 66, 57),
        Warning = Color.FromArgb(210, 139, 36),
        ButtonBack = Color.FromArgb(244, 246, 249),
        ButtonHover = Color.FromArgb(236, 240, 245),
        ButtonDown = Color.FromArgb(226, 232, 239),
        PrimarySelectedBack = Color.FromArgb(224, 238, 255),
        SecondarySelectedBack = Color.FromArgb(235, 239, 244),
        ChartBar = Color.FromArgb(92, 151, 224)
    };

    public static readonly UiTheme Dark = new UiTheme
    {
        Name = "dark",
        Background = Color.FromArgb(24, 27, 31),
        Text = Color.FromArgb(236, 239, 243),
        MutedText = Color.FromArgb(165, 173, 184),
        Border = Color.FromArgb(67, 75, 86),
        Grid = Color.FromArgb(45, 51, 60),
        Track = Color.FromArgb(49, 55, 64),
        Accent = Color.FromArgb(88, 166, 255),
        AccentText = Color.FromArgb(168, 205, 255),
        Danger = Color.FromArgb(236, 96, 85),
        Warning = Color.FromArgb(226, 163, 74),
        ButtonBack = Color.FromArgb(34, 39, 46),
        ButtonHover = Color.FromArgb(44, 51, 60),
        ButtonDown = Color.FromArgb(54, 63, 74),
        PrimarySelectedBack = Color.FromArgb(30, 58, 92),
        SecondarySelectedBack = Color.FromArgb(48, 55, 65),
        ChartBar = Color.FromArgb(105, 166, 235)
    };
}

internal static class QuotaReader
{
    public static QuotaSnapshot Read(MonitorConfig config)
    {
        return new QuotaSnapshot
        {
            UpdatedAt = DateTimeOffset.Now,
            Codex = config.CodexVisible ? (config.useRealtimeApi ? ReadCodexRealtimeOrLocal(config) : ReadCodexLocal(config)) : CodexSnapshot.Hidden(),
            Claude = config.ClaudeVisible ? (config.useRealtimeApi ? ReadClaudeRealtimeOrLocal(config) : ReadClaudeLocal(config)) : ClaudeSnapshot.Hidden()
        };
    }

    private static CodexSnapshot ReadCodexRealtimeOrLocal(MonitorConfig config)
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

    private static ClaudeSnapshot ReadClaudeRealtimeOrLocal(MonitorConfig config)
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

    private static CodexSnapshot ReadCodexLocal(MonitorConfig config)
    {
        var root = config.ExpandedCodexSessionsPath;
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
                    var obj = Json.ParseObject(lines[i]);
                    if (obj == null || Json.String(obj, "type") != "event_msg")
                    {
                        continue;
                    }

                    var payload = Json.Dict(Json.Value(obj, "payload"));
                    if (payload == null || Json.String(payload, "type") != "token_count")
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
        var rateLimits = Json.Dict(Json.Value(payload, "rate_limits"));
        var info = Json.Dict(Json.Value(payload, "info"));
        var total = Json.Dict(Json.Value(info, "total_token_usage"));
        var last = Json.Dict(Json.Value(info, "last_token_usage"));

            return new CodexSnapshot
            {
                Available = true,
                Source = "local token_count",
                Timestamp = Json.Date(root, "timestamp") ?? DateTimeOffset.Now,
                PlanType = Json.String(rateLimits, "plan_type") ?? "unknown",
            LimitId = Json.String(rateLimits, "limit_id") ?? "codex",
            RateLimitReachedType = Json.String(rateLimits, "rate_limit_reached_type"),
            Primary = ReadCodexWindow(rateLimits, "primary"),
            Secondary = ReadCodexWindow(rateLimits, "secondary"),
            TotalTokens = Json.Long(total, "total_tokens") ?? 0,
            LastTurnTokens = Json.Long(last, "total_tokens") ?? 0
        };
    }

    private static CodexWindow ReadCodexWindow(Dictionary<string, object> rateLimits, string name)
    {
        var window = Json.Dict(Json.Value(rateLimits, name));
        if (window == null)
        {
            return null;
        }

        var used = Json.Double(window, "used_percent");
        return new CodexWindow
        {
            UsedPercent = used,
            RemainingPercent = used.HasValue ? Clamp(100.0 - used.Value, 0, 100) : (double?)null,
            ResetsAt = Json.UnixSeconds(window, "resets_at"),
            WindowMinutes = Json.Int(window, "window_minutes")
        };
    }

    private static CodexSnapshot ReadCodexRealtime(MonitorConfig config)
    {
        try
        {
            var authPath = config.ExpandedCodexAuthPath;
            if (!File.Exists(authPath))
            {
                return CodexSnapshot.Missing("codex auth not found: " + authPath);
            }

            var auth = Json.ParseObject(File.ReadAllText(authPath));
            var tokens = Json.Dict(Json.Value(auth, "tokens"));
            var accessToken = Json.String(tokens, "access_token");
            var accountId = Json.String(tokens, "account_id");
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

            var response = Http.GetJson(
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

    private static ClaudeSnapshot ReadClaudeRealtime(MonitorConfig config)
    {
        try
        {
            var credentialsPath = config.ExpandedClaudeCredentialsPath;
            if (!File.Exists(credentialsPath))
            {
                return ClaudeSnapshot.Missing("claude credentials not found: " + credentialsPath);
            }

            var credentials = Json.ParseObject(File.ReadAllText(credentialsPath));
            var oauth = Json.Dict(Json.Value(credentials, "claudeAiOauth"));
            var accessToken = Json.String(oauth, "accessToken");
            var planType = FirstString(oauth, new[] { "subscriptionType", "subscription_type", "planType", "plan_type", "plan" });
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return ClaudeSnapshot.Missing("claude oauth access token missing");
            }

            var response = Http.GetJson(
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

    private static string ReadClaudePlanType(MonitorConfig config)
    {
        try
        {
            var credentialsPath = config.ExpandedClaudeCredentialsPath;
            if (!File.Exists(credentialsPath))
            {
                return null;
            }

            var credentials = Json.ParseObject(File.ReadAllText(credentialsPath));
            var oauth = Json.Dict(Json.Value(credentials, "claudeAiOauth"));
            return FirstString(oauth, new[] { "subscriptionType", "subscription_type", "planType", "plan_type", "plan", "rateLimitTier" });
        }
        catch
        {
            return null;
        }
    }

    private static CodexWindow ParseRealtimeWindow(Dictionary<string, object> root, string[] candidateNames)
    {
        var window = FindWindowObject(root, candidateNames);
        if (window == null)
        {
            return null;
        }

        string remainingField;
        var remaining = FirstDouble(window, new[]
        {
            "remaining_percent",
            "remainingPercent",
            "percent_remaining",
            "percentRemaining"
        }, out remainingField);
        if (remaining.HasValue)
        {
            remaining = NormalizePercent(remainingField, remaining.Value);
        }

        string usedField;
        var used = FirstDouble(window, new[]
        {
            "used_percent",
            "usedPercent",
            "utilization",
            "utilisation",
            "usage_percent",
            "usagePercent",
            "used"
        }, out usedField);
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
        var dict = Json.Dict(value);
        if (dict != null)
        {
            foreach (var pair in dict)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    var matched = Json.Dict(pair.Value);
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

        var array = value as object[];
        if (array != null)
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
            var value = Json.String(dict, name);
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
            var value = Json.Double(dict, name);
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
            var value = Json.Long(dict, name);
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
            var value = Json.Int(dict, name);
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
            var raw = Json.Value(dict, name);
            var parsed = Json.FlexibleDate(raw);
            if (parsed.HasValue)
            {
                return parsed;
            }
        }

        return null;
    }

    private static double NormalizePercent(string fieldName, double value)
    {
        var name = (fieldName ?? "").ToLowerInvariant();
        if ((name.Contains("ratio") || name.Contains("fraction")) &&
            value >= 0 &&
            value <= 1)
        {
            value *= 100.0;
        }

        return Clamp(value, 0, 100);
    }

    private static ClaudeSnapshot ReadClaudeLocal(MonitorConfig config)
    {
        var root = config.ExpandedClaudeProjectsPath;
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
                    var obj = Json.ParseObject(line);
                    var message = Json.Dict(Json.Value(obj, "message"));
                    var usage = Json.Dict(Json.Value(message, "usage"));
                    if (obj == null || message == null || usage == null)
                    {
                        continue;
                    }

                    var timestamp = Json.Date(obj, "timestamp");
                    if (!timestamp.HasValue ||
                        timestamp.Value < weekThreshold ||
                        timestamp.Value > now.AddMinutes(5))
                    {
                        continue;
                    }

                    var model = Json.String(message, "model") ?? "";
                    if (!config.claudeIncludeNonClaudeModels &&
                        !model.StartsWith("claude", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var inputTokens = Json.Long(usage, "input_tokens") ?? 0;
                    var outputTokens = Json.Long(usage, "output_tokens") ?? 0;
                    var cacheCreationTokens = Json.Long(usage, "cache_creation_input_tokens") ?? 0;
                    var cacheReadTokens = Json.Long(usage, "cache_read_input_tokens") ?? 0;

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

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}

internal sealed class ServiceHeaderControl : Control
{
    private readonly string _serviceName;
    private string _planText = "Plan: --";
    private bool _compactMode;
    private UiTheme _theme = UiThemes.Light;

    public ServiceHeaderControl(string serviceName)
    {
        _serviceName = serviceName ?? "";
        DoubleBuffered = true;
        BackColor = _theme.Background;
        Dock = DockStyle.Fill;
        Margin = new Padding(0);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        MinimumSize = new Size(UiScale.Scale(120), PreferredControlHeight);
    }

    public int PreferredControlHeight
    {
        get
        {
            using (var titleFont = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point))
            using (var planFont = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point))
            {
                if (_compactMode)
                {
                    return Math.Max(30, UiScale.LineHeight(titleFont) + UiScale.Scale(2));
                }

                return Math.Max(
                    44,
                    UiScale.LineHeight(titleFont) + UiScale.LineHeight(planFont) + UiScale.Scale(4));
            }
        }
    }

    public void SetCompactMode(bool compactMode)
    {
        if (_compactMode == compactMode)
        {
            return;
        }

        _compactMode = compactMode;
        MinimumSize = new Size(UiScale.Scale(120), PreferredControlHeight);
        Invalidate();
    }

    public void SetPlan(string planText)
    {
        _planText = string.IsNullOrWhiteSpace(planText) ? "Plan: unknown" : planText;
        Invalidate();
    }

    public void SetTheme(UiTheme theme)
    {
        _theme = theme ?? UiThemes.Light;
        BackColor = _theme.Background;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);

        using (var titleFont = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point))
        using (var planFont = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point))
        {
            var titleHeight = UiScale.LineHeight(titleFont);
            if (_compactMode)
            {
                var titleWidth = Math.Min(
                    UiScale.TextWidth(_serviceName, titleFont) + UiScale.Scale(12),
                    Math.Max(1, Width / 2));
                TextRenderer.DrawText(
                    g,
                    _serviceName,
                    titleFont,
                    new Rectangle(0, 0, titleWidth, Height),
                    _theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(
                    g,
                    _planText,
                    planFont,
                    new Rectangle(titleWidth, 0, Math.Max(1, Width - titleWidth), Height),
                    _theme.MutedText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                return;
            }

            TextRenderer.DrawText(
                g,
                _serviceName,
                titleFont,
                new Rectangle(0, 0, Width, titleHeight),
                _theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            var planTop = titleHeight + UiScale.Scale(2);
            TextRenderer.DrawText(
                g,
                _planText,
                planFont,
                new Rectangle(0, planTop, Width, Math.Max(1, Height - planTop)),
                _theme.MutedText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}

internal sealed class QuotaBarControl : Control
{
    private string _title = "";
    private string _detail = "";
    private double? _remainingPercent;
    private bool _compactMode;
    private UiTheme _theme = UiThemes.Light;

    public QuotaBarControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = _theme.Background;
        Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        MinimumSize = new Size(UiScale.Scale(260), PreferredControlHeight);
    }

    public int PreferredControlHeight
    {
        get
        {
            using (var titleFont = new Font(Font, FontStyle.Bold))
            {
                if (_compactMode)
                {
                    return Math.Max(
                        38,
                        UiScale.Scale(2) +
                        UiScale.LineHeight(titleFont) +
                        UiScale.Scale(3) +
                        UiScale.Scale(8) +
                        UiScale.Scale(3));
                }

                return Math.Max(
                    72,
                    UiScale.Scale(2) +
                    UiScale.LineHeight(titleFont) +
                    UiScale.Scale(3) +
                    UiScale.LineHeight(Font) +
                    UiScale.Scale(6) +
                    UiScale.Scale(10) +
                    UiScale.Scale(4));
            }
        }
    }

    public void SetCompactMode(bool compactMode)
    {
        if (_compactMode == compactMode)
        {
            return;
        }

        _compactMode = compactMode;
        MinimumSize = new Size(UiScale.Scale(220), PreferredControlHeight);
        Invalidate();
    }

    public void SetTheme(UiTheme theme)
    {
        _theme = theme ?? UiThemes.Light;
        BackColor = _theme.Background;
        Invalidate();
    }

    public void SetData(string title, string detail, double? remainingPercent)
    {
        _title = title ?? "";
        _detail = detail ?? "";
        _remainingPercent = remainingPercent.HasValue
            ? Math.Max(0, Math.Min(100, remainingPercent.Value))
            : (double?)null;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);

        var titleColor = PickTextColor(_remainingPercent, _theme);
        var detailColor = _theme.MutedText;
        var trackColor = _theme.Track;
        var fillColor = PickFillColor(_remainingPercent, _theme);
        var borderColor = _theme.Border;

        if (_compactMode)
        {
            PaintCompact(g, titleColor, detailColor, trackColor, fillColor, borderColor);
            return;
        }

        var barHeight = UiScale.Scale(10);
        var barBottomPadding = UiScale.Scale(4);
        var barTop = Math.Max(0, Height - barBottomPadding - barHeight);
        var textBottom = Math.Max(0, barTop - UiScale.Scale(6));
        var topPadding = UiScale.Scale(2);
        using (var titleFont = new Font(Font, FontStyle.Bold))
        {
            var titleHeight = Math.Min(UiScale.LineHeight(titleFont), Math.Max(0, textBottom - topPadding));
            var titleBounds = new Rectangle(0, topPadding, Width, titleHeight);
            TextRenderer.DrawText(
                g,
                _title,
                titleFont,
                titleBounds,
                titleColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            var detailTop = titleBounds.Bottom + UiScale.Scale(3);
            var detailHeight = Math.Min(UiScale.LineHeight(Font), Math.Max(0, textBottom - detailTop));
            if (detailHeight > 0)
            {
                var detailBounds = new Rectangle(0, detailTop, Width, detailHeight);
                TextRenderer.DrawText(
                    g,
                    _detail,
                    Font,
                    detailBounds,
                    detailColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        var barRect = new Rectangle(0, barTop, Math.Max(1, Width - 1), Math.Max(1, barHeight));
        using (var trackBrush = new SolidBrush(trackColor))
        using (var fillBrush = new SolidBrush(fillColor))
        using (var borderPen = new Pen(borderColor))
        using (var trackPath = RoundedRect(barRect, UiScale.Scale(4)))
        {
            g.FillPath(trackBrush, trackPath);
            if (_remainingPercent.HasValue)
            {
                var fillWidth = (int)Math.Round(barRect.Width * _remainingPercent.Value / 100.0);
                if (fillWidth > 0)
                {
                    var state = g.Save();
                    try
                    {
                        g.SetClip(trackPath);
                        var fillRect = new Rectangle(barRect.X, barRect.Y, Math.Min(fillWidth, barRect.Width), barRect.Height);
                        using (var fillPath = RoundedRect(fillRect, UiScale.Scale(4)))
                        {
                            g.FillPath(fillBrush, fillPath);
                        }
                    }
                    finally
                    {
                        g.Restore(state);
                    }
                }
            }

            g.DrawPath(borderPen, trackPath);
        }
    }

    private void PaintCompact(Graphics g, Color titleColor, Color detailColor, Color trackColor, Color fillColor, Color borderColor)
    {
        var barHeight = UiScale.Scale(8);
        var topPadding = UiScale.Scale(1);
        using (var titleFont = new Font(Font, FontStyle.Bold))
        {
            var lineHeight = UiScale.LineHeight(titleFont);
            var titleWidth = Math.Min(
                Math.Max(UiScale.TextWidth(_title, titleFont) + UiScale.Scale(8), UiScale.Scale(80)),
                Math.Max(1, Width / 2));
            var titleBounds = new Rectangle(0, topPadding, titleWidth, lineHeight);
            TextRenderer.DrawText(
                g,
                _title,
                titleFont,
                titleBounds,
                titleColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            var detailBounds = new Rectangle(
                titleBounds.Right,
                topPadding,
                Math.Max(1, Width - titleBounds.Right),
                lineHeight);
            TextRenderer.DrawText(
                g,
                _detail,
                Font,
                detailBounds,
                detailColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            var barTop = Math.Min(Height - barHeight - UiScale.Scale(2), titleBounds.Bottom + UiScale.Scale(2));
            var barRect = new Rectangle(0, Math.Max(0, barTop), Math.Max(1, Width - 1), Math.Max(1, barHeight));
            using (var trackBrush = new SolidBrush(trackColor))
            using (var fillBrush = new SolidBrush(fillColor))
            using (var borderPen = new Pen(borderColor))
            using (var trackPath = RoundedRect(barRect, UiScale.Scale(4)))
            {
                g.FillPath(trackBrush, trackPath);
                if (_remainingPercent.HasValue)
                {
                    var fillWidth = (int)Math.Round(barRect.Width * _remainingPercent.Value / 100.0);
                    if (fillWidth > 0)
                    {
                        var state = g.Save();
                        try
                        {
                            g.SetClip(trackPath);
                            var fillRect = new Rectangle(barRect.X, barRect.Y, Math.Min(fillWidth, barRect.Width), barRect.Height);
                            using (var fillPath = RoundedRect(fillRect, UiScale.Scale(4)))
                            {
                                g.FillPath(fillBrush, fillPath);
                            }
                        }
                        finally
                        {
                            g.Restore(state);
                        }
                    }
                }

                g.DrawPath(borderPen, trackPath);
            }
        }
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        if (diameter <= 0 || rect.Width <= diameter || rect.Height <= diameter)
        {
            path.AddRectangle(rect);
            path.CloseFigure();
            return path;
        }

        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color PickFillColor(double? remainingPercent, UiTheme theme)
    {
        if (!remainingPercent.HasValue)
        {
            return theme.MutedText;
        }
        if (remainingPercent.Value <= 10)
        {
            return theme.Danger;
        }
        if (remainingPercent.Value <= 25)
        {
            return theme.Warning;
        }

        return theme.Accent;
    }

    private static Color PickTextColor(double? remainingPercent, UiTheme theme)
    {
        if (!remainingPercent.HasValue)
        {
            return theme.Text;
        }
        if (remainingPercent.Value <= 10)
        {
            return theme.Danger;
        }
        if (remainingPercent.Value <= 25)
        {
            return theme.Warning;
        }

        return theme.AccentText;
    }
}

internal static class AppLog
{
    private static readonly object Gate = new object();

    public static string LogPath
    {
        get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "quota-monitor.log"); }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(
                    LogPath,
                    DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never prevent the monitor from starting.
        }
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    public static void EnableDpiAwareness()
    {
        try
        {
            SetProcessDPIAware();
        }
        catch
        {
        }
    }
}

internal static class UiScale
{
    private static readonly float DpiScale = DetectDpiScale();

    public static float Factor
    {
        get { return DpiScale; }
    }

    public static int Scale(int value)
    {
        if (value == 0)
        {
            return 0;
        }

        if (value < 0)
        {
            return -Scale(-value);
        }

        return Math.Max(1, (int)Math.Round(value * DpiScale));
    }

    public static float Scale(float value)
    {
        return value * DpiScale;
    }

    public static Size Size(int width, int height)
    {
        return new Size(Scale(width), Scale(height));
    }

    public static Padding Padding(int all)
    {
        return new Padding(Scale(all));
    }

    public static Padding Padding(int left, int top, int right, int bottom)
    {
        return new Padding(Scale(left), Scale(top), Scale(right), Scale(bottom));
    }

    public static int LineHeight(Font font)
    {
        var measured = TextRenderer.MeasureText(
            "Mg",
            font,
            new Size(1000, 1000),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        return Math.Max(1, measured.Height + Scale(2));
    }

    public static int TextWidth(string text, Font font)
    {
        var measured = TextRenderer.MeasureText(
            string.IsNullOrEmpty(text) ? "Mg" : text,
            font,
            new Size(1000, 1000),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        return Math.Max(1, measured.Width);
    }

    public static Size FitToWorkingArea(Size requested, int horizontalMargin, int verticalMargin)
    {
        try
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            var maxWidth = Math.Max(360, area.Width - Scale(horizontalMargin));
            var maxHeight = Math.Max(220, area.Height - Scale(verticalMargin));
            return new Size(Math.Min(requested.Width, maxWidth), Math.Min(requested.Height, maxHeight));
        }
        catch
        {
            return requested;
        }
    }

    private static float DetectDpiScale()
    {
        try
        {
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                return Math.Max(1.0F, graphics.DpiX / 96.0F);
            }
        }
        catch
        {
            return 1.0F;
        }
    }
}

internal sealed class UsageSample
{
    public string Service { get; set; }
    public string Window { get; set; }
    public string Timestamp { get; set; }
    public double UsedPercent { get; set; }
    public string ResetAt { get; set; }
    public int WindowMinutes { get; set; }

    [ScriptIgnore]
    public DateTimeOffset TimestampValue
    {
        get { return DateTimeOffset.Parse(Timestamp, CultureInfo.InvariantCulture); }
    }

    [ScriptIgnore]
    public DateTimeOffset ResetAtValue
    {
        get { return DateTimeOffset.Parse(ResetAt, CultureInfo.InvariantCulture); }
    }
}

internal enum ChartMode
{
    Pace,
    History
}

internal enum HistoryAggregation
{
    Day,
    Week,
    Month
}

internal sealed class UsageHistoryPoint
{
    public DateTime PeriodStart;
    public string Label;
    public double UsedPercent;
}

internal static class UsageHistoryStore
{
    private static readonly object Gate = new object();

    public static string HistoryPath
    {
        get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "quota-monitor-history.jsonl"); }
    }

    public static void AppendSnapshot(QuotaSnapshot snapshot)
    {
        var samples = BuildSamples(snapshot);
        if (samples.Count == 0)
        {
            return;
        }

        var serializer = Json.NewSerializer();
        lock (Gate)
        {
            using (var writer = new StreamWriter(HistoryPath, true))
            {
                foreach (var sample in samples)
                {
                    writer.WriteLine(serializer.Serialize(sample));
                }
            }
        }
    }

    public static List<UsageSample> Load(string service, string window, DateTimeOffset resetAt)
    {
        var result = new List<UsageSample>();
        if (!File.Exists(HistoryPath))
        {
            return result;
        }

        lock (Gate)
        {
            foreach (var line in File.ReadLines(HistoryPath).Reverse().Take(2000).Reverse())
            {
                var dict = Json.ParseObject(line);
                if (dict == null)
                {
                    continue;
                }

                var sampleService = Json.String(dict, "Service");
                var sampleWindow = Json.String(dict, "Window");
                if (!string.Equals(sampleService, service, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(sampleWindow, window, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sampleReset = Json.FlexibleDate(Json.Value(dict, "ResetAt"));
                if (!sampleReset.HasValue || Math.Abs((sampleReset.Value - resetAt).TotalMinutes) > 2)
                {
                    continue;
                }

                var timestamp = Json.FlexibleDate(Json.Value(dict, "Timestamp"));
                var used = Json.Double(dict, "UsedPercent");
                var windowMinutes = Json.Int(dict, "WindowMinutes") ?? 0;
                if (!timestamp.HasValue || !used.HasValue || windowMinutes <= 0)
                {
                    continue;
                }

                result.Add(new UsageSample
                {
                    Service = sampleService,
                    Window = sampleWindow,
                    Timestamp = timestamp.Value.ToString("o", CultureInfo.InvariantCulture),
                    UsedPercent = Math.Max(0, Math.Min(100, used.Value)),
                    ResetAt = sampleReset.Value.ToString("o", CultureInfo.InvariantCulture),
                    WindowMinutes = windowMinutes
                });
            }
        }

        return result
            .GroupBy(s => s.Timestamp)
            .Select(g => g.Last())
            .OrderBy(s => s.TimestampValue)
            .ToList();
    }

    public static List<UsageHistoryPoint> LoadUsageHistory(string service, string window, HistoryAggregation aggregation)
    {
        return BuildUsageHistory(LoadAll(service, window), aggregation);
    }

    private static List<UsageSample> LoadAll(string service, string window)
    {
        var result = new List<UsageSample>();
        if (!File.Exists(HistoryPath))
        {
            return result;
        }

        lock (Gate)
        {
            foreach (var line in File.ReadLines(HistoryPath).Reverse().Take(50000).Reverse())
            {
                var dict = Json.ParseObject(line);
                if (dict == null)
                {
                    continue;
                }

                var sampleService = Json.String(dict, "Service");
                var sampleWindow = Json.String(dict, "Window");
                if (!string.Equals(sampleService, service, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(sampleWindow, window, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var timestamp = Json.FlexibleDate(Json.Value(dict, "Timestamp"));
                var resetAt = Json.FlexibleDate(Json.Value(dict, "ResetAt"));
                var used = Json.Double(dict, "UsedPercent");
                var windowMinutes = Json.Int(dict, "WindowMinutes") ?? 0;
                if (!timestamp.HasValue || !resetAt.HasValue || !used.HasValue || windowMinutes <= 0)
                {
                    continue;
                }

                result.Add(new UsageSample
                {
                    Service = sampleService,
                    Window = sampleWindow,
                    Timestamp = timestamp.Value.ToString("o", CultureInfo.InvariantCulture),
                    UsedPercent = Math.Max(0, Math.Min(100, used.Value)),
                    ResetAt = resetAt.Value.ToString("o", CultureInfo.InvariantCulture),
                    WindowMinutes = windowMinutes
                });
            }
        }

        return result
            .GroupBy(s => s.Timestamp)
            .Select(g => g.Last())
            .OrderBy(s => s.TimestampValue)
            .ToList();
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
        for (var i = 0; i < cleanedSamples.Count; i++)
        {
            var current = cleanedSamples[i];
            if (previous != null)
            {
                var sameWindow = Math.Abs((current.ResetAtValue - previous.ResetAtValue).TotalMinutes) <= 2;
                var delta = current.UsedPercent - previous.UsedPercent;
                if (sameWindow && delta > 0.01)
                {
                    var bucket = BucketStart(current.TimestampValue.LocalDateTime, aggregation);
                    if (bucketSet.Contains(bucket))
                    {
                        buckets[bucket] = buckets[bucket] + delta;
                    }
                }
            }

            previous = current;
        }

        var result = new List<UsageHistoryPoint>();
        foreach (var bucket in buckets)
        {
            result.Add(new UsageHistoryPoint
            {
                PeriodStart = bucket.Key,
                Label = FormatBucketLabel(bucket.Key, aggregation),
                UsedPercent = Math.Max(0, bucket.Value)
            });
        }

        return result;
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

internal sealed class UsagePaceChartControl : Control
{
    private string _title = "Usage pace";
    private List<UsageSample> _samples = new List<UsageSample>();
    private DateTimeOffset? _resetAt;
    private int _windowMinutes;
    private double? _currentUsedPercent;
    private UiTheme _theme = UiThemes.Light;

    public UsagePaceChartControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = _theme.Background;
        Dock = DockStyle.Fill;
        Margin = UiScale.Padding(0, 4, 0, 2);
        MinimumSize = new Size(260, 150);
        Font = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
    }

    public void SetData(string title, CodexWindow window, List<UsageSample> samples)
    {
        _title = title;
        _samples = samples ?? new List<UsageSample>();
        _resetAt = window == null ? null : window.ResetsAt;
        _windowMinutes = window != null && window.WindowMinutes.HasValue ? window.WindowMinutes.Value : 0;
        _currentUsedPercent = window == null ? null : window.UsedPercent;
        if (_currentUsedPercent.HasValue)
        {
            var maxReasonableUsed = Math.Min(100, _currentUsedPercent.Value + 5);
            _samples = _samples
                .Where(s => s.UsedPercent <= maxReasonableUsed)
                .ToList();
        }

        Invalidate();
    }

    public void SetTheme(UiTheme theme)
    {
        _theme = theme ?? UiThemes.Light;
        BackColor = _theme.Background;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);

        var plot = CalculatePlotRectangle();
        var borderColor = _theme.Border;
        var gridColor = _theme.Grid;
        var idealColor = _theme.MutedText;
        var actualColor = _theme.Accent;
        var axisColor = _theme.MutedText;

        using (var titleFont = new Font(Font, FontStyle.Bold))
        {
            var titleHeight = UiScale.LineHeight(titleFont);
            TextRenderer.DrawText(
                g,
                BuildTitle(),
                titleFont,
                new Rectangle(0, 0, Math.Max(1, Width - UiScale.Scale(8)), titleHeight),
                _theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        var predictionTop = UiScale.LineHeight(Font);
        TextRenderer.DrawText(
            g,
            BuildPredictionText(),
            Font,
            new Rectangle(0, predictionTop, Math.Max(1, Width - UiScale.Scale(8)), UiScale.LineHeight(Font)),
            axisColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (!_resetAt.HasValue || _windowMinutes <= 0)
        {
            TextRenderer.DrawText(
                g,
                "No pace data yet",
                Font,
                plot,
                _theme.MutedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        if (plot.Width < UiScale.Scale(24) || plot.Height < UiScale.Scale(24))
        {
            return;
        }

        using (var gridPen = new Pen(gridColor))
        using (var borderPen = new Pen(borderColor))
        {
            g.DrawLine(gridPen, plot.Left, plot.Top + plot.Height / 2, plot.Right, plot.Top + plot.Height / 2);
            g.DrawRectangle(borderPen, plot);
        }

        var now = DateTimeOffset.Now;
        var windowStart = _resetAt.Value.AddMinutes(-_windowMinutes);
        var nowX = Clamp01((now - windowStart).TotalMinutes / _windowMinutes);
        DrawAxes(g, plot, windowStart, _resetAt.Value, axisColor, gridColor);

        using (var idealPen = new Pen(idealColor, UiScale.Scale(1.5F)))
        {
            idealPen.DashStyle = DashStyle.Dash;
            g.DrawLine(idealPen, PointFor(plot, 0, 0), PointFor(plot, 1, 100));
        }

        var points = new List<PointF> { PointFor(plot, 0, 0) };
        foreach (var sample in _samples)
        {
            var x = Clamp01((sample.TimestampValue - windowStart).TotalMinutes / _windowMinutes);
            points.Add(PointFor(plot, x, sample.UsedPercent));
        }
        if (_currentUsedPercent.HasValue)
        {
            points.Add(PointFor(plot, nowX, _currentUsedPercent.Value));
        }

        points = points
            .GroupBy(p => Math.Round(p.X, 1))
            .Select(gp => gp.Last())
            .OrderBy(p => p.X)
            .ToList();

        if (points.Count >= 2)
        {
            using (var actualPen = new Pen(actualColor, UiScale.Scale(2.0F)))
            {
                g.DrawLines(actualPen, points.ToArray());
            }
        }
        var pointRadius = UiScale.Scale(2);
        foreach (var point in points.Take(Math.Max(0, points.Count - 12)).Concat(points.Skip(Math.Max(0, points.Count - 12))))
        {
            using (var brush = new SolidBrush(actualColor))
            {
                g.FillEllipse(brush, point.X - pointRadius, point.Y - pointRadius, pointRadius * 2, pointRadius * 2);
            }
        }

        using (var nowPen = new Pen(_theme.Border, UiScale.Scale(1F)))
        {
            g.DrawLine(nowPen, plot.Left + (float)(plot.Width * nowX), plot.Top, plot.Left + (float)(plot.Width * nowX), plot.Bottom);
        }

        var idealLabelWidth = UiScale.TextWidth("ideal", Font) + UiScale.Scale(8);
        var idealLabelHeight = UiScale.LineHeight(Font);
        var idealLabel = new Rectangle(
            plot.Right - idealLabelWidth - UiScale.Scale(6),
            plot.Top + UiScale.Scale(8),
            idealLabelWidth,
            idealLabelHeight);
        using (var labelBack = new SolidBrush(BackColor))
        {
            g.FillRectangle(labelBack, idealLabel);
        }

        TextRenderer.DrawText(
            g,
            "ideal",
            Font,
            idealLabel,
            idealColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping);
    }

    private Rectangle CalculatePlotRectangle()
    {
        using (var titleFont = new Font(Font, FontStyle.Bold))
        using (var labelFont = new Font(Font.FontFamily, 7.5F, FontStyle.Regular, GraphicsUnit.Point))
        {
            var leftMargin = Math.Max(UiScale.Scale(50), UiScale.TextWidth("100%", labelFont) + UiScale.Scale(10));
            var topMargin = UiScale.LineHeight(titleFont) + UiScale.LineHeight(Font) + UiScale.Scale(12);
            var rightMargin = UiScale.Scale(10);
            var bottomMargin = UiScale.LineHeight(labelFont) + UiScale.Scale(12);

            var available = new Rectangle(
                leftMargin,
                topMargin,
                Math.Max(1, Width - leftMargin - rightMargin),
                Math.Max(1, Height - topMargin - bottomMargin));

            var plotWidth = available.Width;
            var plotHeight = available.Height;
            var aspect = plotWidth / (double)plotHeight;
            const double minAspect = 1.25;
            const double maxAspect = 2.45;

            if (aspect < minAspect)
            {
                plotHeight = Math.Max(UiScale.Scale(24), (int)Math.Round(plotWidth / minAspect));
            }
            else if (aspect > maxAspect)
            {
                plotWidth = Math.Max(UiScale.Scale(24), (int)Math.Round(plotHeight * maxAspect));
            }

            plotWidth = Math.Min(plotWidth, available.Width);
            plotHeight = Math.Min(plotHeight, available.Height);

            return new Rectangle(
                available.Left + (available.Width - plotWidth) / 2,
                available.Top + (available.Height - plotHeight) / 3,
                plotWidth,
                plotHeight);
        }
    }

    private void DrawAxes(Graphics g, Rectangle plot, DateTimeOffset windowStart, DateTimeOffset resetAt, Color axisColor, Color gridColor)
    {
        using (var labelFont = new Font(Font.FontFamily, 7.5F, FontStyle.Regular, GraphicsUnit.Point))
        using (var tickPen = new Pen(gridColor))
        {
            DrawYLabel(g, labelFont, plot, 100, axisColor, tickPen);
            DrawYLabel(g, labelFont, plot, 50, axisColor, tickPen);
            DrawYLabel(g, labelFont, plot, 0, axisColor, tickPen);

            var labelTop = plot.Bottom + UiScale.Scale(4);
            var labelWidth = Math.Max(UiScale.Scale(92), UiScale.TextWidth("00/00 00:00", labelFont) + UiScale.Scale(8));
            var labelHeight = UiScale.LineHeight(labelFont);
            var windowMiddle = windowStart.AddTicks((resetAt - windowStart).Ticks / 2);
            var startText = FormatAxisTime(windowStart);
            var middleText = FormatAxisTime(windowMiddle);
            var resetText = FormatAxisTime(resetAt);

            TextRenderer.DrawText(
                g,
                startText,
                labelFont,
                new Rectangle(plot.Left - UiScale.Scale(2), labelTop, labelWidth, labelHeight),
                axisColor,
                TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            if (plot.Width >= labelWidth * 3)
            {
                g.DrawLine(tickPen, plot.Left + plot.Width / 2, plot.Bottom, plot.Left + plot.Width / 2, plot.Bottom + UiScale.Scale(4));
                TextRenderer.DrawText(
                    g,
                    middleText,
                    labelFont,
                    new Rectangle(plot.Left + plot.Width / 2 - labelWidth / 2, labelTop, labelWidth, labelHeight),
                    axisColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }

            TextRenderer.DrawText(
                g,
                resetText,
                labelFont,
                new Rectangle(plot.Right - labelWidth, labelTop, labelWidth, labelHeight),
                axisColor,
                TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    private static void DrawYLabel(Graphics g, Font labelFont, Rectangle plot, int percent, Color axisColor, Pen tickPen)
    {
        var y = plot.Bottom - (int)Math.Round(plot.Height * percent / 100.0);
        g.DrawLine(tickPen, plot.Left - UiScale.Scale(4), y, plot.Left, y);
        var labelHeight = UiScale.LineHeight(labelFont);
        TextRenderer.DrawText(
            g,
            percent.ToString(CultureInfo.InvariantCulture) + "%",
            labelFont,
            new Rectangle(0, y - labelHeight / 2, Math.Max(1, plot.Left - UiScale.Scale(9)), labelHeight),
            axisColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping);
    }

    private static string FormatAxisTime(DateTimeOffset value)
    {
        return value.LocalDateTime.ToString("M/d HH:mm", CultureInfo.InvariantCulture);
    }

    private string BuildTitle()
    {
        if (!_resetAt.HasValue || !_currentUsedPercent.HasValue || _windowMinutes <= 0)
        {
            return _title;
        }

        var start = _resetAt.Value.AddMinutes(-_windowMinutes);
        var elapsed = Math.Max(0, Math.Min(_windowMinutes, (DateTimeOffset.Now - start).TotalMinutes));
        var idealUsed = elapsed * 100.0 / _windowMinutes;
        var delta = _currentUsedPercent.Value - idealUsed;
        var deltaText = (delta >= 0 ? "+" : "-") + Math.Abs(delta).ToString("0", CultureInfo.InvariantCulture) + "pp";
        string pace;
        if (Math.Abs(delta) < 2)
        {
            pace = "on pace (" + deltaText + ")";
        }
        else if (idealUsed >= 1)
        {
            var ratio = _currentUsedPercent.Value / idealUsed;
            pace = (delta > 0 ? "fast " : "slow ") + ratio.ToString("0.0", CultureInfo.InvariantCulture) + "x (" + deltaText + ")";
        }
        else
        {
            pace = (delta > 0 ? "ahead " : "behind ") + deltaText;
        }

        return _title + " - " + pace;
    }

    private string BuildPredictionText()
    {
        if (!_resetAt.HasValue || !_currentUsedPercent.HasValue || _windowMinutes <= 0)
        {
            return "prediction unavailable";
        }

        var start = _resetAt.Value.AddMinutes(-_windowMinutes);
        var now = DateTimeOffset.Now;
        var elapsedHours = Math.Max(0.02, (now - start).TotalHours);
        var remainingHours = Math.Max(0, (_resetAt.Value - now).TotalHours);
        var used = Math.Max(0, Math.Min(100, _currentUsedPercent.Value));
        var currentRate = used / elapsedHours;
        var safeRate = (100 - used) / Math.Max(0.02, remainingHours);

        string emptyText;
        if (used >= 99.5)
        {
            emptyText = "empty now";
        }
        else if (currentRate <= 0.01)
        {
            emptyText = "empty unknown";
        }
        else
        {
            var hoursToEmpty = (100 - used) / currentRate;
            var estimatedEmpty = now.AddHours(hoursToEmpty);
            emptyText = estimatedEmpty <= _resetAt.Value
                ? "empty " + estimatedEmpty.LocalDateTime.ToString("M/d HH:mm", CultureInfo.InvariantCulture)
                : "empty after reset";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} | rate {1:0.0}%/h | safe {2:0.0}%/h",
            emptyText,
            currentRate,
            safeRate);
    }

    private static PointF PointFor(Rectangle plot, double x, double usedPercent)
    {
        var px = plot.Left + (float)(plot.Width * Clamp01(x));
        var py = plot.Bottom - (float)(plot.Height * Math.Max(0, Math.Min(100, usedPercent)) / 100.0);
        return new PointF(px, py);
    }

    private static double Clamp01(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Max(0, Math.Min(1, value));
    }
}

internal sealed class UsageHistoryChartControl : Control
{
    private string _title = "Usage history";
    private HistoryAggregation _aggregation = HistoryAggregation.Day;
    private List<UsageHistoryPoint> _points = new List<UsageHistoryPoint>();
    private UiTheme _theme = UiThemes.Light;

    public UsageHistoryChartControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = _theme.Background;
        Dock = DockStyle.Fill;
        MinimumSize = new Size(260, 150);
        Font = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
    }

    public void SetData(string title, HistoryAggregation aggregation, List<UsageHistoryPoint> points)
    {
        _title = title ?? "Usage history";
        _aggregation = aggregation;
        _points = points ?? new List<UsageHistoryPoint>();
        Invalidate();
    }

    public void SetTheme(UiTheme theme)
    {
        _theme = theme ?? UiThemes.Light;
        BackColor = _theme.Background;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);

        var plot = CalculatePlotRectangle();
        var borderColor = _theme.Border;
        var gridColor = _theme.Grid;
        var axisColor = _theme.MutedText;
        var barColor = _theme.ChartBar;

        using (var titleFont = new Font(Font, FontStyle.Bold))
        {
            var titleHeight = UiScale.LineHeight(titleFont);
            TextRenderer.DrawText(
                g,
                _title + " - " + AggregationLabel(_aggregation),
                titleFont,
                new Rectangle(0, 0, Math.Max(1, Width - UiScale.Scale(8)), titleHeight),
                _theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        if (plot.Width < UiScale.Scale(24) || plot.Height < UiScale.Scale(24))
        {
            return;
        }

        var maxValue = CalculateMaxValue(_points);
        DrawAxes(g, plot, maxValue, axisColor, gridColor);

        using (var gridPen = new Pen(gridColor))
        using (var borderPen = new Pen(borderColor))
        {
            g.DrawLine(gridPen, plot.Left, plot.Top + plot.Height / 2, plot.Right, plot.Top + plot.Height / 2);
            g.DrawRectangle(borderPen, plot);
        }

        if (_points.Count == 0)
        {
            TextRenderer.DrawText(
                g,
                "No history yet",
                Font,
                plot,
                _theme.MutedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        var slotWidth = plot.Width / (double)Math.Max(1, _points.Count);
        var barWidth = Math.Max(UiScale.Scale(3), Math.Min(UiScale.Scale(26), (int)Math.Round(slotWidth * 0.52)));
        using (var barBrush = new SolidBrush(Color.FromArgb(150, barColor)))
        {
            for (var i = 0; i < _points.Count; i++)
            {
                var point = _points[i];
                var x = plot.Left + (float)(slotWidth * i + slotWidth / 2.0);
                var barHeight = (float)(plot.Height * Math.Max(0, point.UsedPercent) / maxValue);
                var barRect = new RectangleF(
                    x - barWidth / 2F,
                    plot.Bottom - barHeight,
                    barWidth,
                    barHeight);
                g.FillRectangle(barBrush, barRect);
            }
        }

        using (var legendFont = new Font(Font.FontFamily, 7.5F, FontStyle.Regular, GraphicsUnit.Point))
        {
            TextRenderer.DrawText(
                g,
                "usage per period",
                legendFont,
                new Rectangle(plot.Left, plot.Top + UiScale.Scale(3), Math.Max(1, plot.Width - UiScale.Scale(4)), UiScale.LineHeight(legendFont)),
                axisColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    private Rectangle CalculatePlotRectangle()
    {
        using (var titleFont = new Font(Font, FontStyle.Bold))
        using (var labelFont = new Font(Font.FontFamily, 7.5F, FontStyle.Regular, GraphicsUnit.Point))
        {
            var leftMargin = Math.Max(UiScale.Scale(50), UiScale.TextWidth("100%", labelFont) + UiScale.Scale(10));
            var topMargin = UiScale.LineHeight(titleFont) + UiScale.Scale(12);
            var rightMargin = UiScale.Scale(10);
            var bottomMargin = UiScale.LineHeight(labelFont) + UiScale.Scale(12);

            var available = new Rectangle(
                leftMargin,
                topMargin,
                Math.Max(1, Width - leftMargin - rightMargin),
                Math.Max(1, Height - topMargin - bottomMargin));

            var plotWidth = available.Width;
            var plotHeight = available.Height;
            var aspect = plotWidth / (double)plotHeight;
            const double minAspect = 1.25;
            const double maxAspect = 2.45;

            if (aspect < minAspect)
            {
                plotHeight = Math.Max(UiScale.Scale(24), (int)Math.Round(plotWidth / minAspect));
            }
            else if (aspect > maxAspect)
            {
                plotWidth = Math.Max(UiScale.Scale(24), (int)Math.Round(plotHeight * maxAspect));
            }

            plotWidth = Math.Min(plotWidth, available.Width);
            plotHeight = Math.Min(plotHeight, available.Height);

            return new Rectangle(
                available.Left + (available.Width - plotWidth) / 2,
                available.Top + (available.Height - plotHeight) / 3,
                plotWidth,
                plotHeight);
        }
    }

    private void DrawAxes(Graphics g, Rectangle plot, double maxValue, Color axisColor, Color gridColor)
    {
        using (var labelFont = new Font(Font.FontFamily, 7.5F, FontStyle.Regular, GraphicsUnit.Point))
        using (var tickPen = new Pen(gridColor))
        {
            DrawYLabel(g, labelFont, plot, maxValue, maxValue, axisColor, tickPen);
            DrawYLabel(g, labelFont, plot, maxValue / 2.0, maxValue, axisColor, tickPen);
            DrawYLabel(g, labelFont, plot, 0, maxValue, axisColor, tickPen);

            if (_points.Count == 0)
            {
                return;
            }

            var labelTop = plot.Bottom + UiScale.Scale(4);
            var labelWidth = Math.Max(UiScale.Scale(92), UiScale.TextWidth("00/00 00:00", labelFont) + UiScale.Scale(8));
            var labelHeight = UiScale.LineHeight(labelFont);
            var first = _points.First();
            var middle = _points[_points.Count / 2];
            var last = _points.Last();

            TextRenderer.DrawText(
                g,
                first.Label,
                labelFont,
                new Rectangle(plot.Left - UiScale.Scale(2), labelTop, labelWidth, labelHeight),
                axisColor,
                TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            if (plot.Width >= labelWidth * 3 && _points.Count > 2)
            {
                TextRenderer.DrawText(
                    g,
                    middle.Label,
                    labelFont,
                    new Rectangle(plot.Left + plot.Width / 2 - labelWidth / 2, labelTop, labelWidth, labelHeight),
                    axisColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }

            TextRenderer.DrawText(
                g,
                last.Label,
                labelFont,
                new Rectangle(plot.Right - labelWidth, labelTop, labelWidth, labelHeight),
                axisColor,
                TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    private static void DrawYLabel(Graphics g, Font labelFont, Rectangle plot, double value, double maxValue, Color axisColor, Pen tickPen)
    {
        var y = plot.Bottom - (int)Math.Round(plot.Height * value / maxValue);
        g.DrawLine(tickPen, plot.Left - UiScale.Scale(4), y, plot.Left, y);
        var labelHeight = UiScale.LineHeight(labelFont);
        TextRenderer.DrawText(
            g,
            value.ToString("0", CultureInfo.InvariantCulture) + "%",
            labelFont,
            new Rectangle(0, y - labelHeight / 2, Math.Max(1, plot.Left - UiScale.Scale(9)), labelHeight),
            axisColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping);
    }

    private static double CalculateMaxValue(List<UsageHistoryPoint> points)
    {
        var max = 10.0;
        foreach (var point in points)
        {
            max = Math.Max(max, point.UsedPercent);
        }

        return Math.Max(10, Math.Ceiling(max / 10.0) * 10.0);
    }

    private static string AggregationLabel(HistoryAggregation aggregation)
    {
        if (aggregation == HistoryAggregation.Week)
        {
            return "Week";
        }
        if (aggregation == HistoryAggregation.Month)
        {
            return "Month";
        }

        return "Day";
    }
}

internal sealed class SettingsForm : Form
{
    private readonly CheckBox _showCodex;
    private readonly CheckBox _showClaude;
    private readonly CheckBox _alwaysOnTop;
    private readonly CheckBox _minimizeToTray;
    private readonly CheckBox _startWithWindows;
    private readonly CheckBox _startAtTopRight;
    private readonly CheckBox _compactMode;
    private readonly CheckBox _useRealtimeApi;
    private readonly CheckBox _alertsEnabled;
    private readonly ComboBox _theme;
    private readonly NumericUpDown _pollIntervalSeconds;
    private readonly NumericUpDown _realtimeTimeoutSeconds;
    private readonly NumericUpDown _fiveHourThreshold;
    private readonly NumericUpDown _longWindowThreshold;

    public SettingsForm(MonitorConfig config)
    {
        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(248, 249, 250);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = UiScale.FitToWorkingArea(UiScale.Size(560, 700), 48, 80);
        AutoScroll = true;

        _showCodex = new CheckBox();
        _showClaude = new CheckBox();
        _alwaysOnTop = new CheckBox();
        _minimizeToTray = new CheckBox();
        _startWithWindows = new CheckBox();
        _startAtTopRight = new CheckBox();
        _compactMode = new CheckBox();
        _useRealtimeApi = new CheckBox();
        _alertsEnabled = new CheckBox();
        _theme = new ComboBox();
        _pollIntervalSeconds = BuildNumber(3, 86400, 0);
        _realtimeTimeoutSeconds = BuildNumber(3, 120, 0);
        _fiveHourThreshold = BuildNumber(0, 100, 0);
        _longWindowThreshold = BuildNumber(0, 100, 0);

        _showCodex.Checked = config.CodexVisible;
        _showClaude.Checked = config.ClaudeVisible;
        _alwaysOnTop.Checked = config.alwaysOnTop;
        _minimizeToTray.Checked = config.minimizeToTray;
        _startWithWindows.Checked = config.startWithWindows;
        _startAtTopRight.Checked = config.startAtTopRight;
        _compactMode.Checked = config.compactMode;
        _useRealtimeApi.Checked = config.useRealtimeApi;
        _alertsEnabled.Checked = config.alertsEnabled;
        ConfigureThemePicker(_theme, config.theme);
        _pollIntervalSeconds.Value = ClampDecimal(config.pollIntervalSeconds, _pollIntervalSeconds.Minimum, _pollIntervalSeconds.Maximum);
        _realtimeTimeoutSeconds.Value = ClampDecimal(config.realtimeApiTimeoutSeconds, _realtimeTimeoutSeconds.Minimum, _realtimeTimeoutSeconds.Maximum);
        _fiveHourThreshold.Value = ClampDecimal((decimal)config.alertFiveHourRemainingPercent, _fiveHourThreshold.Minimum, _fiveHourThreshold.Maximum);
        _longWindowThreshold.Value = ClampDecimal((decimal)config.alertLongWindowRemainingPercent, _longWindowThreshold.Minimum, _longWindowThreshold.Maximum);

        var outer = new TableLayoutPanel();
        outer.Dock = DockStyle.Fill;
        outer.ColumnCount = 1;
        outer.RowCount = 2;
        outer.Padding = UiScale.Padding(18, 16, 18, 14);
        outer.BackColor = BackColor;
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, UiScale.Scale(50)));

        var root = new TableLayoutPanel();
        root.Dock = DockStyle.Fill;
        root.ColumnCount = 2;
        root.Margin = new Padding(0);
        root.Padding = new Padding(0);
        root.BackColor = BackColor;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiScale.Scale(340)));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddSection(root, "Display");
        AddCheck(root, _showCodex, "Show Codex");
        AddCheck(root, _showClaude, "Show Claude");
        AddCheck(root, _alwaysOnTop, "Always on top");
        AddCheck(root, _minimizeToTray, "Minimize to system tray");
        AddCheck(root, _startWithWindows, "Start with Windows");
        AddCheck(root, _startAtTopRight, "Start at top-right");
        AddCheck(root, _compactMode, "Compact mode");
        AddCombo(root, "Theme", _theme);

        AddSection(root, "Refresh");
        AddCheck(root, _useRealtimeApi, "Use realtime API");
        AddNumber(root, "Refresh interval (seconds)", _pollIntervalSeconds);
        AddNumber(root, "Realtime timeout (seconds)", _realtimeTimeoutSeconds);

        AddSection(root, "Alerts");
        AddCheck(root, _alertsEnabled, "Enable quota alerts");
        AddNumber(root, "5h remaining threshold (%)", _fiveHourThreshold);
        AddNumber(root, "Week/7d remaining threshold (%)", _longWindowThreshold);

        var buttons = new TableLayoutPanel();
        buttons.Dock = DockStyle.Fill;
        buttons.ColumnCount = 3;
        buttons.RowCount = 1;
        buttons.Margin = UiScale.Padding(0, 10, 0, 0);
        buttons.BackColor = BackColor;
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiScale.Scale(106)));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiScale.Scale(102)));

        var okButton = new Button();
        okButton.Text = "OK";
        okButton.DialogResult = DialogResult.OK;
        okButton.Size = UiScale.Size(98, 34);
        okButton.Dock = DockStyle.Fill;
        okButton.Margin = UiScale.Padding(4, 0, 0, 0);
        StyleDialogButton(okButton, true);

        var cancelButton = new Button();
        cancelButton.Text = "Cancel";
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Size = UiScale.Size(98, 34);
        cancelButton.Dock = DockStyle.Fill;
        cancelButton.Margin = UiScale.Padding(0, 0, 4, 0);
        StyleDialogButton(cancelButton, false);

        buttons.Controls.Add(cancelButton, 1, 0);
        buttons.Controls.Add(okButton, 2, 0);
        outer.Controls.Add(root, 0, 0);
        outer.Controls.Add(buttons, 0, 1);

        AcceptButton = okButton;
        CancelButton = cancelButton;
        Controls.Add(outer);

        _alertsEnabled.CheckedChanged += delegate { SyncAlertControls(); };
        SyncAlertControls();
    }

    public void ApplyTo(MonitorConfig config)
    {
        config.showCodex = _showCodex.Checked;
        config.showClaude = _showClaude.Checked;
        config.alwaysOnTop = _alwaysOnTop.Checked;
        config.minimizeToTray = _minimizeToTray.Checked;
        config.startWithWindows = _startWithWindows.Checked;
        config.startAtTopRight = _startAtTopRight.Checked;
        config.compactMode = _compactMode.Checked;
        config.useRealtimeApi = _useRealtimeApi.Checked;
        config.alertsEnabled = _alertsEnabled.Checked;
        config.theme = _theme.SelectedIndex == 1 ? "dark" : "light";
        config.pollIntervalSeconds = (int)_pollIntervalSeconds.Value;
        config.realtimeApiTimeoutSeconds = (int)_realtimeTimeoutSeconds.Value;
        config.alertFiveHourRemainingPercent = (double)_fiveHourThreshold.Value;
        config.alertLongWindowRemainingPercent = (double)_longWindowThreshold.Value;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && !_showCodex.Checked && !_showClaude.Checked)
        {
            MessageBox.Show(this, "Select at least Codex or Claude.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    private void SyncAlertControls()
    {
        _fiveHourThreshold.Enabled = _alertsEnabled.Checked;
        _longWindowThreshold.Enabled = _alertsEnabled.Checked;
    }

    private static NumericUpDown BuildNumber(decimal minimum, decimal maximum, int decimalPlaces)
    {
        var number = new NumericUpDown();
        number.Minimum = minimum;
        number.Maximum = maximum;
        number.DecimalPlaces = decimalPlaces;
        number.Increment = decimalPlaces == 0 ? 1 : 0.5M;
        number.Dock = DockStyle.Left;
        number.Width = UiScale.Scale(124);
        number.TextAlign = HorizontalAlignment.Right;
        number.Margin = UiScale.Padding(0, 3, 0, 3);
        return number;
    }

    private static void ConfigureThemePicker(ComboBox comboBox, string selectedTheme)
    {
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox.Dock = DockStyle.Left;
        comboBox.Width = UiScale.Scale(124);
        comboBox.Margin = UiScale.Padding(0, 3, 0, 3);
        comboBox.Items.Add("Light");
        comboBox.Items.Add("Dark");
        comboBox.SelectedIndex = string.Equals(selectedTheme, "dark", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private static void AddSection(TableLayoutPanel root, string text)
    {
        var label = new Label();
        label.Text = text;
        label.Dock = DockStyle.Fill;
        label.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
        label.ForeColor = Color.FromArgb(24, 31, 40);
        label.TextAlign = ContentAlignment.BottomLeft;
        label.Margin = UiScale.Padding(0, 8, 0, 3);
        AddFullWidth(root, label, 36);
    }

    private static void AddCheck(TableLayoutPanel root, CheckBox checkBox, string text)
    {
        checkBox.Text = text;
        checkBox.Dock = DockStyle.Fill;
        checkBox.AutoSize = false;
        checkBox.TextAlign = ContentAlignment.MiddleLeft;
        checkBox.Margin = UiScale.Padding(0, 3, 0, 3);
        checkBox.ForeColor = Color.FromArgb(45, 52, 62);
        AddFullWidth(root, checkBox, 34);
    }

    private static void AddNumber(TableLayoutPanel root, string text, NumericUpDown number)
    {
        var row = AddRow(root, 36);
        var label = new Label();
        label.Text = text;
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.ForeColor = Color.FromArgb(45, 52, 62);
        label.Margin = UiScale.Padding(0, 3, 10, 3);
        root.Controls.Add(label, 0, row);
        root.Controls.Add(number, 1, row);
    }

    private static void AddCombo(TableLayoutPanel root, string text, ComboBox comboBox)
    {
        var row = AddRow(root, 36);
        var label = new Label();
        label.Text = text;
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.ForeColor = Color.FromArgb(45, 52, 62);
        label.Margin = UiScale.Padding(0, 3, 10, 3);
        root.Controls.Add(label, 0, row);
        root.Controls.Add(comboBox, 1, row);
    }

    private static void AddFullWidth(TableLayoutPanel root, Control control, int height)
    {
        var row = AddRow(root, height);
        root.Controls.Add(control, 0, row);
        root.SetColumnSpan(control, 2);
    }

    private static int AddRow(TableLayoutPanel root, int height)
    {
        var row = root.RowStyles.Count;
        root.RowCount = row + 1;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiScale.Scale(height)));
        return row;
    }

    private static decimal ClampDecimal(decimal value, decimal minimum, decimal maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }

    private static void StyleDialogButton(Button button, bool primary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Color.FromArgb(37, 120, 214) : Color.FromArgb(200, 205, 212);
        button.BackColor = primary ? Color.FromArgb(224, 238, 255) : Color.FromArgb(244, 246, 249);
        button.ForeColor = primary ? Color.FromArgb(24, 78, 148) : Color.FromArgb(35, 41, 48);
        button.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    }
}

internal sealed class MainForm : Form
{
    private readonly MonitorConfig _config;
    private UiTheme _theme;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly QuotaBarControl _codexFiveHour;
    private readonly QuotaBarControl _codexWeek;
    private readonly QuotaBarControl _claudeFiveHour;
    private readonly QuotaBarControl _claudeWeek;
    private readonly UsagePaceChartControl _codexPaceChart;
    private readonly UsagePaceChartControl _claudePaceChart;
    private readonly UsageHistoryChartControl _codexHistoryChart;
    private readonly UsageHistoryChartControl _claudeHistoryChart;
    private readonly ServiceHeaderControl _codexHeader;
    private readonly ServiceHeaderControl _claudeHeader;
    private readonly Label _status;
    private readonly Button _refreshButton;
    private readonly CheckBox _topMostCheckBox;
    private readonly Button _paceModeButton;
    private readonly Button _historyModeButton;
    private readonly Button _dayHistoryButton;
    private readonly Button _weekHistoryButton;
    private readonly Button _monthHistoryButton;
    private readonly FlowLayoutPanel _historyRangePanel;
    private readonly HashSet<string> _shownAlertKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private TableLayoutPanel _rootLayout;
    private Control _chartToolbar;
    private TableLayoutPanel _columns;
    private TableLayoutPanel _codexColumn;
    private TableLayoutPanel _claudeColumn;
    private Panel _codexChartHost;
    private Panel _claudeChartHost;
    private NotifyIcon _trayIcon;
    private ToolStripMenuItem _topMostMenuItem;
    private ToolStripMenuItem _showCodexMenuItem;
    private ToolStripMenuItem _showClaudeMenuItem;
    private ToolStripMenuItem _compactModeMenuItem;
    private ToolStripMenuItem _trayTopMostMenuItem;
    private ToolStripMenuItem _trayShowCodexMenuItem;
    private ToolStripMenuItem _trayShowClaudeMenuItem;
    private ToolStripMenuItem _trayCompactModeMenuItem;
    private volatile bool _refreshInProgress;
    private bool _syncingTopMostControl;
    private bool _syncingServiceMenu;
    private bool _syncingCompactMenu;
    private DateTimeOffset? _lastSuccessfulRefreshAt;
    private string _lastDiagnosticsText = "No refresh completed yet.";
    private ChartMode _chartMode = ChartMode.Pace;
    private HistoryAggregation _historyAggregation = HistoryAggregation.Day;

    public MainForm()
    {
        _config = MonitorConfig.LoadOrCreate();
        _theme = UiThemes.FromConfig(_config.theme);
        StartupRegistration.Sync(_config.startWithWindows);

        Text = "Quota Monitor";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = true;
        MaximizeBox = true;
        ShowInTaskbar = true;
        TopMost = _config.alwaysOnTop;
        BackColor = _theme.Background;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = MainClientSize(_config.compactMode);
        MinimumSize = MainMinimumSize(_config.compactMode);
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
        }

        _codexFiveHour = new QuotaBarControl();
        _codexWeek = new QuotaBarControl();
        _claudeFiveHour = new QuotaBarControl();
        _claudeWeek = new QuotaBarControl();
        _codexPaceChart = new UsagePaceChartControl();
        _claudePaceChart = new UsagePaceChartControl();
        _codexHistoryChart = new UsageHistoryChartControl();
        _claudeHistoryChart = new UsageHistoryChartControl();
        _codexHeader = new ServiceHeaderControl("Codex");
        _claudeHeader = new ServiceHeaderControl("Claude");
        _status = new Label();
        _refreshButton = new Button();
        _topMostCheckBox = new CheckBox();
        _paceModeButton = new Button();
        _historyModeButton = new Button();
        _dayHistoryButton = new Button();
        _weekHistoryButton = new Button();
        _monthHistoryButton = new Button();
        _historyRangePanel = new FlowLayoutPanel();

        BuildUi();
        BuildMenu();
        BuildTrayIcon();
        PlaceWindow();

        _timer = new System.Windows.Forms.Timer();
        _timer.Interval = Math.Max(3, _config.pollIntervalSeconds) * 1000;
        _timer.Tick += delegate { RefreshSnapshot(); };
        Shown += delegate
        {
            EnsureClientSizeForMode(_config.compactMode);
            KeepWindowInWorkingArea();
            _codexFiveHour.SetData("5h", "Loading...", null);
            _codexWeek.SetData("Week", "Loading...", null);
            _claudeFiveHour.SetData("5h", "Loading...", null);
            _claudeWeek.SetData("Week", "Loading...", null);
            _codexPaceChart.SetData("Codex Week pace", null, null);
            _claudePaceChart.SetData("Claude 7d pace", null, null);
            RenderHistoryCharts();
            RefreshSnapshot();
            _timer.Start();
        };
    }

    private void BuildUi()
    {
        _rootLayout = new TableLayoutPanel();
        _rootLayout.Dock = DockStyle.Fill;
        _rootLayout.ColumnCount = 1;
        _rootLayout.RowCount = 3;
        _rootLayout.Padding = UiScale.Padding(6);
        _rootLayout.BackColor = BackColor;
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ToolbarHeight()));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, BottomBarHeight(_config.compactMode)));

        _columns = new TableLayoutPanel();
        _columns.Dock = DockStyle.Fill;
        _columns.RowCount = 1;
        _columns.Margin = UiScale.Padding(0, 0, 0, 4);
        _codexColumn = BuildServiceColumn(_codexHeader, _codexFiveHour, _codexWeek, _codexPaceChart, _codexHistoryChart, out _codexChartHost);
        _claudeColumn = BuildServiceColumn(_claudeHeader, _claudeFiveHour, _claudeWeek, _claudePaceChart, _claudeHistoryChart, out _claudeChartHost);
        ApplyServiceVisibility(false);
        _rootLayout.Controls.Add(_columns, 0, 0);
        _chartToolbar = BuildChartToolbar();
        _rootLayout.Controls.Add(_chartToolbar, 0, 1);

        var bottom = new TableLayoutPanel();
        bottom.Dock = DockStyle.Fill;
        bottom.ColumnCount = 3;
        bottom.RowCount = 1;
        bottom.Margin = new Padding(0);
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));

        _refreshButton.Text = "Refresh";
        _refreshButton.Dock = DockStyle.Fill;
        using (var actionFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point))
        {
            _refreshButton.MinimumSize = new Size(108, Math.Max(30, ButtonContentHeight(actionFont)));
        }
        _refreshButton.Margin = UiScale.Padding(6, 3, 0, 2);
        StyleActionButton(_refreshButton, _theme);
        _refreshButton.Click += delegate { RefreshSnapshot(); };

        _topMostCheckBox.Text = "Topmost";
        _topMostCheckBox.Dock = DockStyle.Fill;
        _topMostCheckBox.Checked = _config.alwaysOnTop;
        _topMostCheckBox.AutoSize = false;
        _topMostCheckBox.TextAlign = ContentAlignment.MiddleLeft;
        _topMostCheckBox.ForeColor = _theme.Text;
        _topMostCheckBox.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _topMostCheckBox.Margin = UiScale.Padding(6, 3, 0, 0);
        _topMostCheckBox.CheckedChanged += delegate
        {
            if (!_syncingTopMostControl)
            {
                SetTopMostEnabled(_topMostCheckBox.Checked, true);
            }
        };

        _status.Dock = DockStyle.Fill;
        _status.ForeColor = _theme.MutedText;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.AutoEllipsis = true;
        _status.Margin = UiScale.Padding(0, 2, 4, 0);
        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(_topMostCheckBox, 1, 0);
        bottom.Controls.Add(_refreshButton, 2, 0);
        _rootLayout.Controls.Add(bottom, 0, 2);

        Controls.Add(_rootLayout);
        ApplyChartMode();
        ApplyCompactMode();
        ApplyTheme();
    }

    private Control BuildChartToolbar()
    {
        var toolbar = new FlowLayoutPanel();
        toolbar.Dock = DockStyle.Fill;
        toolbar.FlowDirection = FlowDirection.LeftToRight;
        toolbar.WrapContents = false;
        toolbar.BackColor = BackColor;
        toolbar.Margin = UiScale.Padding(4, 4, 4, 2);
        toolbar.Padding = new Padding(0);

        ConfigureToggleButton(_paceModeButton, "Pace", 76);
        _paceModeButton.Click += delegate
        {
            _chartMode = ChartMode.Pace;
            ApplyChartMode();
        };
        toolbar.Controls.Add(_paceModeButton);

        ConfigureToggleButton(_historyModeButton, "History", 86);
        _historyModeButton.Click += delegate
        {
            _chartMode = ChartMode.History;
            ApplyChartMode();
            RenderHistoryCharts();
        };
        toolbar.Controls.Add(_historyModeButton);

        _historyRangePanel.Dock = DockStyle.None;
        _historyRangePanel.FlowDirection = FlowDirection.LeftToRight;
        _historyRangePanel.WrapContents = false;
        _historyRangePanel.AutoSize = true;
        _historyRangePanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _historyRangePanel.Height = UiScale.Scale(36);
        _historyRangePanel.Margin = UiScale.Padding(10, 0, 0, 0);
        _historyRangePanel.Padding = new Padding(0);
        _historyRangePanel.BackColor = BackColor;

        ConfigureToggleButton(_dayHistoryButton, "Day", 64);
        _dayHistoryButton.Click += delegate { SetHistoryAggregation(HistoryAggregation.Day); };
        _historyRangePanel.Controls.Add(_dayHistoryButton);

        ConfigureToggleButton(_weekHistoryButton, "Week", 70);
        _weekHistoryButton.Click += delegate { SetHistoryAggregation(HistoryAggregation.Week); };
        _historyRangePanel.Controls.Add(_weekHistoryButton);

        ConfigureToggleButton(_monthHistoryButton, "Month", 76);
        _monthHistoryButton.Click += delegate { SetHistoryAggregation(HistoryAggregation.Month); };
        _historyRangePanel.Controls.Add(_monthHistoryButton);
        toolbar.Controls.Add(_historyRangePanel);

        return toolbar;
    }

    private static void ConfigureToggleButton(Button button, string text, int width)
    {
        button.Text = text;
        button.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        var buttonWidth = Math.Max(width, UiScale.TextWidth(text, button.Font) + UiScale.Scale(20));
        var buttonHeight = Math.Max(32, ButtonContentHeight(button.Font));
        button.Size = new Size(buttonWidth, buttonHeight);
        button.MinimumSize = new Size(buttonWidth, buttonHeight);
        button.Margin = UiScale.Padding(0, 2, 6, 2);
        button.Padding = new Padding(0);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
    }

    private void SetHistoryAggregation(HistoryAggregation aggregation)
    {
        _historyAggregation = aggregation;
        _chartMode = ChartMode.History;
        ApplyChartMode();
        RenderHistoryCharts();
    }

    private void ApplyChartMode()
    {
        var showHistory = _chartMode == ChartMode.History && !_config.compactMode;
        var showCharts = !_config.compactMode;
        _codexPaceChart.Visible = showCharts && !showHistory;
        _claudePaceChart.Visible = showCharts && !showHistory;
        _codexHistoryChart.Visible = showHistory;
        _claudeHistoryChart.Visible = showHistory;
        _historyRangePanel.Visible = showHistory;
        _paceModeButton.Enabled = showCharts;
        _historyModeButton.Enabled = showCharts;

        StyleToggleButton(_paceModeButton, _chartMode == ChartMode.Pace, true, _theme);
        StyleToggleButton(_historyModeButton, _chartMode == ChartMode.History, true, _theme);
        StyleToggleButton(_dayHistoryButton, showHistory && _historyAggregation == HistoryAggregation.Day, false, _theme);
        StyleToggleButton(_weekHistoryButton, showHistory && _historyAggregation == HistoryAggregation.Week, false, _theme);
        StyleToggleButton(_monthHistoryButton, showHistory && _historyAggregation == HistoryAggregation.Month, false, _theme);
        _dayHistoryButton.Enabled = true;
        _weekHistoryButton.Enabled = true;
        _monthHistoryButton.Enabled = true;
    }

    private void ApplyCompactMode()
    {
        if (_rootLayout == null)
        {
            return;
        }

        var compact = _config.compactMode;
        _chartToolbar.Visible = !compact;
        _codexChartHost.Visible = !compact;
        _claudeChartHost.Visible = !compact;
        _columns.Margin = compact ? new Padding(0) : UiScale.Padding(0, 0, 0, 4);
        _rootLayout.RowStyles[1].SizeType = SizeType.Absolute;
        _rootLayout.RowStyles[1].Height = compact ? 0 : ToolbarHeight();
        _rootLayout.RowStyles[2].SizeType = SizeType.Absolute;
        _rootLayout.RowStyles[2].Height = BottomBarHeight(compact);
        ApplyColumnCompactMode(_codexColumn, compact);
        ApplyColumnCompactMode(_claudeColumn, compact);
        if (compact)
        {
            _rootLayout.RowStyles[0].SizeType = SizeType.Absolute;
            _rootLayout.RowStyles[0].Height = CompactColumnsRowHeight();
        }
        else
        {
            _rootLayout.RowStyles[0].SizeType = SizeType.Percent;
            _rootLayout.RowStyles[0].Height = 100;
        }

        MinimumSize = compact
            ? UiScale.FitToWorkingArea(new Size(MainMinimumSize(true).Width, CompactClientHeightFromLayout()), 64, 96)
            : MainMinimumSize(false);
        EnsureClientSizeForMode(compact);
        KeepWindowInWorkingArea();
        SyncCompactMenus();

        ApplyChartMode();
        PerformLayout();
    }

    private void ApplyTheme()
    {
        _theme = UiThemes.FromConfig(_config.theme);
        BackColor = _theme.Background;
        ApplyBackColorRecursive(this, _theme.Background);

        _codexHeader.SetTheme(_theme);
        _claudeHeader.SetTheme(_theme);
        _codexFiveHour.SetTheme(_theme);
        _codexWeek.SetTheme(_theme);
        _claudeFiveHour.SetTheme(_theme);
        _claudeWeek.SetTheme(_theme);
        _codexPaceChart.SetTheme(_theme);
        _claudePaceChart.SetTheme(_theme);
        _codexHistoryChart.SetTheme(_theme);
        _claudeHistoryChart.SetTheme(_theme);

        _topMostCheckBox.ForeColor = _theme.Text;
        _status.ForeColor = _theme.MutedText;
        StyleActionButton(_refreshButton, _theme);
        ApplyChartMode();
        Invalidate(true);
    }

    private static void ApplyBackColorRecursive(Control control, Color color)
    {
        if (!(control is Button) && !(control is CheckBox) && !(control is NumericUpDown) && !(control is ComboBox))
        {
            control.BackColor = color;
        }

        foreach (Control child in control.Controls)
        {
            ApplyBackColorRecursive(child, color);
        }
    }

    private static void ApplyColumnCompactMode(TableLayoutPanel column, bool compact)
    {
        if (column == null || column.RowStyles.Count < 4)
        {
            return;
        }

        var header = column.GetControlFromPosition(0, 0) as ServiceHeaderControl;
        var first = column.GetControlFromPosition(0, 1) as QuotaBarControl;
        var second = column.GetControlFromPosition(0, 2) as QuotaBarControl;
        if (header != null)
        {
            header.SetCompactMode(compact);
        }
        if (first != null)
        {
            first.SetCompactMode(compact);
            first.Margin = compact ? new Padding(0) : UiScale.Padding(0, 0, 0, 1);
        }
        if (second != null)
        {
            second.SetCompactMode(compact);
            second.Margin = compact ? new Padding(0) : UiScale.Padding(0, 1, 0, 0);
        }

        if (header != null)
        {
            column.RowStyles[0].SizeType = SizeType.Absolute;
            column.RowStyles[0].Height = header.PreferredControlHeight;
        }
        if (first != null)
        {
            column.RowStyles[1].SizeType = SizeType.Absolute;
            column.RowStyles[1].Height = first.PreferredControlHeight;
        }
        if (second != null)
        {
            column.RowStyles[2].SizeType = SizeType.Absolute;
            column.RowStyles[2].Height = second.PreferredControlHeight;
        }

        column.RowStyles[3].SizeType = compact ? SizeType.Absolute : SizeType.Percent;
        column.RowStyles[3].Height = compact ? 0 : 100;
        column.Padding = compact ? UiScale.Padding(4, 2, 4, 3) : UiScale.Padding(6, 4, 6, 6);
        var chartHost = column.GetControlFromPosition(0, 3);
        if (chartHost != null)
        {
            chartHost.Margin = compact ? new Padding(0) : UiScale.Padding(0, 6, 0, 0);
        }
    }

    private static Size MainClientSize(bool compact)
    {
        return UiScale.FitToWorkingArea(compact ? new Size(720, CompactClientHeight()) : new Size(960, 600), 48, 80);
    }

    private static Size MainMinimumSize(bool compact)
    {
        return UiScale.FitToWorkingArea(compact ? new Size(560, CompactClientHeight()) : new Size(780, 500), 64, 96);
    }

    private static int CompactClientHeight()
    {
        using (var headerFont = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point))
        using (var quotaFont = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point))
        using (var quotaTitleFont = new Font(quotaFont, FontStyle.Bold))
        {
            var headerHeight = Math.Max(30, UiScale.LineHeight(headerFont) + UiScale.Scale(2));
            var quotaHeight = Math.Max(
                38,
                UiScale.Scale(2) +
                UiScale.LineHeight(quotaTitleFont) +
                UiScale.Scale(3) +
                UiScale.Scale(8) +
                UiScale.Scale(3));
            var rootPadding = UiScale.Scale(12);
            var columnsMargin = UiScale.Scale(4);
            var columnPadding = UiScale.Scale(5);
            return rootPadding + columnsMargin + columnPadding + headerHeight + quotaHeight * 2 + BottomBarHeight(true);
        }
    }

    private void EnsureClientSizeForMode(bool compact)
    {
        var target = compact
            ? UiScale.FitToWorkingArea(
                new Size(Math.Max(ClientSize.Width, MainClientSize(true).Width), CompactClientHeightFromLayout()),
                48,
                80)
            : MainClientSize(false);
        var current = ClientSize;

        if (compact)
        {
            var compactHeight = target.Height;
            var compactWidth = Math.Max(current.Width, target.Width);
            if (current.Height != compactHeight || current.Width < target.Width)
            {
                ClientSize = UiScale.FitToWorkingArea(new Size(compactWidth, compactHeight), 48, 80);
            }

            return;
        }

        if (current.Height < target.Height || current.Width < target.Width)
        {
            ClientSize = target;
        }
    }

    private void KeepWindowInWorkingArea()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var area = Screen.FromControl(this).WorkingArea;
        var x = Math.Min(Math.Max(Left, area.Left), Math.Max(area.Left, area.Right - Width));
        var y = Math.Min(Math.Max(Top, area.Top), Math.Max(area.Top, area.Bottom - Height));
        if (x != Left || y != Top)
        {
            Location = new Point(x, y);
        }
    }

    private int CompactClientHeightFromLayout()
    {
        if (_rootLayout == null || _columns == null)
        {
            return CompactClientHeight();
        }

        return _rootLayout.Padding.Vertical + CompactColumnsRowHeight() + BottomBarHeight(true);
    }

    private int CompactColumnsRowHeight()
    {
        if (_columns == null)
        {
            return Math.Max(0, CompactClientHeight() - BottomBarHeight(true));
        }

        return _columns.Margin.Vertical + Math.Max(
            ServiceColumnContentHeight(_codexColumn),
            ServiceColumnContentHeight(_claudeColumn));
    }

    private static int ServiceColumnContentHeight(TableLayoutPanel column)
    {
        if (column == null || column.RowStyles.Count < 3)
        {
            return 0;
        }

        return column.Margin.Vertical +
            column.Padding.Vertical +
            (int)Math.Ceiling(column.RowStyles[0].Height) +
            (int)Math.Ceiling(column.RowStyles[1].Height) +
            (int)Math.Ceiling(column.RowStyles[2].Height);
    }

    private static int ToolbarHeight()
    {
        using (var font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point))
        {
            return Math.Max(40, ButtonContentHeight(font) + UiScale.Scale(8));
        }
    }

    private static int BottomBarHeight()
    {
        return BottomBarHeight(false);
    }

    private static int BottomBarHeight(bool compact)
    {
        using (var font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point))
        {
            return Math.Max(compact ? 34 : 38, ButtonContentHeight(font) + UiScale.Scale(compact ? 4 : 8));
        }
    }

    private static int ButtonContentHeight(Font font)
    {
        return UiScale.LineHeight(font) + UiScale.Scale(8);
    }

    private static void StyleToggleButton(Button button, bool selected, bool primary, UiTheme theme)
    {
        if (selected && primary)
        {
            button.FlatAppearance.BorderColor = theme.Accent;
            button.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
            button.FlatAppearance.MouseDownBackColor = theme.ButtonDown;
            button.BackColor = theme.PrimarySelectedBack;
            button.ForeColor = theme.AccentText;
            return;
        }

        if (selected)
        {
            button.FlatAppearance.BorderColor = theme.MutedText;
            button.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
            button.FlatAppearance.MouseDownBackColor = theme.ButtonDown;
            button.BackColor = theme.SecondarySelectedBack;
            button.ForeColor = theme.Text;
            return;
        }

        button.FlatAppearance.BorderColor = theme.Border;
        button.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
        button.FlatAppearance.MouseDownBackColor = theme.ButtonDown;
        button.BackColor = theme.ButtonBack;
        button.ForeColor = theme.Text;
    }

    private void ApplyServiceVisibility(bool persist)
    {
        if (!_config.CodexVisible && !_config.ClaudeVisible)
        {
            _config.showCodex = true;
            _config.showClaude = true;
        }

        if (_columns != null)
        {
            _columns.SuspendLayout();
            try
            {
                _columns.Controls.Clear();
                _columns.ColumnStyles.Clear();
                var visibleCount = (_config.CodexVisible ? 1 : 0) + (_config.ClaudeVisible ? 1 : 0);
                _columns.ColumnCount = Math.Max(1, visibleCount);

                for (var i = 0; i < _columns.ColumnCount; i++)
                {
                    _columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / _columns.ColumnCount));
                }

                var columnIndex = 0;
                if (_config.CodexVisible)
                {
                    _columns.Controls.Add(_codexColumn, columnIndex++, 0);
                }
                if (_config.ClaudeVisible)
                {
                    _columns.Controls.Add(_claudeColumn, columnIndex, 0);
                }
            }
            finally
            {
                _columns.ResumeLayout(true);
            }
        }

        _syncingServiceMenu = true;
        try
        {
            if (_showCodexMenuItem != null)
            {
                _showCodexMenuItem.Checked = _config.CodexVisible;
            }
            if (_showClaudeMenuItem != null)
            {
                _showClaudeMenuItem.Checked = _config.ClaudeVisible;
            }
            if (_trayShowCodexMenuItem != null)
            {
                _trayShowCodexMenuItem.Checked = _config.CodexVisible;
            }
            if (_trayShowClaudeMenuItem != null)
            {
                _trayShowClaudeMenuItem.Checked = _config.ClaudeVisible;
            }
        }
        finally
        {
            _syncingServiceMenu = false;
        }

        if (persist)
        {
            SaveConfig();
        }
    }

    private void SetServiceVisible(string service, bool visible)
    {
        if (string.Equals(service, "codex", StringComparison.OrdinalIgnoreCase))
        {
            _config.showCodex = visible;
        }
        else
        {
            _config.showClaude = visible;
        }

        if (!_config.CodexVisible && !_config.ClaudeVisible)
        {
            if (string.Equals(service, "codex", StringComparison.OrdinalIgnoreCase))
            {
                _config.showCodex = true;
            }
            else
            {
                _config.showClaude = true;
            }
        }

        ApplyServiceVisibility(true);
        RefreshSnapshot();
    }

    private void SetCompactModeEnabled(bool enabled, bool persist)
    {
        _config.compactMode = enabled;
        ApplyCompactMode();
        if (persist)
        {
            SaveConfig();
        }
    }

    private void SyncCompactMenus()
    {
        _syncingCompactMenu = true;
        try
        {
            if (_compactModeMenuItem != null)
            {
                _compactModeMenuItem.Checked = _config.compactMode;
            }
            if (_trayCompactModeMenuItem != null)
            {
                _trayCompactModeMenuItem.Checked = _config.compactMode;
            }
        }
        finally
        {
            _syncingCompactMenu = false;
        }
    }

    private static void StyleActionButton(Button button, UiTheme theme)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = theme.Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
        button.FlatAppearance.MouseDownBackColor = theme.ButtonDown;
        button.BackColor = theme.ButtonBack;
        button.ForeColor = theme.Text;
        button.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    }

    private static TableLayoutPanel BuildServiceColumn(
        ServiceHeaderControl header,
        QuotaBarControl first,
        QuotaBarControl second,
        UsagePaceChartControl paceChart,
        UsageHistoryChartControl historyChart,
        out Panel chartHost)
    {
        var column = new TableLayoutPanel();
        column.Dock = DockStyle.Fill;
        column.ColumnCount = 1;
        column.RowCount = 4;
        column.Padding = UiScale.Padding(6, 4, 6, 6);
        column.Margin = UiScale.Padding(2);
        column.BackColor = Color.FromArgb(248, 249, 250);
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, header.PreferredControlHeight));
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, first.PreferredControlHeight));
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, second.PreferredControlHeight));
        column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        first.Dock = DockStyle.Fill;
        second.Dock = DockStyle.Fill;
        paceChart.Dock = DockStyle.Fill;
        historyChart.Dock = DockStyle.Fill;
        first.Margin = UiScale.Padding(0, 0, 0, 1);
        second.Margin = UiScale.Padding(0, 1, 0, 0);

        chartHost = new Panel();
        chartHost.Dock = DockStyle.Fill;
        chartHost.Margin = UiScale.Padding(0, 6, 0, 0);
        chartHost.BackColor = Color.FromArgb(248, 249, 250);
        chartHost.Controls.Add(historyChart);
        chartHost.Controls.Add(paceChart);

        column.Controls.Add(header, 0, 0);
        column.Controls.Add(first, 0, 1);
        column.Controls.Add(second, 0, 2);
        column.Controls.Add(chartHost, 0, 3);
        return column;
    }

    private void BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh", null, delegate { RefreshSnapshot(); });
        menu.Items.Add("Minimize", null, delegate { MinimizeWindow(); });
        menu.Items.Add(new ToolStripSeparator());
        _showCodexMenuItem = new ToolStripMenuItem("Show Codex");
        _showCodexMenuItem.Checked = _config.CodexVisible;
        _showCodexMenuItem.CheckOnClick = true;
        _showCodexMenuItem.CheckedChanged += delegate
        {
            if (!_syncingServiceMenu)
            {
                SetServiceVisible("codex", _showCodexMenuItem.Checked);
            }
        };
        menu.Items.Add(_showCodexMenuItem);

        _showClaudeMenuItem = new ToolStripMenuItem("Show Claude");
        _showClaudeMenuItem.Checked = _config.ClaudeVisible;
        _showClaudeMenuItem.CheckOnClick = true;
        _showClaudeMenuItem.CheckedChanged += delegate
        {
            if (!_syncingServiceMenu)
            {
                SetServiceVisible("claude", _showClaudeMenuItem.Checked);
            }
        };
        menu.Items.Add(_showClaudeMenuItem);
        menu.Items.Add(new ToolStripSeparator());

        _compactModeMenuItem = new ToolStripMenuItem("Compact mode");
        _compactModeMenuItem.Checked = _config.compactMode;
        _compactModeMenuItem.CheckOnClick = true;
        _compactModeMenuItem.CheckedChanged += delegate
        {
            if (!_syncingCompactMenu)
            {
                SetCompactModeEnabled(_compactModeMenuItem.Checked, true);
            }
        };
        menu.Items.Add(_compactModeMenuItem);

        _topMostMenuItem = new ToolStripMenuItem("Topmost");
        _topMostMenuItem.Checked = _config.alwaysOnTop;
        _topMostMenuItem.CheckOnClick = true;
        _topMostMenuItem.CheckedChanged += delegate
        {
            if (!_syncingTopMostControl)
            {
                SetTopMostEnabled(_topMostMenuItem.Checked, true);
            }
        };
        menu.Items.Add(_topMostMenuItem);
        menu.Items.Add("Diagnostics...", null, delegate { ShowDiagnostics(); });
        menu.Items.Add("Settings...", null, delegate { ShowSettings(); });
        menu.Items.Add("Open config", null, delegate { Process.Start(MonitorConfig.ConfigPath); });
        menu.Items.Add("Exit", null, delegate { Close(); });
        ContextMenuStrip = menu;
    }

    private void BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, delegate { RestoreFromTray(); });
        menu.Items.Add("Refresh", null, delegate
        {
            RestoreFromTray();
            RefreshSnapshot();
        });
        menu.Items.Add("Settings...", null, delegate
        {
            RestoreFromTray();
            ShowSettings();
        });
        menu.Items.Add("Diagnostics...", null, delegate
        {
            RestoreFromTray();
            ShowDiagnostics();
        });
        menu.Items.Add(new ToolStripSeparator());

        _trayShowCodexMenuItem = new ToolStripMenuItem("Show Codex");
        _trayShowCodexMenuItem.Checked = _config.CodexVisible;
        _trayShowCodexMenuItem.CheckOnClick = true;
        _trayShowCodexMenuItem.CheckedChanged += delegate
        {
            if (!_syncingServiceMenu)
            {
                SetServiceVisible("codex", _trayShowCodexMenuItem.Checked);
            }
        };
        menu.Items.Add(_trayShowCodexMenuItem);

        _trayShowClaudeMenuItem = new ToolStripMenuItem("Show Claude");
        _trayShowClaudeMenuItem.Checked = _config.ClaudeVisible;
        _trayShowClaudeMenuItem.CheckOnClick = true;
        _trayShowClaudeMenuItem.CheckedChanged += delegate
        {
            if (!_syncingServiceMenu)
            {
                SetServiceVisible("claude", _trayShowClaudeMenuItem.Checked);
            }
        };
        menu.Items.Add(_trayShowClaudeMenuItem);

        _trayTopMostMenuItem = new ToolStripMenuItem("Topmost");
        _trayTopMostMenuItem.Checked = _config.alwaysOnTop;
        _trayTopMostMenuItem.CheckOnClick = true;
        _trayTopMostMenuItem.CheckedChanged += delegate
        {
            if (!_syncingTopMostControl)
            {
                SetTopMostEnabled(_trayTopMostMenuItem.Checked, true);
            }
        };
        menu.Items.Add(_trayTopMostMenuItem);

        _trayCompactModeMenuItem = new ToolStripMenuItem("Compact mode");
        _trayCompactModeMenuItem.Checked = _config.compactMode;
        _trayCompactModeMenuItem.CheckOnClick = true;
        _trayCompactModeMenuItem.CheckedChanged += delegate
        {
            if (!_syncingCompactMenu)
            {
                SetCompactModeEnabled(_trayCompactModeMenuItem.Checked, true);
            }
        };
        menu.Items.Add(_trayCompactModeMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate { Close(); });

        _trayIcon = new NotifyIcon();
        _trayIcon.Icon = Icon == null ? SystemIcons.Application : Icon;
        _trayIcon.Text = "Quota Monitor";
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += delegate { RestoreFromTray(); };
    }

    private void ShowSettings()
    {
        using (var dialog = new SettingsForm(_config))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            dialog.ApplyTo(_config);
            if (!_config.CodexVisible && !_config.ClaudeVisible)
            {
                _config.showCodex = true;
            }

            _timer.Interval = Math.Max(3, _config.pollIntervalSeconds) * 1000;
            StartupRegistration.Sync(_config.startWithWindows);
            ApplyTheme();
            SetTopMostEnabled(_config.alwaysOnTop, false);
            ApplyServiceVisibility(false);
            ApplyCompactMode();
            SaveConfig();
            RefreshSnapshot();
        }
    }

    private void ShowDiagnostics()
    {
        MessageBox.Show(this, _lastDiagnosticsText, "Quota Monitor diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void MinimizeWindow()
    {
        if (_config.minimizeToTray)
        {
            HideToTray();
            return;
        }

        WindowState = FormWindowState.Minimized;
    }

    private void HideToTray()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = true;
        }

        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        Hide();
    }

    private void RestoreFromTray()
    {
        if (IsDisposed)
        {
            return;
        }

        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    private void SetTopMostEnabled(bool enabled, bool persist)
    {
        _config.alwaysOnTop = enabled;
        TopMost = enabled;
        _syncingTopMostControl = true;
        try
        {
            _topMostCheckBox.Checked = enabled;
            if (_topMostMenuItem != null)
            {
                _topMostMenuItem.Checked = enabled;
            }
            if (_trayTopMostMenuItem != null)
            {
                _trayTopMostMenuItem.Checked = enabled;
            }
        }
        finally
        {
            _syncingTopMostControl = false;
        }

        if (persist)
        {
            SaveConfig();
        }

        if (enabled)
        {
            Activate();
        }
    }

    private void SaveConfig()
    {
        try
        {
            _config.Save();
        }
        catch (Exception ex)
        {
            AppLog.Write("save config error: " + ex);
        }
    }

    private void PlaceWindow()
    {
        if (!_config.startAtTopRight)
        {
            StartPosition = FormStartPosition.CenterScreen;
            return;
        }

        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen.WorkingArea;
        var margin = UiScale.Scale(16);
        Location = new Point(
            Math.Max(area.Left + margin, area.Right - Width - margin),
            area.Top + margin);
    }

    private void RefreshSnapshot()
    {
        if (_refreshInProgress)
        {
            return;
        }

        _refreshInProgress = true;
        _refreshButton.Enabled = false;
        _status.Text = "Loading...";
        AppLog.Write("refresh begin");

        ThreadPool.QueueUserWorkItem(delegate
        {
            QuotaSnapshot snapshot = null;
            Exception error = null;
            try
            {
                snapshot = QuotaReader.Read(_config);
            }
            catch (Exception ex)
            {
                error = ex;
                AppLog.Write("refresh error: " + ex);
            }

            if (IsDisposed)
            {
                return;
            }

            BeginInvoke((MethodInvoker)delegate
            {
                try
                {
                    TopMost = _config.alwaysOnTop;

                    if (error != null)
                    {
                        _status.ForeColor = _theme.Danger;
                        _status.Text = error.Message;
                        _lastDiagnosticsText = "Refresh failed: " + error + Environment.NewLine +
                            "Last successful refresh: " + FormatLocalDateTime(_lastSuccessfulRefreshAt);
                    }
                    else
                    {
                        UsageHistoryStore.AppendSnapshot(snapshot);
                        RenderCodex(snapshot.Codex);
                        RenderClaude(snapshot.Claude);
                        RenderPaceCharts(snapshot);
                        RenderHistoryCharts();
                        UpdateTrayTooltip(snapshot);
                        var alertSummary = EvaluateAlerts(snapshot);
                        _lastSuccessfulRefreshAt = snapshot.UpdatedAt;
                        _lastDiagnosticsText = BuildDiagnostics(snapshot, alertSummary);
                        if (string.IsNullOrWhiteSpace(alertSummary))
                        {
                            var hasIssues = HasServiceIssue(snapshot);
                            _status.ForeColor = hasIssues ? _theme.Warning : _theme.MutedText;
                            _status.Text = BuildStatusLine(snapshot, hasIssues);
                        }
                        else
                        {
                            _status.ForeColor = _theme.Warning;
                            _status.Text = "Alert: " + alertSummary;
                        }
                        AppLog.Write("refresh ok");
                    }
                }
                finally
                {
                    _refreshInProgress = false;
                    _refreshButton.Enabled = true;
                }
            });
        });
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && _config.minimizeToTray)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                if (!IsDisposed && WindowState == FormWindowState.Minimized && _config.minimizeToTray)
                {
                    HideToTray();
                }
            });
        }
    }

    private string BuildStatusLine(QuotaSnapshot snapshot, bool hasIssues)
    {
        var parts = new List<string>
        {
            string.Format(CultureInfo.InvariantCulture, "Updated {0:HH:mm:ss}", snapshot.UpdatedAt.LocalDateTime)
        };

        if (_config.CodexVisible)
        {
            parts.Add("Codex: " + ShortServiceState(snapshot.Codex.Available, snapshot.Codex.Source, snapshot.Codex.Error, snapshot.Codex.FallbackError));
        }
        if (_config.ClaudeVisible)
        {
            parts.Add("Claude: " + ShortServiceState(snapshot.Claude.Available, snapshot.Claude.Source, snapshot.Claude.Error, snapshot.Claude.FallbackError));
        }
        if (hasIssues)
        {
            parts.Add("Diagnostics available");
        }

        return string.Join(" | ", parts.ToArray());
    }

    private bool HasServiceIssue(QuotaSnapshot snapshot)
    {
        return (_config.CodexVisible &&
                (!snapshot.Codex.Available || !string.IsNullOrWhiteSpace(snapshot.Codex.FallbackError))) ||
            (_config.ClaudeVisible &&
                (!snapshot.Claude.Available || !string.IsNullOrWhiteSpace(snapshot.Claude.FallbackError)));
    }

    private static string ShortServiceState(bool available, string source, string error, string fallbackError)
    {
        if (!available)
        {
            return SimplifyError(error);
        }

        var state = SimplifySource(source);
        if (!string.IsNullOrWhiteSpace(fallbackError))
        {
            state += " fallback";
        }

        return state;
    }

    private static string SimplifySource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "unknown";
        }
        if (source.IndexOf("wham", StringComparison.OrdinalIgnoreCase) >= 0 ||
            source.IndexOf("oauth", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "realtime";
        }
        if (source.IndexOf("local", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "local";
        }

        return source;
    }

    private static string SimplifyError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "no data";
        }
        if (error.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0 ||
            error.IndexOf("credentials", StringComparison.OrdinalIgnoreCase) >= 0 ||
            error.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "login needed";
        }
        if (error.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "path missing";
        }

        return error.Length > 28 ? error.Substring(0, 25) + "..." : error;
    }

    private string BuildDiagnostics(QuotaSnapshot snapshot, string alertSummary)
    {
        var lines = new List<string>
        {
            "Last successful refresh: " + FormatLocalDateTime(snapshot.UpdatedAt),
            "Config: refresh " + _config.pollIntervalSeconds + "s, realtime API " + (_config.useRealtimeApi ? "on" : "off") +
                ", theme " + MonitorConfig.NormalizeTheme(_config.theme) +
                ", compact " + (_config.compactMode ? "on" : "off"),
            "Alerts: " + (string.IsNullOrWhiteSpace(alertSummary) ? "none" : alertSummary),
            "",
            BuildCodexDiagnostics(snapshot.Codex),
            "",
            BuildClaudeDiagnostics(snapshot.Claude)
        };

        return string.Join(Environment.NewLine, lines.ToArray());
    }

    private static string BuildCodexDiagnostics(CodexSnapshot codex)
    {
        var lines = new List<string>
        {
            "Codex",
            "  available: " + codex.Available,
            "  source: " + (codex.Source ?? ""),
            "  plan: " + FormatPlan(codex.PlanType),
            "  5h remaining: " + FormatShortPercent(codex.Primary == null ? null : codex.Primary.RemainingPercent),
            "  Week remaining: " + FormatShortPercent(codex.Secondary == null ? null : codex.Secondary.RemainingPercent)
        };

        AddDiagnosticLine(lines, "  error", codex.Error);
        AddDiagnosticLine(lines, "  realtime fallback error", codex.FallbackError);
        return string.Join(Environment.NewLine, lines.ToArray());
    }

    private static string BuildClaudeDiagnostics(ClaudeSnapshot claude)
    {
        var five = claude.RealtimeFiveHour == null
            ? claude.RemainingTokenPercent ?? claude.RemainingMessagePercent
            : claude.RealtimeFiveHour.RemainingPercent;
        var week = claude.RealtimeWeek == null
            ? claude.WeeklyRemainingTokenPercent ?? claude.WeeklyRemainingMessagePercent
            : claude.RealtimeWeek.RemainingPercent;
        var lines = new List<string>
        {
            "Claude",
            "  available: " + claude.Available,
            "  source: " + (claude.Source ?? ""),
            "  plan: " + FormatPlan(claude.PlanType),
            "  5h remaining: " + FormatShortPercent(five),
            "  7d remaining: " + FormatShortPercent(week)
        };

        AddDiagnosticLine(lines, "  error", claude.Error);
        AddDiagnosticLine(lines, "  realtime fallback error", claude.FallbackError);
        return string.Join(Environment.NewLine, lines.ToArray());
    }

    private static void AddDiagnosticLine(List<string> lines, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(label + ": " + value);
        }
    }

    private string EvaluateAlerts(QuotaSnapshot snapshot)
    {
        if (!_config.alertsEnabled)
        {
            return null;
        }

        var activeAlerts = new List<string>();
        var newAlerts = new List<string>();

        if (_config.CodexVisible && snapshot.Codex.Available)
        {
            AddWindowAlert("Codex", "5h", snapshot.Codex.Primary, _config.alertFiveHourRemainingPercent, activeAlerts, newAlerts);
            AddWindowAlert("Codex", "Week", snapshot.Codex.Secondary, _config.alertLongWindowRemainingPercent, activeAlerts, newAlerts);
        }

        if (_config.ClaudeVisible && snapshot.Claude.Available)
        {
            AddWindowAlert("Claude", "5h", snapshot.Claude.RealtimeFiveHour, _config.alertFiveHourRemainingPercent, activeAlerts, newAlerts);
            AddWindowAlert("Claude", "7d", snapshot.Claude.RealtimeWeek, _config.alertLongWindowRemainingPercent, activeAlerts, newAlerts);

            if (snapshot.Claude.RealtimeFiveHour == null)
            {
                AddRemainingAlert(
                    "Claude",
                    "5h",
                    snapshot.Claude.RemainingTokenPercent ?? snapshot.Claude.RemainingMessagePercent,
                    snapshot.Claude.EstimatedResetAt,
                    _config.alertFiveHourRemainingPercent,
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
                    _config.alertLongWindowRemainingPercent,
                    activeAlerts,
                    newAlerts);
            }
        }

        if (newAlerts.Count > 0)
        {
            ShowQuotaAlert(newAlerts);
        }

        if (activeAlerts.Count == 0)
        {
            return null;
        }

        return string.Join("; ", activeAlerts.Take(3).ToArray()) + (activeAlerts.Count > 3 ? "; ..." : "");
    }

    private void AddWindowAlert(string service, string windowName, CodexWindow window, double threshold, List<string> activeAlerts, List<string> newAlerts)
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

    private void ShowQuotaAlert(List<string> newAlerts)
    {
        if (_trayIcon == null || newAlerts.Count == 0)
        {
            return;
        }

        try
        {
            var message = string.Join(Environment.NewLine, newAlerts.Take(4).ToArray());
            if (newAlerts.Count > 4)
            {
                message += Environment.NewLine + "...";
            }

            _trayIcon.ShowBalloonTip(8000, "Quota Monitor alert", message, ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            AppLog.Write("tray alert error: " + ex.Message);
        }
    }

    private void UpdateTrayTooltip(QuotaSnapshot snapshot)
    {
        if (_trayIcon == null)
        {
            return;
        }

        var parts = new List<string>();
        if (_config.CodexVisible && snapshot.Codex.Available)
        {
            parts.Add("Codex 5h " + FormatShortPercent(snapshot.Codex.Primary == null ? null : snapshot.Codex.Primary.RemainingPercent) +
                " W " + FormatShortPercent(snapshot.Codex.Secondary == null ? null : snapshot.Codex.Secondary.RemainingPercent));
        }

        if (_config.ClaudeVisible && snapshot.Claude.Available)
        {
            var claudeFive = snapshot.Claude.RealtimeFiveHour == null
                ? snapshot.Claude.RemainingTokenPercent ?? snapshot.Claude.RemainingMessagePercent
                : snapshot.Claude.RealtimeFiveHour.RemainingPercent;
            var claudeLong = snapshot.Claude.RealtimeWeek == null
                ? snapshot.Claude.WeeklyRemainingTokenPercent ?? snapshot.Claude.WeeklyRemainingMessagePercent
                : snapshot.Claude.RealtimeWeek.RemainingPercent;
            parts.Add("Claude 5h " + FormatShortPercent(claudeFive) + " 7d " + FormatShortPercent(claudeLong));
        }

        var text = parts.Count == 0 ? "Quota Monitor" : "Quota: " + string.Join(" | ", parts.ToArray());
        if (text.Length > 63)
        {
            text = text.Substring(0, 60) + "...";
        }

        try
        {
            _trayIcon.Text = text;
        }
        catch
        {
            _trayIcon.Text = "Quota Monitor";
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try
        {
            _timer.Stop();
            _timer.Dispose();
        }
        catch
        {
        }

        try
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
        }
        catch
        {
        }

        base.OnFormClosed(e);
    }

    private void RenderCodex(CodexSnapshot codex)
    {
        _codexHeader.SetPlan("Plan: " + FormatPlan(codex.PlanType));
        if (!codex.Available)
        {
            _codexFiveHour.SetData("5h", "No data: " + (codex.Error ?? ""), null);
            _codexWeek.SetData("Week", "No data", null);
            return;
        }

        _codexFiveHour.SetData(
            FormatCodexRemaining(codex.Primary, "5h"),
            "reset " + FormatReset(codex.Primary == null ? null : codex.Primary.ResetsAt) +
            " | used " + FormatUsedPercent(codex.Primary),
            codex.Primary == null ? null : codex.Primary.RemainingPercent);

        _codexWeek.SetData(
            FormatCodexRemaining(codex.Secondary, "Week"),
            "reset " + FormatReset(codex.Secondary == null ? null : codex.Secondary.ResetsAt) +
            " | used " + FormatUsedPercent(codex.Secondary),
            codex.Secondary == null ? null : codex.Secondary.RemainingPercent);
    }

    private void RenderClaude(ClaudeSnapshot claude)
    {
        _claudeHeader.SetPlan("Plan: " + FormatPlan(claude.PlanType));
        if (!claude.Available)
        {
            _claudeFiveHour.SetData("5h estimate", "No data: " + (claude.Error ?? ""), null);
            _claudeWeek.SetData("7d estimate", "No data", null);
            return;
        }

        if (claude.RealtimeFiveHour != null || claude.RealtimeWeek != null)
        {
            _claudeFiveHour.SetData(
                FormatCodexRemaining(claude.RealtimeFiveHour, "5h"),
                "reset " + FormatReset(claude.RealtimeFiveHour == null ? null : claude.RealtimeFiveHour.ResetsAt) +
                " | used " + FormatUsedPercent(claude.RealtimeFiveHour),
                claude.RealtimeFiveHour == null ? null : claude.RealtimeFiveHour.RemainingPercent);

            _claudeWeek.SetData(
                FormatCodexRemaining(claude.RealtimeWeek, "7d"),
                "reset " + FormatReset(claude.RealtimeWeek == null ? null : claude.RealtimeWeek.ResetsAt) +
                " | used " + FormatUsedPercent(claude.RealtimeWeek),
                claude.RealtimeWeek == null ? null : claude.RealtimeWeek.RemainingPercent);
            return;
        }
        else if (claude.TokenBudget > 0)
        {
            _claudeFiveHour.SetData(
                "5h est. left " + FormatTokens(claude.RemainingTokens.Value),
                "used " + claude.MessageCount + " msg, " + FormatTokens(claude.WeightedTokens) +
                " | reset~ " + FormatReset(claude.EstimatedResetAt),
                claude.RemainingTokenPercent);
        }
        else if (claude.MessageBudget > 0)
        {
            _claudeFiveHour.SetData(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "5h est. left {0}/{1} msg",
                    claude.RemainingMessages.Value,
                    claude.MessageBudget),
                "used " + claude.MessageCount + " msg, " + FormatTokens(claude.WeightedTokens) +
                " | reset~ " + FormatReset(claude.EstimatedResetAt),
                claude.RemainingMessagePercent);
        }
        else
        {
            _claudeFiveHour.SetData(
                "5h local usage",
                "used " + claude.MessageCount + " msg, " + FormatTokens(claude.WeightedTokens),
                null);
        }

        if (claude.WeeklyTokenBudget > 0)
        {
            _claudeWeek.SetData(
                "Week est. left " + FormatTokens(claude.WeeklyRemainingTokens.Value),
                "used " + claude.WeeklyMessageCount + " msg, " + FormatTokens(claude.WeeklyWeightedTokens) +
                " | reset~ " + FormatReset(claude.EstimatedWeeklyResetAt),
                claude.WeeklyRemainingTokenPercent);
        }
        else if (claude.WeeklyMessageBudget > 0)
        {
            _claudeWeek.SetData(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Week est. left {0}/{1} msg",
                    claude.WeeklyRemainingMessages.Value,
                    claude.WeeklyMessageBudget),
                "used " + claude.WeeklyMessageCount + " msg, " + FormatTokens(claude.WeeklyWeightedTokens) +
                " | reset~ " + FormatReset(claude.EstimatedWeeklyResetAt),
                claude.WeeklyRemainingMessagePercent);
        }
        else
        {
            _claudeWeek.SetData(
                "7d local usage",
                "used " + claude.WeeklyMessageCount + " msg, " + FormatTokens(claude.WeeklyWeightedTokens),
                null);
        }
    }

    private void RenderPaceCharts(QuotaSnapshot snapshot)
    {
        var codexSamples = snapshot.Codex.Secondary != null && snapshot.Codex.Secondary.ResetsAt.HasValue
            ? UsageHistoryStore.Load("Codex", "Week", snapshot.Codex.Secondary.ResetsAt.Value)
            : new List<UsageSample>();
        _codexPaceChart.SetData("Codex Week pace", snapshot.Codex.Secondary, codexSamples);

        var claudeSamples = snapshot.Claude.RealtimeWeek != null && snapshot.Claude.RealtimeWeek.ResetsAt.HasValue
            ? UsageHistoryStore.Load("Claude", "7d", snapshot.Claude.RealtimeWeek.ResetsAt.Value)
            : new List<UsageSample>();
        _claudePaceChart.SetData("Claude 7d pace", snapshot.Claude.RealtimeWeek, claudeSamples);
    }

    private void RenderHistoryCharts()
    {
        _codexHistoryChart.SetData(
            "Codex Week usage",
            _historyAggregation,
            UsageHistoryStore.LoadUsageHistory("Codex", "Week", _historyAggregation));

        _claudeHistoryChart.SetData(
            "Claude 7d usage",
            _historyAggregation,
            UsageHistoryStore.LoadUsageHistory("Claude", "7d", _historyAggregation));
    }

    private static string FormatCodexRemaining(CodexWindow window, string label)
    {
        if (window == null || !window.RemainingPercent.HasValue)
        {
            return label + " --";
        }

        return string.Format(CultureInfo.InvariantCulture, "{0} left {1:0}%", label, window.RemainingPercent.Value);
    }

    private static string FormatPlan(string planType)
    {
        if (string.IsNullOrWhiteSpace(planType))
        {
            return "unknown";
        }

        var normalized = planType.Trim().Replace("_", " ").Replace("-", " ");
        if (normalized.Length == 0)
        {
            return "unknown";
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private static string FormatUsedPercent(CodexWindow window)
    {
        if (window == null || !window.UsedPercent.HasValue)
        {
            return "--";
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0}%", window.UsedPercent.Value);
    }

    private static string FormatShortPercent(double? value)
    {
        if (!value.HasValue)
        {
            return "--";
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0}%", value.Value);
    }

    private static string FormatReset(DateTimeOffset? resetAt)
    {
        if (!resetAt.HasValue)
        {
            return "--";
        }

        var remaining = resetAt.Value - DateTimeOffset.Now;
        if (remaining.TotalSeconds < 0)
        {
            return "soon";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:HH:mm} ({1})",
            resetAt.Value.LocalDateTime,
            FormatDuration(remaining));
    }

    private static string FormatLocalDateTime(DateTimeOffset? value)
    {
        if (!value.HasValue)
        {
            return "--";
        }

        return value.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}d {1}h",
                (int)duration.TotalDays,
                duration.Hours);
        }
        if (duration.TotalHours >= 1)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}h {1}m",
                (int)duration.TotalHours,
                duration.Minutes);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0}m", Math.Max(0, (int)duration.TotalMinutes));
    }

    private static string FormatTokens(long tokens)
    {
        var abs = Math.Abs(tokens);
        if (abs >= 1000000)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0}M tok", tokens / 1000000.0);
        }
        if (abs >= 1000)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0}K tok", tokens / 1000.0);
        }

        return tokens + " tok";
    }

    private static Color PickColor(double? remainingPercent)
    {
        if (!remainingPercent.HasValue)
        {
            return Color.FromArgb(19, 94, 191);
        }
        if (remainingPercent.Value <= 10)
        {
            return Color.FromArgb(190, 55, 45);
        }
        if (remainingPercent.Value <= 25)
        {
            return Color.FromArgb(180, 118, 20);
        }

        return Color.FromArgb(19, 94, 191);
    }
}

internal static class Json
{
    public static JavaScriptSerializer NewSerializer()
    {
        return new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 512
        };
    }

    public static Dictionary<string, object> ParseObject(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return Dict(NewSerializer().DeserializeObject(line));
        }
        catch
        {
            return null;
        }
    }

    public static object Value(Dictionary<string, object> dict, string name)
    {
        object value;
        return dict != null && dict.TryGetValue(name, out value) ? value : null;
    }

    public static Dictionary<string, object> Dict(object value)
    {
        return value as Dictionary<string, object>;
    }

    public static string String(Dictionary<string, object> dict, string name)
    {
        var value = Value(dict, name);
        return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    public static long? Long(Dictionary<string, object> dict, string name)
    {
        var value = Value(dict, name);
        if (value == null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    public static int? Int(Dictionary<string, object> dict, string name)
    {
        var value = Long(dict, name);
        return value.HasValue ? (int)value.Value : (int?)null;
    }

    public static double? Double(Dictionary<string, object> dict, string name)
    {
        var value = Value(dict, name);
        if (value == null)
        {
            return null;
        }

        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    public static DateTimeOffset? Date(Dictionary<string, object> dict, string name)
    {
        var text = String(dict, name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        DateTimeOffset parsed;
        if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsed))
        {
            return null;
        }

        return parsed.ToLocalTime();
    }

    public static DateTimeOffset? UnixSeconds(Dictionary<string, object> dict, string name)
    {
        var seconds = Long(dict, name);
        if (!seconds.HasValue)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds.Value).ToLocalTime();
    }

    public static DateTimeOffset? FlexibleDate(object value)
    {
        if (value == null)
        {
            return null;
        }

        try
        {
            if (value is string)
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                long numeric;
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric))
                {
                    return UnixTimestampToLocal(numeric);
                }

                DateTimeOffset parsed;
                if (DateTimeOffset.TryParse(
                        text,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out parsed))
                {
                    return parsed.ToLocalTime();
                }

                return null;
            }

            return UnixTimestampToLocal(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? UnixTimestampToLocal(long value)
    {
        if (value <= 0)
        {
            return null;
        }

        if (value > 100000000000L)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value).ToLocalTime();
        }

        return DateTimeOffset.FromUnixTimeSeconds(value).ToLocalTime();
    }
}

internal static class Http
{
    public static Dictionary<string, object> GetJson(
        string url,
        string bearerToken,
        Dictionary<string, string> extraHeaders,
        int timeoutMs)
    {
        ServicePointManager.SecurityProtocol =
            SecurityProtocolType.Tls12;
        ServicePointManager.Expect100Continue = false;

        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.Accept = "application/json, text/plain, */*";
        request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) QuotaMonitor/1.0";
        request.Timeout = timeoutMs;
        request.ReadWriteTimeout = timeoutMs;
        request.KeepAlive = false;
        request.Pipelined = false;
        request.ProtocolVersion = HttpVersion.Version11;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        request.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearerToken;
        request.Headers[HttpRequestHeader.CacheControl] = "no-cache";

        if (extraHeaders != null)
        {
            foreach (var header in extraHeaders)
            {
                if (string.Equals(header.Key, "Accept", StringComparison.OrdinalIgnoreCase))
                {
                    request.Accept = header.Value;
                }
                else if (string.Equals(header.Key, "User-Agent", StringComparison.OrdinalIgnoreCase))
                {
                    request.UserAgent = header.Value;
                }
                else if (string.Equals(header.Key, "Referer", StringComparison.OrdinalIgnoreCase))
                {
                    request.Referer = header.Value;
                }
                else
                {
                    request.Headers[header.Key] = header.Value;
                }
            }
        }

        using (var response = (HttpWebResponse)request.GetResponse())
        using (var stream = response.GetResponseStream())
        using (var reader = new StreamReader(stream))
        {
            return Json.ParseObject(reader.ReadToEnd());
        }
    }
}

internal static class SharedFile
{
    public static List<string> ReadAllLines(string path)
    {
        using (var stream = new FileStream(
                   path,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            var lines = new List<string>();
            while (!reader.EndOfStream)
            {
                lines.Add(reader.ReadLine());
            }

            return lines;
        }
    }
}

internal static class SelfTest
{
    public static void Write(QuotaSnapshot snapshot)
    {
        var path = Path.Combine(MonitorConfig.AppDir, "quota-monitor.snapshot.txt");
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
        File.WriteAllLines(path, lines);
    }

    private static string NullableDouble(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "";
    }

    private static string NullableInt(int? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Replace("\r", " ").Replace("\n", " ");
    }
}

internal sealed class QuotaSnapshot
{
    public DateTimeOffset UpdatedAt;
    public CodexSnapshot Codex;
    public ClaudeSnapshot Claude;
}

internal sealed class CodexSnapshot
{
    public bool Available;
    public string Error;
    public string Source;
    public string FallbackError;
    public DateTimeOffset Timestamp;
    public string PlanType;
    public string LimitId;
    public string RateLimitReachedType;
    public CodexWindow Primary;
    public CodexWindow Secondary;
    public long TotalTokens;
    public long LastTurnTokens;

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

internal sealed class CodexWindow
{
    public double? UsedPercent;
    public double? RemainingPercent;
    public DateTimeOffset? ResetsAt;
    public int? WindowMinutes;
}

internal sealed class ClaudeSnapshot
{
    public bool Available;
    public string Error;
    public string Source;
    public string FallbackError;
    public string PlanType;
    public int WindowMinutes;
    public int WeekWindowMinutes;
    public int MessageBudget;
    public long TokenBudget;
    public int WeeklyMessageBudget;
    public long WeeklyTokenBudget;
    public int MessageCount;
    public long InputTokens;
    public long OutputTokens;
    public long CacheCreationTokens;
    public long CacheReadTokens;
    public long WeightedTokens;
    public int WeeklyMessageCount;
    public long WeeklyInputTokens;
    public long WeeklyOutputTokens;
    public long WeeklyCacheCreationTokens;
    public long WeeklyCacheReadTokens;
    public long WeeklyWeightedTokens;
    public DateTimeOffset? OldestCountedAt;
    public DateTimeOffset? OldestWeeklyCountedAt;
    public DateTimeOffset? EstimatedResetAt;
    public DateTimeOffset? EstimatedWeeklyResetAt;
    public CodexWindow RealtimeFiveHour;
    public CodexWindow RealtimeWeek;
    public readonly HashSet<string> Models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public int? RemainingMessages
    {
        get { return MessageBudget <= 0 ? (int?)null : Math.Max(0, MessageBudget - MessageCount); }
    }

    public long? RemainingTokens
    {
        get { return TokenBudget <= 0 ? (long?)null : Math.Max(0, TokenBudget - WeightedTokens); }
    }

    public int? WeeklyRemainingMessages
    {
        get { return WeeklyMessageBudget <= 0 ? (int?)null : Math.Max(0, WeeklyMessageBudget - WeeklyMessageCount); }
    }

    public long? WeeklyRemainingTokens
    {
        get { return WeeklyTokenBudget <= 0 ? (long?)null : Math.Max(0, WeeklyTokenBudget - WeeklyWeightedTokens); }
    }

    public double? RemainingTokenPercent
    {
        get
        {
            if (TokenBudget <= 0)
            {
                return null;
            }

            return Math.Max(0, Math.Min(100, (TokenBudget - WeightedTokens) * 100.0 / TokenBudget));
        }
    }

    public double? WeeklyRemainingTokenPercent
    {
        get
        {
            if (WeeklyTokenBudget <= 0)
            {
                return null;
            }

            return Math.Max(0, Math.Min(100, (WeeklyTokenBudget - WeeklyWeightedTokens) * 100.0 / WeeklyTokenBudget));
        }
    }

    public double? RemainingMessagePercent
    {
        get
        {
            if (MessageBudget <= 0 || !RemainingMessages.HasValue)
            {
                return null;
            }

            return Math.Max(0, Math.Min(100, RemainingMessages.Value * 100.0 / MessageBudget));
        }
    }

    public double? WeeklyRemainingMessagePercent
    {
        get
        {
            if (WeeklyMessageBudget <= 0 || !WeeklyRemainingMessages.HasValue)
            {
                return null;
            }

            return Math.Max(0, Math.Min(100, WeeklyRemainingMessages.Value * 100.0 / WeeklyMessageBudget));
        }
    }

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
