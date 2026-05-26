using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using QuotaMonitor.Core.Config;

namespace QuotaMonitor.App.Avalonia;

internal sealed class SettingsWindow : Window
{
    private readonly CheckBox _showCodex = new();
    private readonly CheckBox _showClaude = new();
    private readonly CheckBox _compactMode = new();
    private readonly CheckBox _alwaysOnTop = new();
    private readonly CheckBox _startAtTopRight = new();
    private readonly CheckBox _minimizeToTray = new();
    private readonly CheckBox _startWithSystem = new();
    private readonly ComboBox _theme = new();
    private readonly CheckBox _useRealtimeApi = new();
    private readonly CheckBox _alertsEnabled = new();
    private readonly TextBox _pollIntervalSeconds = new();
    private readonly TextBox _realtimeTimeoutSeconds = new();
    private readonly TextBox _fiveHourThreshold = new();
    private readonly TextBox _longWindowThreshold = new();
    private readonly TextBox _codexSessionsPath = new();
    private readonly TextBox _codexAuthPath = new();
    private readonly TextBox _claudeProjectsPath = new();
    private readonly TextBox _claudeCredentialsPath = new();
    private readonly TextBlock _errorText = new();

    public SettingsWindow(MonitorConfig config, UiPalette palette)
    {
        Title = "Quota Monitor Settings";
        Width = 680;
        Height = 680;
        MinWidth = 560;
        MinHeight = 520;
        RequestedThemeVariant = palette.ThemeVariant;
        Background = palette.Page;

        _showCodex.Content = "Show Codex";
        _showClaude.Content = "Show Claude";
        _compactMode.Content = "Compact mode";
        _alwaysOnTop.Content = "Always on top";
        _startAtTopRight.Content = "Start at top right";
        _minimizeToTray.Content = "Keep running in tray/menu bar";
        _startWithSystem.Content = "Start with system";
        _useRealtimeApi.Content = "Use realtime API";
        _alertsEnabled.Content = "Enable quota alerts";
        _theme.ItemsSource = new[] { "light", "dark" };
        _theme.SelectedItem = MonitorConfig.NormalizeTheme(config.theme);
        _theme.Height = 34;

        _showCodex.IsChecked = config.showCodex;
        _showClaude.IsChecked = config.showClaude;
        _compactMode.IsChecked = config.compactMode;
        _alwaysOnTop.IsChecked = config.alwaysOnTop;
        _startAtTopRight.IsChecked = config.startAtTopRight;
        _minimizeToTray.IsChecked = config.minimizeToTray;
        _startWithSystem.IsChecked = config.startWithSystem;
        _useRealtimeApi.IsChecked = config.useRealtimeApi;
        _alertsEnabled.IsChecked = config.alertsEnabled;
        SetText(_pollIntervalSeconds, config.pollIntervalSeconds);
        SetText(_realtimeTimeoutSeconds, config.realtimeApiTimeoutSeconds);
        SetText(_fiveHourThreshold, config.alertFiveHourRemainingPercent);
        SetText(_longWindowThreshold, config.alertLongWindowRemainingPercent);
        _codexSessionsPath.Text = config.codexSessionsPath;
        _codexAuthPath.Text = config.codexAuthPath;
        _claudeProjectsPath.Text = config.claudeProjectsPath;
        _claudeCredentialsPath.Text = config.claudeCredentialsPath;

        _errorText.Foreground = palette.Error;
        _errorText.TextWrapping = TextWrapping.Wrap;
        _errorText.Margin = new Thickness(0, 12, 0, 0);

        var form = new StackPanel
        {
            Spacing = 14
        };
        AddSection(form, "Display", palette);
        form.Children.Add(_showCodex);
        form.Children.Add(_showClaude);
        form.Children.Add(_compactMode);
        form.Children.Add(_alwaysOnTop);
        form.Children.Add(_startAtTopRight);
        form.Children.Add(_minimizeToTray);
        form.Children.Add(_startWithSystem);
        AddField(form, "Theme", _theme, palette);

        AddSection(form, "Refresh", palette);
        form.Children.Add(_useRealtimeApi);
        AddField(form, "Refresh interval seconds", _pollIntervalSeconds, palette);
        AddField(form, "Realtime timeout seconds", _realtimeTimeoutSeconds, palette);

        AddSection(form, "Alerts", palette);
        form.Children.Add(_alertsEnabled);
        AddField(form, "5h remaining threshold percent", _fiveHourThreshold, palette);
        AddField(form, "Week/7d remaining threshold percent", _longWindowThreshold, palette);

        AddSection(form, "Data paths", palette);
        AddField(form, "Codex sessions", _codexSessionsPath, palette);
        AddField(form, "Codex auth", _codexAuthPath, palette);
        AddField(form, "Claude projects", _claudeProjectsPath, palette);
        AddField(form, "Claude credentials", _claudeCredentialsPath, palette);

        var save = new Button
        {
            Content = "Save",
            Width = 96,
            Height = 34
        };
        save.Click += (_, _) =>
        {
            if (Validate())
            {
                Close(true);
            }
        };

        var cancel = new Button
        {
            Content = "Cancel",
            Width = 96,
            Height = 34
        };
        cancel.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Spacing = 8,
            Children =
            {
                cancel,
                save
            }
        };

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            Margin = new Thickness(18),
            Children =
            {
                new ScrollViewer
                {
                    Content = form,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                _errorText,
                buttons
            }
        };
        Grid.SetRow(_errorText, 1);
        Grid.SetRow(buttons, 2);

