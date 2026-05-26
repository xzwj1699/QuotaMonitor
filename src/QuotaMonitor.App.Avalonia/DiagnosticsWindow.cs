using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace QuotaMonitor.App.Avalonia;

internal sealed class DiagnosticsWindow : Window
{
    public DiagnosticsWindow(string diagnostics, UiPalette palette)
    {
        Title = "Quota Monitor Diagnostics";
        Width = 680;
        Height = 520;
        MinWidth = 520;
        MinHeight = 360;
        RequestedThemeVariant = palette.ThemeVariant;
        Background = palette.Page;

        var text = new TextBlock
        {
            Text = diagnostics,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = FontFamily.Parse("Menlo, Consolas, monospace"),
            FontSize = 12,
            Foreground = palette.Text
        };

        var close = new Button
        {
            Content = "Close",
            Width = 96,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        close.Click += (_, _) => Close();

        var scroll = new ScrollViewer
        {
            Content = text,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(16),
            Children =
            {
                scroll,
                close
            }
        };
        Grid.SetRow(close, 1);
    }
}
