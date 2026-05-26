using System.Text.Json.Serialization;
using QuotaMonitor.Core.Infrastructure;

namespace QuotaMonitor.Core.Config;

public sealed class MonitorConfig
{
    public int pollIntervalSeconds { get; set; }
    public bool alwaysOnTop { get; set; }
    public bool startAtTopRight { get; set; }
    public bool showCodex { get; set; }
    public bool showClaude { get; set; }
    public bool minimizeToTray { get; set; }
    public bool startWithSystem { get; set; }
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

    // Legacy compatibility: older Windows builds used this name in config.
    [JsonIgnore]
    public bool startWithWindows
    {
        get => startWithSystem;
        set => startWithSystem = value;
    }

    [JsonIgnore]
    public bool CodexVisible => showCodex;

    [JsonIgnore]
    public bool ClaudeVisible => showClaude;

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
            startWithSystem = false,
            compactMode = false,
            theme = "light",
            alertsEnabled = true,
            alertFiveHourRemainingPercent = 20,
            alertLongWindowRemainingPercent = 30,
            codexSessionsPath = "~/.codex/sessions",
            codexAuthPath = "~/.codex/auth.json",
            claudeProjectsPath = "~/.claude/projects",
            claudeCredentialsPath = "~/.claude/.credentials.json",
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

    public static MonitorConfig LoadOrCreate(IAppPaths paths)
    {
        Directory.CreateDirectory(paths.AppDataDirectory);

        var configPath = ResolveConfigPath(paths);
        if (!File.Exists(configPath))
        {
            var created = Default();
            File.WriteAllText(paths.ConfigPath, JsonUtil.SerializeIndented(created));
            return created;
        }

        try
        {
            var raw = File.ReadAllText(configPath);
            var config = JsonUtil.Deserialize<MonitorConfig>(raw) ?? Default();
            ApplyLegacyStartupSetting(config, raw);
            ApplyMissingDefaults(config, raw);

            if (!string.Equals(configPath, paths.ConfigPath, StringComparison.OrdinalIgnoreCase) &&
                !File.Exists(paths.ConfigPath))
            {
                File.WriteAllText(paths.ConfigPath, JsonUtil.SerializeIndented(config));
            }

            return config;
        }
        catch
        {
            return Default();
        }
    }

    public void Save(IAppPaths paths)
    {
        Directory.CreateDirectory(paths.AppDataDirectory);
        File.WriteAllText(paths.ConfigPath, JsonUtil.SerializeIndented(this));
    }

    public string ExpandedCodexSessionsPath(IAppPaths paths)
    {
        return PathExpander.Expand(codexSessionsPath, paths);
    }

    public string ExpandedCodexAuthPath(IAppPaths paths)
    {
        return PathExpander.Expand(codexAuthPath, paths);
    }

    public string ExpandedClaudeProjectsPath(IAppPaths paths)
    {
        return PathExpander.Expand(claudeProjectsPath, paths);
    }

    public string ExpandedClaudeCredentialsPath(IAppPaths paths)
    {
        return PathExpander.Expand(claudeCredentialsPath, paths);
    }

    public static string NormalizeTheme(string value)
    {
        return string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
    }

    private static string ResolveConfigPath(IAppPaths paths)
    {
        if (File.Exists(paths.ConfigPath))
        {
            return paths.ConfigPath;
        }

        var legacyConfigPath = Path.Combine(paths.LegacyBaseDirectory, "quota-monitor.config.json");
        return File.Exists(legacyConfigPath) ? legacyConfigPath : paths.ConfigPath;
    }

    private static void ApplyMissingDefaults(MonitorConfig config, string raw)
    {
        var defaults = Default();

        if (!raw.Contains("\"showCodex\"", StringComparison.Ordinal))
        {
            config.showCodex = defaults.showCodex;
        }
        if (!raw.Contains("\"showClaude\"", StringComparison.Ordinal))
        {
            config.showClaude = defaults.showClaude;
        }
        if (!raw.Contains("\"minimizeToTray\"", StringComparison.Ordinal))
        {
            config.minimizeToTray = defaults.minimizeToTray;
        }
        if (!raw.Contains("\"startWithSystem\"", StringComparison.Ordinal) &&
            !raw.Contains("\"startWithWindows\"", StringComparison.Ordinal))
        {
            config.startWithSystem = defaults.startWithSystem;
        }
        if (!raw.Contains("\"compactMode\"", StringComparison.Ordinal))
        {
            config.compactMode = defaults.compactMode;
        }
        if (!raw.Contains("\"theme\"", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(config.theme))
        {
            config.theme = defaults.theme;
        }
        if (!raw.Contains("\"alertsEnabled\"", StringComparison.Ordinal))
        {
            config.alertsEnabled = defaults.alertsEnabled;
        }
        if (!raw.Contains("\"alertFiveHourRemainingPercent\"", StringComparison.Ordinal))
        {
            config.alertFiveHourRemainingPercent = defaults.alertFiveHourRemainingPercent;
        }
        if (!raw.Contains("\"alertLongWindowRemainingPercent\"", StringComparison.Ordinal))
        {
            config.alertLongWindowRemainingPercent = defaults.alertLongWindowRemainingPercent;
        }
        if (string.IsNullOrWhiteSpace(config.codexSessionsPath))
        {
            config.codexSessionsPath = defaults.codexSessionsPath;
        }
        if (string.IsNullOrWhiteSpace(config.codexAuthPath))
        {
            config.codexAuthPath = defaults.codexAuthPath;
        }
        if (string.IsNullOrWhiteSpace(config.claudeProjectsPath))
        {
            config.claudeProjectsPath = defaults.claudeProjectsPath;
        }
        if (string.IsNullOrWhiteSpace(config.claudeCredentialsPath))
        {
            config.claudeCredentialsPath = defaults.claudeCredentialsPath;
        }

        config.pollIntervalSeconds = Math.Max(3, config.pollIntervalSeconds);
        config.realtimeApiTimeoutSeconds = Math.Max(3, config.realtimeApiTimeoutSeconds);
        config.alertFiveHourRemainingPercent = ClampPercent(config.alertFiveHourRemainingPercent);
        config.alertLongWindowRemainingPercent = ClampPercent(config.alertLongWindowRemainingPercent);
        config.theme = NormalizeTheme(config.theme);
    }

    private static void ApplyLegacyStartupSetting(MonitorConfig config, string raw)
    {
        if (raw.Contains("\"startWithSystem\"", StringComparison.Ordinal) ||
            !raw.Contains("\"startWithWindows\"", StringComparison.Ordinal))
        {
            return;
        }

        var dict = JsonUtil.ParseObject(raw);
        var legacy = JsonUtil.Value(dict, "startWithWindows");
        if (legacy is bool enabled)
        {
            config.startWithSystem = enabled;
            return;
        }

        if (bool.TryParse(Convert.ToString(legacy), out var parsed))
        {
            config.startWithSystem = parsed;
        }
    }

    private static double ClampPercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Max(0, Math.Min(100, value));
    }
}
