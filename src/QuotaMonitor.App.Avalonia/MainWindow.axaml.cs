using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using QuotaMonitor.Core.Config;
using QuotaMonitor.Core.Infrastructure;
using QuotaMonitor.Core.Models;
using QuotaMonitor.Core.Services;

namespace QuotaMonitor.App.Avalonia;

public partial class MainWindow : Window
{
    private enum ChartMode
    {
        Pace,
        History
    }

    private readonly IAppPaths _paths;
    private readonly AppLog _log;
    private readonly QuotaReader _reader;
    private readonly UsageHistoryStore _historyStore;
    private readonly AlertEvaluator _alertEvaluator = new();
    private readonly IClassicDesktopStyleApplicationLifetime? _desktop;
    private readonly DispatcherTimer _timer;
    private MonitorConfig _config;
    private UiPalette _palette = UiPalette.FromThemeName("light");
    private WindowNotificationManager? _notificationManager;
    private bool _refreshInProgress;
    private bool _allowClose;
    private bool _hiddenToTray;
    private string _lastDiagnosticsText = "No refresh completed yet.";
    private string _lastTrayTooltipText = "Quota Monitor";
    private ChartMode _chartMode = ChartMode.Pace;
    private HistoryAggregation _historyAggregation = HistoryAggregation.Day;

