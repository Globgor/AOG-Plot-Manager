using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using PlotManager.Core.Models;
using SkiaSharp;
using System;

namespace PlotManager.UI.Views.Controls;

public partial class TelemetryPanel : UserControl
{
    private SensorSnapshot? _snapshot;
    private double _minSafePressureBar = 2.0;
    private double _maxPressureBar = 10.0;
    private double _targetFlowLpm;

    public double MinSafePressureBar
    {
        get => _minSafePressureBar;
        set { _minSafePressureBar = value; InvalidateVisual(); }
    }

    public double MaxPressureBar
    {
        get => _maxPressureBar;
        set { _maxPressureBar = value; InvalidateVisual(); }
    }

    public double TargetFlowLpm
    {
        get => _targetFlowLpm;
        set { _targetFlowLpm = value; InvalidateVisual(); }
    }

    public TelemetryPanel()
    {
        InitializeComponent();
    }

    public void UpdateSnapshot(SensorSnapshot snapshot)
    {
        _snapshot = snapshot;
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.Custom(new TelemetryDrawOp(Bounds, _snapshot, _minSafePressureBar, _maxPressureBar, _targetFlowLpm));
    }

    private class TelemetryDrawOp : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly SensorSnapshot? _snapshot;
        private readonly double _minSafePressureBar;
        private readonly double _maxPressureBar;
        private readonly double _targetFlowLpm;

        private const float Padding = 12f;
        private const float GaugeHeight = 30f;
        private const float BarWidth = 20f;
        private const float BarMaxHeight = 120f;

        public TelemetryDrawOp(Rect bounds, SensorSnapshot? snapshot, double minSafe, double max, double targetFlow)
        {
            _bounds = bounds; _snapshot = snapshot;
            _minSafePressureBar = minSafe; _maxPressureBar = max; _targetFlowLpm = targetFlow;
        }

