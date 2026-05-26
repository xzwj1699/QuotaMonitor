using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using QuotaMonitor.Core.Models;

namespace QuotaMonitor.App.Avalonia;

internal sealed class PaceChartControl : Control
{
    private static readonly Typeface RegularTypeface = new("Inter, Segoe UI, Arial");
    private static readonly Typeface BoldTypeface = new("Inter, Segoe UI, Arial", FontStyle.Normal, FontWeight.SemiBold);

    private UiPalette _palette = UiPalette.FromThemeName("light");
    private string _title = "Usage pace";
    private List<UsageSample> _samples = new();
    private DateTimeOffset? _resetAt;
    private int _windowMinutes;
    private double? _currentUsedPercent;

    public PaceChartControl()
    {
        MinHeight = 190;
    }

    public void SetTheme(UiPalette palette)
    {
        _palette = palette;
        InvalidateVisual();
    }

    public void SetData(string title, CodexWindow? window, List<UsageSample>? samples)
    {
        _title = title;
        _samples = samples ?? new List<UsageSample>();
        _resetAt = window?.ResetsAt;
        _windowMinutes = window?.WindowMinutes ?? 0;
        _currentUsedPercent = window?.UsedPercent;

        if (_currentUsedPercent.HasValue)
        {
            var maxReasonableUsed = Math.Min(100, _currentUsedPercent.Value + 5);
            _samples = _samples
                .Where(s => s.UsedPercent <= maxReasonableUsed)
                .ToList();
        }

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

        DrawText(context, BuildTitle(), 0, 0, 13, _palette.Text, BoldTypeface, bounds.Width);
        DrawText(context, BuildPredictionText(), 0, 22, 12, _palette.Muted, RegularTypeface, bounds.Width);

        var plot = PlotRect(bounds);
        if (!_resetAt.HasValue || _windowMinutes <= 0)
        {
            DrawCenteredText(context, "No pace data yet", plot);
            return;
        }

        context.DrawRectangle(null, new Pen(_palette.Border, 1), plot);
        context.DrawLine(new Pen(_palette.Grid, 1), new Point(plot.Left, plot.Top + plot.Height / 2), new Point(plot.Right, plot.Top + plot.Height / 2));

        var now = DateTimeOffset.Now;
        var windowStart = _resetAt.Value.AddMinutes(-_windowMinutes);
        var nowX = Clamp01((now - windowStart).TotalMinutes / _windowMinutes);

        DrawAxes(context, plot, windowStart, _resetAt.Value);

        var idealPen = new Pen(_palette.Muted, 1.3)
        {
            DashStyle = new DashStyle(new[] { 5.0, 4.0 }, 0)
        };
        context.DrawLine(idealPen, PointFor(plot, 0, 0), PointFor(plot, 1, 100));

        var points = BuildActualPoints(plot, windowStart, nowX);
        if (points.Count >= 2)
        {
            DrawPolyline(context, points, new Pen(_palette.Accent, 2.0));
        }

        foreach (var point in points.Skip(Math.Max(0, points.Count - 16)))
        {
            context.DrawEllipse(_palette.Accent, null, point, 2.5, 2.5);
        }

        var nowPlotX = plot.Left + plot.Width * nowX;
        context.DrawLine(new Pen(_palette.Border, 1), new Point(nowPlotX, plot.Top), new Point(nowPlotX, plot.Bottom));
        DrawText(context, "ideal", plot.Right - 34, plot.Top + 6, 11, _palette.Muted, RegularTypeface, 34);
    }

    private List<Point> BuildActualPoints(Rect plot, DateTimeOffset windowStart, double nowX)
    {
        var points = new List<Point> { PointFor(plot, 0, 0) };
        foreach (var sample in _samples)
        {
            var x = Clamp01((sample.TimestampValue - windowStart).TotalMinutes / _windowMinutes);
            points.Add(PointFor(plot, x, sample.UsedPercent));
        }
        if (_currentUsedPercent.HasValue)
        {
            points.Add(PointFor(plot, nowX, _currentUsedPercent.Value));
        }

        return points
            .GroupBy(p => Math.Round(p.X, 1))
            .Select(g => g.Last())
            .OrderBy(p => p.X)
            .ToList();
    }

    private static void DrawPolyline(DrawingContext context, List<Point> points, IPen pen)
    {
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(points[0], false);
            foreach (var point in points.Skip(1))
            {
                stream.LineTo(point);
            }
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static Rect PlotRect(Rect bounds)
    {
        const double left = 44;
        const double top = 50;
        const double right = 8;
        const double bottom = 28;
        return new Rect(
            left,
            top,
            Math.Max(1, bounds.Width - left - right),
            Math.Max(1, bounds.Height - top - bottom));
    }

    private void DrawAxes(DrawingContext context, Rect plot, DateTimeOffset windowStart, DateTimeOffset resetAt)
    {
        DrawYLabel(context, plot, 100);
        DrawYLabel(context, plot, 50);
        DrawYLabel(context, plot, 0);

        var middle = windowStart.AddTicks((resetAt - windowStart).Ticks / 2);
        DrawText(context, FormatAxisTime(windowStart), plot.Left - 2, plot.Bottom + 6, 10.5, _palette.Muted, RegularTypeface, 92);
        if (plot.Width >= 260)
        {
            context.DrawLine(new Pen(_palette.Grid, 1), new Point(plot.Left + plot.Width / 2, plot.Bottom), new Point(plot.Left + plot.Width / 2, plot.Bottom + 4));
            DrawText(context, FormatAxisTime(middle), plot.Left + plot.Width / 2 - 46, plot.Bottom + 6, 10.5, _palette.Muted, RegularTypeface, 92);
        }
        DrawText(context, FormatAxisTime(resetAt), plot.Right - 92, plot.Bottom + 6, 10.5, _palette.Muted, RegularTypeface, 92);
    }

    private void DrawYLabel(DrawingContext context, Rect plot, int percent)
    {
        var y = plot.Bottom - plot.Height * percent / 100.0;
        context.DrawLine(new Pen(_palette.Grid, 1), new Point(plot.Left - 4, y), new Point(plot.Left, y));
        DrawText(context, percent.ToString(CultureInfo.InvariantCulture) + "%", 0, y - 7, 10.5, _palette.Muted, RegularTypeface, plot.Left - 8, TextAlignment.Right);
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
        var formatted = new FormattedText(
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
        return formatted;
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

    private static Point PointFor(Rect plot, double x, double usedPercent)
    {
        return new Point(
            plot.Left + plot.Width * Clamp01(x),
            plot.Bottom - plot.Height * Math.Max(0, Math.Min(100, usedPercent)) / 100.0);
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