        Content = content;
    }

    public void ApplyTo(MonitorConfig config)
    {
        config.showCodex = _showCodex.IsChecked == true;
        config.showClaude = _showClaude.IsChecked == true;
        config.compactMode = _compactMode.IsChecked == true;
        config.alwaysOnTop = _alwaysOnTop.IsChecked == true;
        config.startAtTopRight = _startAtTopRight.IsChecked == true;
        config.minimizeToTray = _minimizeToTray.IsChecked == true;
        config.startWithSystem = _startWithSystem.IsChecked == true;
        config.theme = MonitorConfig.NormalizeTheme(Convert.ToString(_theme.SelectedItem) ?? "light");
        config.useRealtimeApi = _useRealtimeApi.IsChecked == true;
        config.alertsEnabled = _alertsEnabled.IsChecked == true;
        config.pollIntervalSeconds = ParseInt(_pollIntervalSeconds.Text, 300);
        config.realtimeApiTimeoutSeconds = ParseInt(_realtimeTimeoutSeconds.Text, 15);
        config.alertFiveHourRemainingPercent = ParseDouble(_fiveHourThreshold.Text, 20);
        config.alertLongWindowRemainingPercent = ParseDouble(_longWindowThreshold.Text, 30);
        config.codexSessionsPath = _codexSessionsPath.Text ?? string.Empty;
        config.codexAuthPath = _codexAuthPath.Text ?? string.Empty;
        config.claudeProjectsPath = _claudeProjectsPath.Text ?? string.Empty;
        config.claudeCredentialsPath = _claudeCredentialsPath.Text ?? string.Empty;
    }

    private bool Validate()
    {
        if (_showCodex.IsChecked != true && _showClaude.IsChecked != true)
        {
            _errorText.Text = "Select at least Codex or Claude.";
            return false;
        }

        if (!TryParseInt(_pollIntervalSeconds.Text, 3, 86400, "refresh interval") ||
            !TryParseInt(_realtimeTimeoutSeconds.Text, 3, 120, "realtime timeout") ||
            !TryParseDouble(_fiveHourThreshold.Text, 0, 100, "5h threshold") ||
            !TryParseDouble(_longWindowThreshold.Text, 0, 100, "long-window threshold"))
        {
            return false;
        }

        _errorText.Text = string.Empty;
        return true;
    }

    private bool TryParseInt(string? value, int min, int max, string label)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < min ||
            parsed > max)
        {
            _errorText.Text = $"Enter a valid {label} between {min} and {max}.";
            return false;
        }

        return true;
    }

    private bool TryParseDouble(string? value, double min, double max, string label)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            double.IsNaN(parsed) ||
            parsed < min ||
            parsed > max)
        {
            _errorText.Text = $"Enter a valid {label} between {min} and {max}.";
            return false;
        }

        return true;
    }

    private static void AddSection(StackPanel form, string text, UiPalette palette)
    {
        form.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = palette.Text,
            Margin = new Thickness(0, 6, 0, 0)
        });
    }

    private static void AddField(StackPanel form, string label, Control control, UiPalette palette)
    {
        control.Height = 34;

        form.Children.Add(new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = palette.Muted
                },
                control
            }
        });
    }

    private static void SetText(TextBox textBox, int value)
    {
        textBox.Text = value.ToString(CultureInfo.InvariantCulture);
    }

    private static void SetText(TextBox textBox, double value)
    {
        textBox.Text = value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static int ParseInt(string? value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    private static double ParseDouble(string? value, double fallback)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }
}