    private readonly TextBlock _statusText = new();
    private readonly TextBlock _configPathText = new();
    private readonly TextBlock _titleText = new();
    private readonly Button _refreshButton = new();
    private readonly Button _settingsButton = new();
    private readonly Button _diagnosticsButton = new();
    private readonly Button _openConfigButton = new();
    private readonly Button _openDataButton = new();
    private readonly Button _paceModeButton = new();
    private readonly Button _historyModeButton = new();
    private readonly Button _dayHistoryButton = new();
    private readonly Button _weekHistoryButton = new();
    private readonly Button _monthHistoryButton = new();
    private readonly StackPanel _historyRangePanel = new();
    private readonly ServicePanel _codexPanel = new("Codex", "Week");
    private readonly ServicePanel _claudePanel = new("Claude", "7d");
    private Grid? _rootGrid;
    private StackPanel? _chartToolbar;
    private Grid? _columns;
    private TrayIcon? _trayIcon;

    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(IClassicDesktopStyleApplicationLifetime? desktop)
    {
        InitializeComponent();

        _desktop = desktop;
        _paths = DefaultAppPaths.Create();
        _log = new AppLog(_paths);
        _reader = new QuotaReader(_paths);
        _historyStore = new UsageHistoryStore(_paths);
        _config = MonitorConfig.LoadOrCreate(_paths);
        _palette = UiPalette.FromConfig(_config);
        ApplyApplicationTheme();
        Topmost = _config.alwaysOnTop;
        ApplyWindowIcon();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(3, _config.pollIntervalSeconds))
        };
        _timer.Tick += async (_, _) => await RefreshSnapshotAsync();

        Content = BuildContent();
        try
        {
            _notificationManager = new WindowNotificationManager(this)
            {
                Position = NotificationPosition.TopRight,
                MaxItems = 3
            };
        }
        catch (Exception ex)
        {
            _log.Write("notification manager init error: " + ex);
            CrashReporter.Write("Notification manager init error", ex);
        }

        InitializeTrayIcon();
        Opened += async (_, _) =>
        {
            try
            {
                _hiddenToTray = false;
                _timer.Start();
                PositionAtTopRightIfNeeded();
                await RefreshSnapshotAsync();
            }
            catch (Exception ex)
            {
                _log.Write("opened handler error: " + ex);
                CrashReporter.Write("Opened handler error", ex);
            }
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _trayIcon?.Dispose();
        };
    }

    private Control BuildContent()
    {
        Background = _palette.Page;
        _statusText.Text = "Ready";
        _statusText.Foreground = _palette.Muted;
        _statusText.VerticalAlignment = VerticalAlignment.Center;

        _configPathText.Text = _paths.ConfigPath;
        _configPathText.Foreground = _palette.Muted;
        _configPathText.FontSize = 12;
        _configPathText.TextTrimming = TextTrimming.CharacterEllipsis;

        _refreshButton.Content = "Refresh";
        _refreshButton.MinWidth = 108;
        _refreshButton.Height = 34;
        _refreshButton.Click += async (_, _) => await RefreshSnapshotAsync();

        ConfigureHeaderButton(_settingsButton, "Settings");
        _settingsButton.Click += async (_, _) => await ShowSettingsAsync();

        ConfigureHeaderButton(_diagnosticsButton, "Diagnostics");
        _diagnosticsButton.Click += (_, _) => ShowDiagnostics();

        ConfigureHeaderButton(_openConfigButton, "Open Config");
        _openConfigButton.Click += (_, _) => OpenPath(_paths.ConfigPath);

        ConfigureHeaderButton(_openDataButton, "Open Data");
        _openDataButton.Click += (_, _) => OpenPath(_paths.AppDataDirectory);

        ConfigureToggleButton(_paceModeButton, "Pace", 74);
        _paceModeButton.Click += (_, _) => SetChartMode(ChartMode.Pace);

        ConfigureToggleButton(_historyModeButton, "History", 86);
        _historyModeButton.Click += (_, _) => SetChartMode(ChartMode.History);

        ConfigureToggleButton(_dayHistoryButton, "Day", 64);
        _dayHistoryButton.Click += (_, _) => SetHistoryAggregation(HistoryAggregation.Day);

        ConfigureToggleButton(_weekHistoryButton, "Week", 70);
        _weekHistoryButton.Click += (_, _) => SetHistoryAggregation(HistoryAggregation.Week);

        ConfigureToggleButton(_monthHistoryButton, "Month", 78);
        _monthHistoryButton.Click += (_, _) => SetHistoryAggregation(HistoryAggregation.Month);

        _historyRangePanel.Orientation = Orientation.Horizontal;
        _historyRangePanel.Spacing = 6;
        _historyRangePanel.Children.Add(_dayHistoryButton);
        _historyRangePanel.Children.Add(_weekHistoryButton);
        _historyRangePanel.Children.Add(_monthHistoryButton);

        _titleText.Text = "Quota Monitor";
        _titleText.FontSize = 24;
        _titleText.FontWeight = FontWeight.SemiBold;
        _titleText.Foreground = _palette.Text;
        _titleText.VerticalAlignment = VerticalAlignment.Center;

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 12)
        };
        header.Children.Add(_titleText);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                _openDataButton,
                _openConfigButton,
                _diagnosticsButton,
                _settingsButton,
                _refreshButton
            }
        };
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        _chartToolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                _paceModeButton,
                _historyModeButton,
                _historyRangePanel
            }
        };

        _columns = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 12
        };
        _columns.Children.Add(_codexPanel.Root);
        Grid.SetColumn(_claudePanel.Root, 1);
        _columns.Children.Add(_claudePanel.Root);

        var bottom = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 12, 0, 0)
        };
        bottom.Children.Add(_statusText);
        Grid.SetColumn(_configPathText, 1);
        bottom.Children.Add(_configPathText);

        _rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(18)
        };
        _rootGrid.ContextMenu = BuildContextMenu();
        _rootGrid.Children.Add(header);
        Grid.SetRow(_chartToolbar, 1);
        _rootGrid.Children.Add(_chartToolbar);
        var scroll = new ScrollViewer
        {
            Content = _columns,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 2);
        _rootGrid.Children.Add(scroll);
        Grid.SetRow(bottom, 3);
        _rootGrid.Children.Add(bottom);

        ApplyServiceVisibility();
        ApplyWindowPreferences();
        ApplyTheme();
        ApplyChartMode();
        return _rootGrid;
    }

    public void AllowShutdown()
    {
        _allowClose = true;
    }

    private void ApplyApplicationTheme()
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = _palette.ThemeVariant;
        }

        RequestedThemeVariant = _palette.ThemeVariant;
    }

    private void ApplyTheme()
    {
        ApplyApplicationTheme();
        Background = _palette.Page;
        _titleText.Foreground = _palette.Text;
        _configPathText.Foreground = _palette.Muted;
        if (_statusText.Foreground != _palette.Warning && _statusText.Foreground != _palette.Error)
        {
            _statusText.Foreground = _palette.Muted;
        }

        _codexPanel.ApplyTheme(_palette);
        _claudePanel.ApplyTheme(_palette);
        ApplyChartMode();
    }

    private void ApplyWindowIcon()
    {
        var icon = TryLoadWindowIcon();
        if (icon != null)
        {
            Icon = icon;
        }
    }

    private void InitializeTrayIcon()
    {
        try
        {
            if (Application.Current == null)
            {
                return;
            }

            var trayIcon = new TrayIcon
            {
                ToolTipText = "Quota Monitor",
                IsVisible = _config.minimizeToTray,
                Menu = BuildTrayMenu()
            };

            var icon = TryLoadWindowIcon();
            if (icon != null)
            {
                trayIcon.Icon = icon;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                MacOSProperties.SetIsTemplateIcon(trayIcon, false);
            }

            trayIcon.Clicked += (_, _) => ShowWindowFromTray();

            var icons = TrayIcon.GetIcons(Application.Current) ?? new TrayIcons();
            icons.Add(trayIcon);
            TrayIcon.SetIcons(Application.Current, icons);
            _trayIcon = trayIcon;
        }
        catch (Exception ex)
        {
            _log.Write("tray init error: " + ex);
            CrashReporter.Write("Tray init error", ex);
            _trayIcon = null;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || !_config.minimizeToTray)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void ShowWindowFromTray()
    {
        _hiddenToTray = false;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        RefreshTrayMenu();
    }

    private void ShowDiagnosticsFromTray()
    {
        ShowWindowFromTray();
        ShowDiagnostics();
    }

    private async Task ShowSettingsFromTrayAsync()
    {
        ShowWindowFromTray();
        await ShowSettingsAsync();
    }

    private void HideToTray()
    {
        if (!_config.minimizeToTray)
        {
            WindowState = WindowState.Minimized;
            return;
        }

        _hiddenToTray = true;
        Hide();
        RefreshTrayMenu();
    }

    private void RequestQuit()
    {
        _allowClose = true;
        _trayIcon?.Dispose();
        _trayIcon = null;

        if (_desktop != null)
        {
            _desktop.TryShutdown(0);
            return;
        }

        Close();
    }

    private void PositionAtTopRightIfNeeded()
    {
        if (!_config.startAtTopRight)
        {
            return;
        }

        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen == null)
            {
                return;
            }

            var workingArea = screen.WorkingArea;
            var scaling = Math.Max(1, screen.Scaling);
            var width = (int)Math.Ceiling(Math.Max(Bounds.Width, Width) * scaling);
            var height = (int)Math.Ceiling(Math.Max(Bounds.Height, Height) * scaling);
            var margin = (int)Math.Ceiling(16 * scaling);
            var x = workingArea.X + workingArea.Width - width - margin;
            var y = workingArea.Y + margin;
            var maxY = workingArea.Y + Math.Max(0, workingArea.Height - height - margin);
            Position = new PixelPoint(Math.Max(workingArea.X + margin, x), Math.Min(y, maxY));
        }
        catch (Exception ex)
        {
            _log.Write("position window error: " + ex);
            CrashReporter.Write("Position window error", ex);
        }
    }

    private NativeMenu BuildTrayMenu()
    {
        var menu = new NativeMenu();
        menu.Add(new NativeMenuItem("Quota Monitor")
        {
            IsEnabled = false
        });
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(BuildNativeMenuItem(_hiddenToTray ? "Show Window" : "Hide Window", _hiddenToTray ? ShowWindowFromTray : HideToTray));
        menu.Add(BuildNativeMenuItem("Refresh", async () => await RefreshSnapshotAsync()));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(BuildNativeCheckMenuItem("Show Codex", _config.CodexVisible, () => SetServiceVisible("Codex", !_config.CodexVisible)));
        menu.Add(BuildNativeCheckMenuItem("Show Claude", _config.ClaudeVisible, () => SetServiceVisible("Claude", !_config.ClaudeVisible)));
        menu.Add(BuildNativeCheckMenuItem("Compact Mode", _config.compactMode, () => SetCompactModeEnabled(!_config.compactMode)));
        menu.Add(BuildNativeCheckMenuItem("Always On Top", _config.alwaysOnTop, () => SetAlwaysOnTopEnabled(!_config.alwaysOnTop)));
        menu.Add(BuildNativeRadioMenuItem("Light Theme", !_palette.IsDark, () => SetTheme("light")));
        menu.Add(BuildNativeRadioMenuItem("Dark Theme", _palette.IsDark, () => SetTheme("dark")));
        menu.Add(BuildNativeCheckMenuItem("Minimize To Tray", _config.minimizeToTray, () => SetMinimizeToTrayEnabled(!_config.minimizeToTray)));
        menu.Add(BuildNativeCheckMenuItem("Start With System", _config.startWithSystem, () => SetStartWithSystemEnabled(!_config.startWithSystem)));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(BuildNativeRadioMenuItem("Pace Graph", _chartMode == ChartMode.Pace, () => SetChartMode(ChartMode.Pace)));
        menu.Add(BuildNativeRadioMenuItem("Usage History", _chartMode == ChartMode.History, () => SetChartMode(ChartMode.History)));
        menu.Add(BuildHistoryRangeMenu());
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(BuildNativeMenuItem("Diagnostics", ShowDiagnosticsFromTray));
        menu.Add(BuildNativeMenuItem("Settings", async () => await ShowSettingsFromTrayAsync()));
        menu.Add(BuildNativeMenuItem("Open Config", () => OpenPath(_paths.ConfigPath)));
        menu.Add(BuildNativeMenuItem("Open Data", () => OpenPath(_paths.AppDataDirectory)));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(BuildNativeMenuItem("Quit", RequestQuit));
        return menu;
    }

    private NativeMenuItem BuildHistoryRangeMenu()
    {
        var submenu = new NativeMenu();
        submenu.Add(BuildNativeRadioMenuItem("Day", _historyAggregation == HistoryAggregation.Day, () => SetHistoryAggregation(HistoryAggregation.Day)));
        submenu.Add(BuildNativeRadioMenuItem("Week", _historyAggregation == HistoryAggregation.Week, () => SetHistoryAggregation(HistoryAggregation.Week)));
        submenu.Add(BuildNativeRadioMenuItem("Month", _historyAggregation == HistoryAggregation.Month, () => SetHistoryAggregation(HistoryAggregation.Month)));
        return new NativeMenuItem("History Range")
        {
            Menu = submenu
        };
    }

    private static NativeMenuItem BuildNativeMenuItem(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    private static NativeMenuItem BuildNativeMenuItem(string header, Func<Task> action)
    {
        var item = new NativeMenuItem(header);
        item.Click += async (_, _) => await action();
        return item;
    }

    private static NativeMenuItem BuildNativeCheckMenuItem(string header, bool isChecked, Action action)
    {
        var item = BuildNativeMenuItem(header, action);
        item.ToggleType = MenuItemToggleType.CheckBox;
        item.IsChecked = isChecked;
        return item;
    }

    private static NativeMenuItem BuildNativeRadioMenuItem(string header, bool isChecked, Action action)
    {
        var item = BuildNativeMenuItem(header, action);
        item.ToggleType = MenuItemToggleType.Radio;
        item.IsChecked = isChecked;
        return item;
    }

    private void RefreshTrayMenu()
    {
        if (_trayIcon == null)
        {
            return;
        }

        _trayIcon.ToolTipText = _lastTrayTooltipText;
        _trayIcon.IsVisible = _config.minimizeToTray;
        _trayIcon.Menu = BuildTrayMenu();
    }

    private static WindowIcon? TryLoadWindowIcon()
    {
        foreach (var path in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "quota-monitor.ico"),
            Path.Combine(AppContext.BaseDirectory, "quota-monitor.png"),
            Path.Combine(AppContext.BaseDirectory, "assets", "quota-monitor.ico"),
            Path.Combine(AppContext.BaseDirectory, "assets", "quota-monitor.png")
        })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                return new WindowIcon(path);
            }
            catch
            {
                // Keep the app usable if a platform rejects the icon format.
            }
        }

        return null;
    }

    private async Task RefreshSnapshotAsync()
    {
        if (_refreshInProgress)
        {
            return;
        }

        _refreshInProgress = true;
        _refreshButton.IsEnabled = false;
        _statusText.Text = "Loading...";
        _log.Write("avalonia refresh begin");

        try
        {
            var snapshot = await Task.Run(() => _reader.Read(_config));
            _historyStore.AppendSnapshot(snapshot);
            Render(snapshot);
            _log.Write("avalonia refresh ok");
        }
        catch (Exception ex)
        {
            _log.Write("avalonia refresh error: " + ex);
            _lastDiagnosticsText = "Refresh failed:" + Environment.NewLine + ex;
            _statusText.Text = ex.Message;
            _statusText.Foreground = _palette.Error;
            _lastTrayTooltipText = TrimTrayTooltip("Quota Monitor - " + ex.Message);
            RefreshTrayMenu();
        }
        finally
        {
            _refreshButton.IsEnabled = true;
            _refreshInProgress = false;
        }
    }

    private void Render(QuotaSnapshot snapshot)
    {
        RenderCodex(snapshot.Codex);
        RenderClaude(snapshot.Claude);
        RenderCharts(snapshot);

        var alerts = _alertEvaluator.Evaluate(_config, snapshot);
        _lastDiagnosticsText = BuildDiagnostics(snapshot, alerts.Summary);
        UpdateTrayTooltip(snapshot);
        ShowAlertNotifications(alerts);
        if (!string.IsNullOrWhiteSpace(alerts.Summary))
        {
            _statusText.Text = "Alert: " + alerts.Summary;
            _statusText.Foreground = _palette.Warning;
            RefreshTrayMenu();
            return;
        }

        var hasIssue = HasServiceIssue(snapshot);
        _statusText.Text = BuildStatusLine(snapshot, hasIssue);
        _statusText.Foreground = hasIssue ? _palette.Warning : _palette.Muted;
        RefreshTrayMenu();
    }

    private void ShowAlertNotifications(AlertEvaluation alerts)
    {
        if (alerts.NewAlerts.Count == 0)
        {
            return;
        }

        var message = string.Join(Environment.NewLine, alerts.NewAlerts.Take(3));
        if (alerts.NewAlerts.Count > 3)
        {
            message += Environment.NewLine + "...";
        }

        _notificationManager?.Show(new Notification(
            "Quota alert",
            message,
            NotificationType.Warning,
            TimeSpan.FromSeconds(10),
            onClick: ShowWindowFromTray));

        if (_hiddenToTray)
        {
            PlatformNotifier.Show("Quota Monitor", message);
        }
    }

    private async Task ShowSettingsAsync()
    {
        var dialog = new SettingsWindow(_config, _palette);
        var result = await dialog.ShowDialog<bool?>(this);
        if (result != true)
        {
            return;
        }

        dialog.ApplyTo(_config);
        _config.theme = MonitorConfig.NormalizeTheme(_config.theme);
        _palette = UiPalette.FromConfig(_config);
        var startupResult = ApplyStartupPreference();
        _config.Save(_paths);
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(3, _config.pollIntervalSeconds));
        ApplyServiceVisibility();
        ApplyTheme();
        ApplyWindowPreferences();
        ApplyChartMode();
        RefreshContextMenu();
        RefreshTrayMenu();
        _statusText.Text = startupResult.Success ? "Settings saved." : "Settings saved. Startup: " + startupResult.Message;
        await RefreshSnapshotAsync();
    }

    private void ShowDiagnostics()
    {
        var dialog = new DiagnosticsWindow(_lastDiagnosticsText, _palette);
        dialog.Show(this);
    }

    private void OpenPath(string path)
    {
        try
        {
            if (string.Equals(path, _paths.AppDataDirectory, StringComparison.Ordinal))
            {
                Directory.CreateDirectory(_paths.AppDataDirectory);
            }

            PlatformFileLauncher.OpenPath(path);
        }
        catch (Exception ex)
        {
            _log.Write("open path error: " + ex);
            _statusText.Text = "Open failed: " + ex.Message;
            _statusText.Foreground = _palette.Error;
        }
    }

    private ContextMenu BuildContextMenu()
    {
        var historyRange = new MenuItem
        {
            Header = "History Range",
            ItemsSource = new object[]
            {
                BuildRadioMenuItem("Day", _historyAggregation == HistoryAggregation.Day, "history-range", () => SetHistoryAggregation(HistoryAggregation.Day)),
                BuildRadioMenuItem("Week", _historyAggregation == HistoryAggregation.Week, "history-range", () => SetHistoryAggregation(HistoryAggregation.Week)),
                BuildRadioMenuItem("Month", _historyAggregation == HistoryAggregation.Month, "history-range", () => SetHistoryAggregation(HistoryAggregation.Month))
            }
        };

        return new ContextMenu
        {
            ItemsSource = new object[]
            {
                BuildMenuItem("Refresh", async () => await RefreshSnapshotAsync()),
                new Separator(),
                BuildCheckMenuItem("Show Codex", _config.CodexVisible, () => SetServiceVisible("Codex", !_config.CodexVisible)),
                BuildCheckMenuItem("Show Claude", _config.ClaudeVisible, () => SetServiceVisible("Claude", !_config.ClaudeVisible)),
                BuildCheckMenuItem("Compact Mode", _config.compactMode, () => SetCompactModeEnabled(!_config.compactMode)),
                BuildCheckMenuItem("Always On Top", _config.alwaysOnTop, () => SetAlwaysOnTopEnabled(!_config.alwaysOnTop)),
                BuildRadioMenuItem("Light Theme", !_palette.IsDark, "theme", () => SetTheme("light")),
                BuildRadioMenuItem("Dark Theme", _palette.IsDark, "theme", () => SetTheme("dark")),
                BuildCheckMenuItem("Minimize To Tray", _config.minimizeToTray, () => SetMinimizeToTrayEnabled(!_config.minimizeToTray)),
                BuildCheckMenuItem("Start With System", _config.startWithSystem, () => SetStartWithSystemEnabled(!_config.startWithSystem)),
                new Separator(),
                BuildRadioMenuItem("Pace Graph", _chartMode == ChartMode.Pace, "chart-mode", () => SetChartMode(ChartMode.Pace)),
                BuildRadioMenuItem("Usage History", _chartMode == ChartMode.History, "chart-mode", () => SetChartMode(ChartMode.History)),
                historyRange,
                new Separator(),
                BuildMenuItem("Diagnostics", ShowDiagnostics),
                BuildMenuItem("Settings", async () => await ShowSettingsAsync()),
                BuildMenuItem("Open Config", () => OpenPath(_paths.ConfigPath)),
                BuildMenuItem("Open Data", () => OpenPath(_paths.AppDataDirectory)),
                new Separator(),
                BuildMenuItem("Minimize", HideToTray),
                BuildMenuItem("Quit", RequestQuit)
            }
        };
    }

    private static MenuItem BuildMenuItem(string header, Action action)
    {
        var item = new MenuItem
        {
            Header = header
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem BuildMenuItem(string header, Func<Task> action)
    {
        var item = new MenuItem
        {
            Header = header
        };
        item.Click += async (_, _) => await action();
        return item;
    }

    private static MenuItem BuildCheckMenuItem(string header, bool isChecked, Action action)
    {
        var item = BuildMenuItem(header, action);
        item.ToggleType = MenuItemToggleType.CheckBox;
        item.IsChecked = isChecked;
        return item;
    }

    private static MenuItem BuildRadioMenuItem(string header, bool isChecked, string groupName, Action action)
    {
        var item = BuildMenuItem(header, action);
        item.ToggleType = MenuItemToggleType.Radio;
        item.GroupName = groupName;
        item.IsChecked = isChecked;
        return item;
    }

    private void RefreshContextMenu()
    {
        if (_rootGrid != null)
        {
            _rootGrid.ContextMenu = BuildContextMenu();
        }
    }

    private void SetServiceVisible(string serviceName, bool visible)
    {
        if (string.Equals(serviceName, "Codex", StringComparison.Ordinal))
        {
            if (!visible && !_config.showClaude)
            {
                _statusText.Text = "At least one service must stay visible.";
                return;
            }

            _config.showCodex = visible;
        }
        else
        {
            if (!visible && !_config.showCodex)
            {
                _statusText.Text = "At least one service must stay visible.";
                return;
            }

            _config.showClaude = visible;
        }

        ApplyServiceVisibility();
        ApplyWindowPreferences();
        ApplyChartMode();
        SaveConfig();
        RefreshContextMenu();
        RefreshTrayMenu();
    }

    private void SetCompactModeEnabled(bool enabled)
    {
        _config.compactMode = enabled;
        ApplyWindowPreferences();
        ApplyChartMode();
        SaveConfig();
        RefreshContextMenu();
        RefreshTrayMenu();
        _statusText.Text = enabled ? "Compact mode enabled." : "Compact mode disabled.";
    }

    private void SetAlwaysOnTopEnabled(bool enabled)
    {
        _config.alwaysOnTop = enabled;
        ApplyWindowPreferences();
        SaveConfig();
        RefreshContextMenu();
        RefreshTrayMenu();
        _statusText.Text = enabled ? "Always on top enabled." : "Always on top disabled.";
    }

    private void SetTheme(string theme)
    {
        _config.theme = MonitorConfig.NormalizeTheme(theme);
        _palette = UiPalette.FromConfig(_config);
        ApplyTheme();
        SaveConfig();
        RefreshContextMenu();
        RefreshTrayMenu();
        _statusText.Text = _palette.IsDark ? "Dark theme enabled." : "Light theme enabled.";
        _statusText.Foreground = _palette.Muted;
    }

    private void SetMinimizeToTrayEnabled(bool enabled)
    {
        _config.minimizeToTray = enabled;
        if (!enabled && _hiddenToTray)
        {
            ShowWindowFromTray();
        }

        SaveConfig();
        RefreshContextMenu();
        RefreshTrayMenu();
        _statusText.Text = enabled ? "Tray/menu bar mode enabled." : "Tray/menu bar mode disabled.";
    }

    private void SetStartWithSystemEnabled(bool enabled)
    {
        _config.startWithSystem = enabled;
        var result = ApplyStartupPreference();
        SaveConfig();
        RefreshContextMenu();
        RefreshTrayMenu();
        _statusText.Text = result.Success ? result.Message : "Start with system failed: " + result.Message;
        _statusText.Foreground = result.Success ? _palette.Muted : _palette.Error;
    }

    private StartupRegistrationResult ApplyStartupPreference()
    {
        var result = StartupRegistration.Apply(_config.startWithSystem);
        if (!result.Success)
        {
            _log.Write("startup registration error: " + result.Message);
        }

        return result;
    }

    private void ApplyServiceVisibility()
    {
        if (!_config.showCodex && !_config.showClaude)
        {
            _config.showCodex = true;
        }

        _codexPanel.Root.IsVisible = _config.CodexVisible;
        _claudePanel.Root.IsVisible = _config.ClaudeVisible;

        if (_columns == null)
        {
            return;
        }

        var bothVisible = _config.CodexVisible && _config.ClaudeVisible;
        _columns.ColumnDefinitions = new ColumnDefinitions(bothVisible ? "*,*" : "*");
        _columns.ColumnSpacing = bothVisible ? 12 : 0;
        Grid.SetColumn(_codexPanel.Root, 0);
        Grid.SetColumn(_claudePanel.Root, _config.CodexVisible ? 1 : 0);
    }

    private void ApplyWindowPreferences()
    {
        Topmost = _config.alwaysOnTop;
        ApplyCompactMode();
        if (IsVisible)
        {
            PositionAtTopRightIfNeeded();
        }
    }

    private void ApplyCompactMode()
    {
        var compact = _config.compactMode;
        if (_chartToolbar != null)
        {
            _chartToolbar.IsVisible = !compact;
        }

        _codexPanel.SetCompactMode(compact);
        _claudePanel.SetCompactMode(compact);

        MinWidth = compact ? 460 : 720;
        MinHeight = compact ? 340 : 600;
        if (compact)
        {
            Width = Math.Min(Width, _config.CodexVisible && _config.ClaudeVisible ? 700 : 500);
            Height = Math.Min(Height, 430);
            return;
        }

        Width = Math.Max(Width, 900);
        Height = Math.Max(Height, 640);
    }

    private bool SaveConfig()
    {
        try
        {
            _config.Save(_paths);
            return true;
        }
        catch (Exception ex)
        {
            _log.Write("save config error: " + ex);
            _statusText.Text = "Save config failed: " + ex.Message;
            _statusText.Foreground = _palette.Error;
            return false;
        }
    }

    private void RenderCharts(QuotaSnapshot snapshot)
    {
        var codexSamples = snapshot.Codex.Secondary?.ResetsAt.HasValue == true
            ? _historyStore.Load("Codex", "Week", snapshot.Codex.Secondary.ResetsAt.Value)
            : new List<UsageSample>();
        _codexPanel.SetPaceData("Codex Week pace", snapshot.Codex.Secondary, codexSamples);

        var claudeSamples = snapshot.Claude.RealtimeWeek?.ResetsAt.HasValue == true
            ? _historyStore.Load("Claude", "7d", snapshot.Claude.RealtimeWeek.ResetsAt.Value)
            : new List<UsageSample>();
        _claudePanel.SetPaceData("Claude 7d pace", snapshot.Claude.RealtimeWeek, claudeSamples);

        _codexPanel.SetHistoryData(
            "Codex Week usage",
            _historyAggregation,
            _historyStore.LoadUsageHistory("Codex", "Week", _historyAggregation));
        _claudePanel.SetHistoryData(
            "Claude 7d usage",
            _historyAggregation,
            _historyStore.LoadUsageHistory("Claude", "7d", _historyAggregation));
    }

    private void SetChartMode(ChartMode mode)
    {
        _chartMode = mode;
        ApplyChartMode();
        RefreshContextMenu();
        RefreshTrayMenu();
    }

    private void SetHistoryAggregation(HistoryAggregation aggregation)
    {
        _historyAggregation = aggregation;
        _chartMode = ChartMode.History;
        ApplyChartMode();
        _codexPanel.SetHistoryData(
            "Codex Week usage",
            _historyAggregation,
            _historyStore.LoadUsageHistory("Codex", "Week", _historyAggregation));
        _claudePanel.SetHistoryData(
            "Claude 7d usage",
            _historyAggregation,
            _historyStore.LoadUsageHistory("Claude", "7d", _historyAggregation));
        RefreshContextMenu();
        RefreshTrayMenu();
    }

    private void ApplyChartMode()
    {
        var showHistory = _chartMode == ChartMode.History;
        _codexPanel.SetChartMode(showHistory);
        _claudePanel.SetChartMode(showHistory);
        _historyRangePanel.IsVisible = showHistory && !_config.compactMode;
        StyleToggleButton(_paceModeButton, !showHistory);
        StyleToggleButton(_historyModeButton, showHistory);
        StyleToggleButton(_dayHistoryButton, showHistory && _historyAggregation == HistoryAggregation.Day);
        StyleToggleButton(_weekHistoryButton, showHistory && _historyAggregation == HistoryAggregation.Week);
        StyleToggleButton(_monthHistoryButton, showHistory && _historyAggregation == HistoryAggregation.Month);
    }

    private static void ConfigureHeaderButton(Button button, string text)
    {
        button.Content = text;
        button.Height = 34;
        button.MinWidth = 96;
    }

    private static void ConfigureToggleButton(Button button, string text, double minWidth)
    {
        button.Content = text;
        button.Height = 32;
        button.MinWidth = minWidth;
    }

    private void StyleToggleButton(Button button, bool selected)
    {
        button.Background = selected ? _palette.Selected : _palette.Panel;
        button.Foreground = selected ? _palette.SelectedText : _palette.Text;
        button.BorderBrush = selected ? _palette.SelectedBorder : _palette.Border;
    }

    private void RenderCodex(CodexSnapshot codex)
    {
        _codexPanel.SetPlan("Plan: " + FormatPlan(codex.PlanType));
        if (!codex.Available)
        {
            _codexPanel.SetPrimary("5h", "No data: " + (codex.Error ?? string.Empty), null);
            _codexPanel.SetLongWindow("Week", "No data", null);
            return;
        }

        _codexPanel.SetPrimary(
            FormatRemaining(codex.Primary, "5h"),
            "reset " + FormatReset(codex.Primary?.ResetsAt) + " | used " + FormatUsedPercent(codex.Primary),
            codex.Primary?.RemainingPercent);
        _codexPanel.SetLongWindow(
            FormatRemaining(codex.Secondary, "Week"),
            "reset " + FormatReset(codex.Secondary?.ResetsAt) + " | used " + FormatUsedPercent(codex.Secondary),
            codex.Secondary?.RemainingPercent);
    }

    private void RenderClaude(ClaudeSnapshot claude)
    {
        _claudePanel.SetPlan("Plan: " + FormatPlan(claude.PlanType));
        if (!claude.Available)
        {
            _claudePanel.SetPrimary("5h estimate", "No data: " + (claude.Error ?? string.Empty), null);
            _claudePanel.SetLongWindow("7d estimate", "No data", null);
            return;
        }

        if (claude.RealtimeFiveHour != null || claude.RealtimeWeek != null)
        {
            _claudePanel.SetPrimary(
                FormatRemaining(claude.RealtimeFiveHour, "5h"),
                "reset " + FormatReset(claude.RealtimeFiveHour?.ResetsAt) + " | used " + FormatUsedPercent(claude.RealtimeFiveHour),
                claude.RealtimeFiveHour?.RemainingPercent);
            _claudePanel.SetLongWindow(
                FormatRemaining(claude.RealtimeWeek, "7d"),
                "reset " + FormatReset(claude.RealtimeWeek?.ResetsAt) + " | used " + FormatUsedPercent(claude.RealtimeWeek),
                claude.RealtimeWeek?.RemainingPercent);
            return;
        }

        if (claude.TokenBudget > 0 && claude.RemainingTokens.HasValue)
        {
            _claudePanel.SetPrimary(
                "5h est. left " + FormatTokens(claude.RemainingTokens.Value),
                "used " + claude.MessageCount + " msg, " + FormatTokens(claude.WeightedTokens) +
                " | reset~ " + FormatReset(claude.EstimatedResetAt),
                claude.RemainingTokenPercent);
        }
        else if (claude.MessageBudget > 0 && claude.RemainingMessages.HasValue)
        {
            _claudePanel.SetPrimary(
                string.Format(CultureInfo.InvariantCulture, "5h est. left {0}/{1} msg", claude.RemainingMessages.Value, claude.MessageBudget),
                "used " + claude.MessageCount + " msg, " + FormatTokens(claude.WeightedTokens) +
                " | reset~ " + FormatReset(claude.EstimatedResetAt),
                claude.RemainingMessagePercent);
        }
        else
        {
            _claudePanel.SetPrimary(
                "5h local usage",
                "used " + claude.MessageCount + " msg, " + FormatTokens(claude.WeightedTokens),
                null);
        }

        if (claude.WeeklyTokenBudget > 0 && claude.WeeklyRemainingTokens.HasValue)
        {
            _claudePanel.SetLongWindow(
                "Week est. left " + FormatTokens(claude.WeeklyRemainingTokens.Value),
                "used " + claude.WeeklyMessageCount + " msg, " + FormatTokens(claude.WeeklyWeightedTokens) +
                " | reset~ " + FormatReset(claude.EstimatedWeeklyResetAt),
                claude.WeeklyRemainingTokenPercent);
        }
        else if (claude.WeeklyMessageBudget > 0 && claude.WeeklyRemainingMessages.HasValue)
        {
            _claudePanel.SetLongWindow(
                string.Format(CultureInfo.InvariantCulture, "Week est. left {0}/{1} msg", claude.WeeklyRemainingMessages.Value, claude.WeeklyMessageBudget),
                "used " + claude.WeeklyMessageCount + " msg, " + FormatTokens(claude.WeeklyWeightedTokens) +
                " | reset~ " + FormatReset(claude.EstimatedWeeklyResetAt),
                claude.WeeklyRemainingMessagePercent);
        }
        else
        {
            _claudePanel.SetLongWindow(
                "7d local usage",
                "used " + claude.WeeklyMessageCount + " msg, " + FormatTokens(claude.WeeklyWeightedTokens),
                null);
        }
    }

    private string BuildStatusLine(QuotaSnapshot snapshot, bool hasIssue)
    {
        var parts = new[]
        {
            string.Format(CultureInfo.InvariantCulture, "Updated {0:HH:mm:ss}", snapshot.UpdatedAt.LocalDateTime),
            "Codex: " + ShortServiceState(snapshot.Codex.Available, snapshot.Codex.Source, snapshot.Codex.Error, snapshot.Codex.FallbackError),
            "Claude: " + ShortServiceState(snapshot.Claude.Available, snapshot.Claude.Source, snapshot.Claude.Error, snapshot.Claude.FallbackError),
            hasIssue ? "Diagnostics pending" : null
        };

        return string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private void UpdateTrayTooltip(QuotaSnapshot snapshot)
    {
        var parts = new List<string>();
        if (_config.CodexVisible && snapshot.Codex.Available)
        {
            parts.Add("Codex 5h " + FormatShortPercent(snapshot.Codex.Primary?.RemainingPercent) +
                " W " + FormatShortPercent(snapshot.Codex.Secondary?.RemainingPercent));
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

        var text = parts.Count == 0 ? "Quota Monitor" : "Quota: " + string.Join(" | ", parts);
        _lastTrayTooltipText = TrimTrayTooltip(text);
    }

    private string BuildDiagnostics(QuotaSnapshot snapshot, string? alertSummary)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "Last successful refresh: " + FormatLocalDateTime(snapshot.UpdatedAt),
            "Config path: " + _paths.ConfigPath,
            "Data directory: " + _paths.AppDataDirectory,
            "Refresh interval: " + _config.pollIntervalSeconds + "s",
            "Realtime API: " + (_config.useRealtimeApi ? "on" : "off"),
            "Alerts: " + (string.IsNullOrWhiteSpace(alertSummary) ? "none" : alertSummary),
            "",
            BuildCodexDiagnostics(snapshot.Codex),
            "",
            BuildClaudeDiagnostics(snapshot.Claude)
        });
    }

    private static string BuildCodexDiagnostics(CodexSnapshot codex)
    {
        var lines = new[]
        {
            "Codex",
            "  available: " + codex.Available,
            "  source: " + (codex.Source ?? string.Empty),
            "  plan: " + FormatPlan(codex.PlanType),
            "  5h remaining: " + FormatShortPercent(codex.Primary?.RemainingPercent),
            "  Week remaining: " + FormatShortPercent(codex.Secondary?.RemainingPercent),
            "  error: " + (codex.Error ?? string.Empty),
            "  realtime fallback error: " + (codex.FallbackError ?? string.Empty)
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildClaudeDiagnostics(ClaudeSnapshot claude)
    {
        var five = claude.RealtimeFiveHour == null
            ? claude.RemainingTokenPercent ?? claude.RemainingMessagePercent
            : claude.RealtimeFiveHour.RemainingPercent;
        var week = claude.RealtimeWeek == null
            ? claude.WeeklyRemainingTokenPercent ?? claude.WeeklyRemainingMessagePercent
            : claude.RealtimeWeek.RemainingPercent;
        var lines = new[]
        {
            "Claude",
            "  available: " + claude.Available,
            "  source: " + (claude.Source ?? string.Empty),
            "  plan: " + FormatPlan(claude.PlanType),
            "  5h remaining: " + FormatShortPercent(five),
            "  7d remaining: " + FormatShortPercent(week),
            "  error: " + (claude.Error ?? string.Empty),
            "  realtime fallback error: " + (claude.FallbackError ?? string.Empty)
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static bool HasServiceIssue(QuotaSnapshot snapshot)
    {
        return !snapshot.Codex.Available ||
            !string.IsNullOrWhiteSpace(snapshot.Codex.FallbackError) ||
            !snapshot.Claude.Available ||
            !string.IsNullOrWhiteSpace(snapshot.Claude.FallbackError);
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
        if (source.Contains("wham", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("oauth", StringComparison.OrdinalIgnoreCase))
        {
            return "realtime";
        }
        if (source.Contains("local", StringComparison.OrdinalIgnoreCase))
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
        if (error.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("credentials", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            return "login needed";
        }
        if (error.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "path missing";
        }

        return error.Length > 28 ? error[..25] + "..." : error;
    }

    private static string FormatRemaining(CodexWindow? window, string label)
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
        return normalized.Length == 0 ? "unknown" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private static string FormatUsedPercent(CodexWindow? window)
    {
        return window == null || !window.UsedPercent.HasValue
            ? "--"
            : string.Format(CultureInfo.InvariantCulture, "{0:0}%", window.UsedPercent.Value);
    }

    private static string FormatShortPercent(double? value)
    {
        return value.HasValue ? string.Format(CultureInfo.InvariantCulture, "{0:0}%", value.Value) : "--";
    }

    private static string TrimTrayTooltip(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Quota Monitor";
        }

        return text.Length > 63 ? text[..60] + "..." : text;
    }

    private static string FormatLocalDateTime(DateTimeOffset value)
    {
        return value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
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

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}d {1}h", (int)duration.TotalDays, duration.Hours);
        }
        if (duration.TotalHours >= 1)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}h {1}m", (int)duration.TotalHours, duration.Minutes);
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

    private sealed class ServicePanel
    {
        private readonly TextBlock _title = new();
        private readonly TextBlock _plan = new();
        private readonly WindowRow _primary = new();
        private readonly WindowRow _longWindow = new();
        private readonly PaceChartControl _paceChart = new();
        private readonly HistoryChartControl _historyChart = new();
        private readonly StackPanel _stack = new();
        private bool _showHistory;
        private bool _compactMode;

        public ServicePanel(string name, string longWindowLabel)
        {
            _title.Text = name;
            _title.FontSize = 18;
            _title.FontWeight = FontWeight.SemiBold;

            _plan.Text = "Plan: --";
            _plan.Margin = new Thickness(0, 2, 0, 10);

            _primary.Set("5h", "Loading...", null);
            _longWindow.Set(longWindowLabel, "Loading...", null);
            _paceChart.Height = 220;
            _historyChart.Height = 220;
            _historyChart.IsVisible = false;

            _stack.Spacing = 12;
            _stack.Children.Add(_title);
            _stack.Children.Add(_plan);
            _stack.Children.Add(_primary.Root);
            _stack.Children.Add(_longWindow.Root);
            _stack.Children.Add(_paceChart);
            _stack.Children.Add(_historyChart);

            Root = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Child = _stack
            };
            ApplyTheme(UiPalette.FromThemeName("light"));
        }

        public Border Root { get; }

        public void SetPlan(string text)
        {
            _plan.Text = text;
        }

        public void SetPrimary(string title, string detail, double? remainingPercent)
        {
            _primary.Set(title, detail, remainingPercent);
        }

        public void SetLongWindow(string title, string detail, double? remainingPercent)
        {
            _longWindow.Set(title, detail, remainingPercent);
        }

        public void SetPaceData(string title, CodexWindow? window, List<UsageSample> samples)
        {
            _paceChart.SetData(title, window, samples);
        }

        public void SetHistoryData(string title, HistoryAggregation aggregation, List<UsageHistoryPoint> points)
        {
            _historyChart.SetData(title, aggregation, points);
        }

        public void SetChartMode(bool showHistory)
        {
            _showHistory = showHistory;
            ApplyChartVisibility();
        }

        public void SetCompactMode(bool compactMode)
        {
            _compactMode = compactMode;
            _stack.Spacing = compactMode ? 8 : 12;
            _plan.Margin = compactMode ? new Thickness(0, 2, 0, 6) : new Thickness(0, 2, 0, 10);
            Root.Padding = compactMode ? new Thickness(12) : new Thickness(16);
            _primary.SetCompactMode(compactMode);
            _longWindow.SetCompactMode(compactMode);
            ApplyChartVisibility();
        }

        public void ApplyTheme(UiPalette palette)
        {
            _title.Foreground = palette.Text;
            _plan.Foreground = palette.Muted;
            Root.Background = palette.Card;
            Root.BorderBrush = palette.Border;
            _primary.ApplyTheme(palette);
            _longWindow.ApplyTheme(palette);
            _paceChart.SetTheme(palette);
            _historyChart.SetTheme(palette);
        }

        private void ApplyChartVisibility()
        {
            _paceChart.IsVisible = !_compactMode && !_showHistory;
            _historyChart.IsVisible = !_compactMode && _showHistory;
        }
    }

    private sealed class WindowRow
    {
        private readonly TextBlock _title = new();
        private readonly TextBlock _detail = new();
        private readonly ProgressBar _bar = new();

        public WindowRow()
        {
            _title.FontSize = 16;
            _title.FontWeight = FontWeight.SemiBold;

            _detail.FontSize = 13;
            _detail.TextWrapping = TextWrapping.Wrap;

            _bar.Minimum = 0;
            _bar.Maximum = 100;
            _bar.Height = 12;
            _bar.Margin = new Thickness(0, 8, 0, 0);

            Root = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Child = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        _title,
                        _detail,
                        _bar
                    }
                }
            };
            ApplyTheme(UiPalette.FromThemeName("light"));
        }

        public Border Root { get; }

        public void Set(string title, string detail, double? remainingPercent)
        {
            _title.Text = title;
            _detail.Text = detail;
            _bar.Value = remainingPercent.HasValue ? Math.Max(0, Math.Min(100, remainingPercent.Value)) : 0;
            _bar.Opacity = remainingPercent.HasValue ? 1 : 0.35;
        }

        public void SetCompactMode(bool compactMode)
        {
            _title.FontSize = compactMode ? 14 : 16;
            _detail.FontSize = compactMode ? 12 : 13;
            _bar.Height = compactMode ? 10 : 12;
            _bar.Margin = new Thickness(0, compactMode ? 6 : 8, 0, 0);
            Root.Padding = compactMode ? new Thickness(10) : new Thickness(12);
        }

        public void ApplyTheme(UiPalette palette)
        {
            _title.Foreground = palette.Text;
            _detail.Foreground = palette.Muted;
            Root.Background = palette.Panel;
            Root.BorderBrush = palette.Subtle;
        }
    }
}