        public Rect Bounds => _bounds;
        public void Dispose() { }
        public bool Equals(ICustomDrawOperation? other) => false;
        public bool HitTest(Point p) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null) return;
            using var lease = leaseFeature.Lease();
            SKCanvas canvas = lease.SkCanvas;

            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)_bounds.Width, (float)_bounds.Height));

            using var titleFont = new SKPaint { Color = new SKColor(120, 120, 120), TextSize = 12, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), IsAntialias = true };
            using var valueFont = new SKPaint { Color = new SKColor(220, 220, 220), TextSize = 14, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), IsAntialias = true };

            float y = Padding;
            float width = (float)_bounds.Width - Padding * 2;

            // AIR PRESSURE
            titleFont.GetFontMetrics(out SKFontMetrics tm);
            canvas.DrawText("AIR PRESSURE", Padding, y - tm.Ascent, titleFont);
            y += 20;

            double pressure = _snapshot?.AirPressureBar ?? 0;
            DrawPressureGauge(canvas, Padding, y, width, GaugeHeight, pressure);

            string pressureText = $"{pressure:F1} Bar";
            float valW = valueFont.MeasureText(pressureText);
            valueFont.GetFontMetrics(out SKFontMetrics vm);
            canvas.DrawText(pressureText, (float)_bounds.Width - Padding - valW, y + GaugeHeight/2 - (vm.Ascent + vm.Descent)/2, valueFont);

            y += GaugeHeight + 25;

            // FLOW RATES
            canvas.DrawText("FLOW RATES (L/min)", Padding, y - tm.Ascent, titleFont);
            y += 20;

            DrawFlowMeters(canvas, Padding, y, width);

            // STALE OVERLAY
            if (_snapshot == null || _snapshot.IsStale)
            {
                using var overlayPaint = new SKPaint { Color = new SKColor(30, 30, 30, 180), Style = SKPaintStyle.Fill };
                canvas.DrawRect(0, 0, (float)_bounds.Width, (float)_bounds.Height, overlayPaint);

                using var staleFont = new SKPaint { Color = new SKColor(244, 67, 54), TextSize = 18, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), IsAntialias = true };
                string msg = "NO TELEMETRY";
                float msgW = staleFont.MeasureText(msg);
                staleFont.GetFontMetrics(out SKFontMetrics sm);
                canvas.DrawText(msg, ((float)_bounds.Width - msgW) / 2, ((float)_bounds.Height - (sm.Ascent + sm.Descent)) / 2, staleFont);
            }

            canvas.Restore();
        }

        private void DrawPressureGauge(SKCanvas canvas, float x, float y, float width, float height, double pressure)
        {
            float fillFraction = (float)Math.Clamp(pressure / _maxPressureBar, 0, 1);
            float redZoneFraction = (float)(_minSafePressureBar / _maxPressureBar);
            float gaugeWidth = width * 0.7f;

            using var bgPaint = new SKPaint { Color = new SKColor(50, 50, 55), Style = SKPaintStyle.Fill };
            canvas.DrawRect(x, y, gaugeWidth, height, bgPaint);

            float redWidth = gaugeWidth * redZoneFraction;
            using var redPaint = new SKPaint { Color = new SKColor(244, 67, 54, 60), Style = SKPaintStyle.Fill };
            canvas.DrawRect(x, y, redWidth, height, redPaint);

            float fillWidth = gaugeWidth * fillFraction;
            SKColor fillColor = pressure < _minSafePressureBar ? new SKColor(244, 67, 54) :
                                pressure < _minSafePressureBar * 1.5 ? new SKColor(255, 193, 7) :
                                new SKColor(76, 175, 80);
            using var fillPaint = new SKPaint { Color = fillColor, Style = SKPaintStyle.Fill };
            canvas.DrawRect(x, y, fillWidth, height, fillPaint);

            using var borderPaint = new SKPaint { Color = new SKColor(80, 80, 80), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            canvas.DrawRect(x, y, gaugeWidth, height, borderPaint);

            float threshX = x + redWidth;
            using var threshPaint = new SKPaint { Color = new SKColor(244, 67, 54), Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            threshPaint.PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0);
            canvas.DrawLine(threshX, y - 2, threshX, y + height + 2, threshPaint);
        }

        private void DrawFlowMeters(SKCanvas canvas, float x, float y, float areaWidth)
        {
            int count = SensorSnapshot.FlowMeterCount;
            float spacing = Math.Max(4, (areaWidth - count * BarWidth) / (count + 1));
            float maxLpm = 5.0f;

            if (_snapshot?.FlowRatesLpm != null)
            {
                foreach (double v in _snapshot.FlowRatesLpm)
                    if (v > maxLpm) maxLpm = (float)v;
            }
            if (_targetFlowLpm > maxLpm) maxLpm = (float)_targetFlowLpm;
            maxLpm *= 1.2f;

            using var labelFont = new SKPaint { Color = new SKColor(120, 120, 120), TextSize = 10, Typeface = SKTypeface.FromFamilyName("Segoe UI"), IsAntialias = true };
            using var barPaint = new SKPaint { Color = new SKColor(33, 150, 243), Style = SKPaintStyle.Fill };
            using var outlinePaint = new SKPaint { Color = new SKColor(60, 60, 65), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };

            for (int i = 0; i < count; i++)
            {
                float barX = x + spacing + i * (BarWidth + spacing);
                double flowLpm = _snapshot?.FlowRatesLpm?[i] ?? 0;
                float barHeight = (float)(flowLpm / maxLpm) * BarMaxHeight;
                barHeight = Math.Clamp(barHeight, 0, BarMaxHeight);

                float barTop = y + BarMaxHeight - barHeight;
                canvas.DrawRect(barX, barTop, BarWidth, barHeight, barPaint);
                canvas.DrawRect(barX, y, BarWidth, BarMaxHeight, outlinePaint);

                string label = (i + 1).ToString();
                float lw = labelFont.MeasureText(label);
                labelFont.GetFontMetrics(out SKFontMetrics lm);
                canvas.DrawText(label, barX + (BarWidth - lw) / 2, y + BarMaxHeight + 4 - lm.Ascent, labelFont);
            }

            if (_targetFlowLpm > 0)
            {
                float targetY = y + BarMaxHeight - (float)(_targetFlowLpm / maxLpm) * BarMaxHeight;
                using var targetPen = new SKPaint { Color = new SKColor(255, 152, 0), Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
                targetPen.PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0);
                canvas.DrawLine(x, targetY, x + areaWidth, targetY, targetPen);

                using var targetFont = new SKPaint { Color = new SKColor(255, 152, 0), TextSize = 10, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), IsAntialias = true };
                targetFont.GetFontMetrics(out var tfm);
                canvas.DrawText($"Target: {_targetFlowLpm:F1}", x + areaWidth - 60, targetY - 4, targetFont);
            }
        }
    }
}
