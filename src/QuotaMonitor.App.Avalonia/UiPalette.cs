using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using QuotaMonitor.Core.Config;

namespace QuotaMonitor.App.Avalonia;

internal sealed class UiPalette
{
    private UiPalette(
        bool isDark,
        string page,
        string card,
        string panel,
        string border,
        string text,
        string muted,
        string subtle,
        string selected,
        string selectedText,
        string selectedBorder,
        string accent,
        string warning,
        string error,
        string grid)
    {
        IsDark = isDark;
        Page = Brush(page);
        Card = Brush(card);
        Panel = Brush(panel);
        Border = Brush(border);
        Text = Brush(text);
        Muted = Brush(muted);
        Subtle = Brush(subtle);
        Selected = Brush(selected);
        SelectedText = Brush(selectedText);
        SelectedBorder = Brush(selectedBorder);
        Accent = Brush(accent);
        Warning = Brush(warning);
        Error = Brush(error);
        Grid = Brush(grid);
    }

    public bool IsDark { get; }
    public IBrush Page { get; }
    public IBrush Card { get; }
    public IBrush Panel { get; }
    public IBrush Border { get; }
    public IBrush Text { get; }
    public IBrush Muted { get; }
    public IBrush Subtle { get; }
    public IBrush Selected { get; }
    public IBrush SelectedText { get; }
    public IBrush SelectedBorder { get; }
    public IBrush Accent { get; }
    public IBrush Warning { get; }
    public IBrush Error { get; }
    public IBrush Grid { get; }

    public ThemeVariant ThemeVariant => IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

    public static UiPalette FromConfig(MonitorConfig config)
    {
        return FromThemeName(config.theme);
    }

    public static UiPalette FromThemeName(string? theme)
    {
        return string.Equals(MonitorConfig.NormalizeTheme(theme ?? "light"), "dark", StringComparison.OrdinalIgnoreCase)
            ? Dark()
            : Light();
    }

    private static UiPalette Light()
    {
        return new UiPalette(
            false,
            "#F7F8FA",
            "#FFFFFF",
            "#F4F6F9",
            "#D6DBE2",
            "#1E2228",
            "#5C626C",
            "#E1E5EB",
            "#E0EEFF",
            "#184E94",
            "#9BC7FA",
            "#2578D6",
            "#B47614",
            "#D14239",
            "#E5E9EE");
    }

    private static UiPalette Dark()
    {
        return new UiPalette(
            true,
            "#15181D",
            "#20252C",
            "#2A3038",
            "#3C4551",
            "#EEF2F6",
            "#AAB3BF",
            "#333B45",
            "#233C58",
            "#D7E9FF",
            "#4F79A8",
            "#6AA9FF",
            "#E0A642",
            "#FF6B61",
            "#39424D");
    }

    private static SolidColorBrush Brush(string value)
    {
        return new SolidColorBrush(Color.Parse(value));
    }
}
