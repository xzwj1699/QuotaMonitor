using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using QuotaMonitor.Core.Models;

namespace QuotaMonitor.App.Avalonia;

internal sealed class HistoryChartControl : Control
{
    private static readonly Typeface RegularTypeface = new("Inter, Segoe UI, Arial");
    private static readonly Typeface BoldTypeface = new("Inter, Segoe UI, Arial", FontStyle.Normal, FontWeight.SemiBold);

    private UiPalette _palette = UiPalette.FromThemeName("light");
    private string _title = "Usage history";
    private HistoryAggregation _aggregation = HistoryAggregation.Day;
    private List<UsageHistoryPoint> _points = new();

    public HistoryChartControl()
    {
        MinHeight = 190;
    }

    public void SetTheme(UiPalette palette)
    {
        _palette = palette;
        InvalidateVisual();
    }

    public void SetData(string title, HistoryAggregation aggregation, List<UsageHistoryPoint>? points)
    {
        _title = title ?? "Usage history";
        _aggregation = aggregation;
        _points = points ?? new List<UsageHistoryPoint>();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width < 120 || bounds.Height < 120)
        {
            return;
        }

        context.DrawRectangle(_palette.Card, null, bounds);
        DrawText(context, _title + " - " + AggregationLabel(_aggregation), 0, 0, 13, _palette.Text, BoldTypeface, bounds.Width);

        var plot = PlotRect(bounds);
        var maxValue = CalculateMaxValue(_points);

        DrawAxes(context, plot, maxValue);
        context.DrawRectangle(null, new Pen(_palette.Border, 1), plot);
        context.DrawLine(new Pen(_palette.Grid, 1), new Point(plot.Left, plot.Top + plot.Height / 2), new Point(plot.Right, plot.Top + plot.Height / 2));

        if (_points.Count == 0)
        {
            DrawCenteredText(context, "No history yet", plot);
            return;
        }

        var slotWidth = plot.Width / Math.Max(1, _points.Count);
        var barWidth = Math.Max(3, Math.Min(28, slotWidth * 0.54));
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            var x = plot.Left + slotWidth * i + slotWidth / 2.0;
            var barHeight = plot.Height * Math.Max(0, point.UsedPercent) / maxValue;
            var barRect = new Rect(
                x - barWidth / 2.0,
                plot.Bottom - barHeight,
                barWidth,
                Math.Max(1, barHeight));
            context.DrawRectangle(_palette.Accent, null, barRect);
        }

        DrawText(context, "usage per period", plot.Left, plot.Top + 4, 11, _palette.Muted, RegularTypeface, plot.Width, TextAlignment.Right);
    }

    private void DrawAxes(DrawingContext context, Rect plot, double maxValue)
    {
        DrawYLabel(context, plot, maxValue, maxValue);
        DrawYLabel(context, plot, maxValue / 2.0, maxValue);
        DrawYLabel(context, plot, 0, maxValue);

        if (_points.Count == 0)
        {
            return;
        }

        var first = _points.First();
        var middle = _points[_points.Count / 2];
        var last = _points.Last();

        DrawText(context, first.Label, plot.Left - 2, plot.Bottom + 6, 10.5, _palette.Muted, RegularTypeface, 92);
        if (plot.Width >= 260 && _points.Count > 2)
        {
            DrawText(context, middle.Label, plot.Left + plot.Width / 2 - 46, plot.Bottom + 6, 10.5, _palette.Muted, RegularTypeface, 92, TextAlignment.Center);
        }
        DrawText(context, last.Label, plot.Right - 92, plot.Bottom + 6, 10.5, _palette.Muted, RegularTypeface, 92, TextAlignment.Right);
    }

    private void DrawYLabel(DrawingContext context, Rect plot, double value, double maxValue)
    {
        var y = plot.Bottom - plot.Height * value / maxValue;
        context.DrawLine(new Pen(_palette.Grid, 1), new Point(plot.Left - 4, y), new Point(plot.Left, y));
        DrawText(
            context,
            value.ToString("0", CultureInfo.InvariantCulture) + "%",
            0,
            y - 7,
            10.5,
            _palette.Muted,
            RegularTypeface,
            plot.Left - 8,
            TextAlignment.Right);
    }

    private static Rect PlotRect(Rect bounds)
    {
        const double left = 44;
        const double top = 34;
        const double right = 8;
        const double bottom = 28;
        return new Rect(
            left,
            top,
            Math.Max(1, bounds.Width - left - right),
            Math.Max(1, bounds.Height - top - bottom));
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
        return aggregation switch
        {
            HistoryAggregation.Week => "Week",
            HistoryAggregation.Month => "Month",
            _ => "Day"
        };
    }

    private void DrawCenteredText(DrawingContext context, string text, Rect rect)
    {
        var formatted = BuildText(text, 12, _palette.Muted, RegularTypeface, rect.Width, TextAlignment.Center);
        context.DrawText(formatted, new Point(rect.Left, rect.Top + Math.Max(0, (rect.Height - formatted.Height) / 2)));
    }

    private static void DrawText(
        DrawingContext context,
        string text,
        double x,
        double y,
        double fontSize,
        IBrush brush,
        Typeface typeface,
        double maxWidth,
        TextAlignment alignment = TextAlignment.Left)
    {
        context.DrawText(BuildText(text, fontSize, brush, typeface, maxWidth, alignment), new Point(x, y));
    }

    private static FormattedText BuildText(
        string text,
        double fontSize,
        IBrush brush,
        Typeface typeface,
        double maxWidth,
        TextAlignment alignment)
    {
        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            TextAlignment = alignment
        };
    }
}
