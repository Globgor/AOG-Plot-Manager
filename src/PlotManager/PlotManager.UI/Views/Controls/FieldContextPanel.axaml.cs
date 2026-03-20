using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using PlotManager.Core.Models;
using PlotManager.Core.Protocol;
using PlotManager.Core.Services;
using SkiaSharp;

namespace PlotManager.UI.Views.Controls;

public partial class FieldContextPanel : UserControl
{
    private SpatialResult? _lastResult;
    private PassState? _currentPass;
    private string? _trialName;
    private bool _trialActive;
    private long _trialRecordCount;
    private double _speedKmh;
    private double _targetSpeedKmh;
    private string _loggerStatus = "idle";
    private long _logEntryCount;
    private string? _nextProduct;
    private double _latitude;
    private double _longitude;
    private double _headingDeg;

    public FieldContextPanel()
    {
        InitializeComponent();
    }

    public void UpdateSpatial(SpatialResult result, double speedKmh, double targetSpeedKmh)
    {
        _lastResult = result;
        _speedKmh = speedKmh;
        _targetSpeedKmh = targetSpeedKmh;
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);
    }

    public void UpdatePass(PassState? pass)
    {
        _currentPass = pass;
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);
    }

    public void UpdateTrialStatus(bool isActive, string? trialName, long recordCount)
    {
        _trialActive = isActive;
        _trialName = trialName;
        _trialRecordCount = recordCount;
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);
    }

    public void UpdateLoggerStatus(string status, long entryCount)
    {
        _loggerStatus = status;
        _logEntryCount = entryCount;
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);
    }

    public void UpdateGps(double lat, double lon, double heading)
    {
        _latitude = lat;
        _longitude = lon;
        _headingDeg = heading;
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);
    }

    public void SetNextProduct(string? product)
    {
        _nextProduct = product;
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var snapshot = new FieldContextSnapshot(this);
        context.Custom(new FieldContextDrawOp(Bounds, snapshot));
    }

    private class FieldContextSnapshot
    {
        public SpatialResult? LastResult { get; }
        public PassState? CurrentPass { get; }
        public string? TrialName { get; }
        public bool TrialActive { get; }
        public long TrialRecordCount { get; }
        public double SpeedKmh { get; }
        public double TargetSpeedKmh { get; }
        public string LoggerStatus { get; }
        public long LogEntryCount { get; }
        public string? NextProduct { get; }
        public double Latitude { get; }
        public double Longitude { get; }
        public double HeadingDeg { get; }

        public FieldContextSnapshot(FieldContextPanel panel)
        {
            LastResult = panel._lastResult; CurrentPass = panel._currentPass; TrialName = panel._trialName;
            TrialActive = panel._trialActive; TrialRecordCount = panel._trialRecordCount; SpeedKmh = panel._speedKmh;
            TargetSpeedKmh = panel._targetSpeedKmh; LoggerStatus = panel._loggerStatus; LogEntryCount = panel._logEntryCount;
            NextProduct = panel._nextProduct; Latitude = panel._latitude; Longitude = panel._longitude;
            HeadingDeg = panel._headingDeg;
        }
    }

    private class FieldContextDrawOp : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly FieldContextSnapshot _s;

        // Colors
        private static readonly SKColor CardBg = new SKColor(40, 40, 48);
        private static readonly SKColor DimText = new SKColor(130, 130, 140);
        private static readonly SKColor BrightText = new SKColor(230, 230, 230);
        private static readonly SKColor AccentCyan = new SKColor(0, 188, 212);
        private static readonly SKColor AccentGreen = new SKColor(76, 175, 80);
        private static readonly SKColor AccentOrange = new SKColor(255, 152, 0);
        private static readonly SKColor AccentRed = new SKColor(244, 67, 54);
        private static readonly SKColor AccentPurple = new SKColor(156, 39, 176);

        public FieldContextDrawOp(Rect bounds, FieldContextSnapshot snapshot)
        {
            _bounds = bounds;
            _s = snapshot;
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

            float y = 8;
            float cardW = (float)_bounds.Width - 16;
            float cardX = 8;

            y = DrawCard(canvas, cardX, y, cardW, "🧭 GPS & HEADING", DrawGpsContent);
            y = DrawCard(canvas, cardX, y + 6, cardW, "📍 POSITION", DrawPositionContent);
            y = DrawCard(canvas, cardX, y + 6, cardW, "🧪 PRODUCT", DrawProductContent);
            y = DrawCard(canvas, cardX, y + 6, cardW, "🚜 PASS", DrawPassContent);
            DrawCard(canvas, cardX, y + 6, cardW, "📝 TRIAL & LOG", DrawTrialContent);

            canvas.Restore();
        }

        private float DrawCard(SKCanvas canvas, float x, float y, float w, string title, Func<SKCanvas, float, float, float, float> drawContent)
        {
            float contentY = y + 24;
            
            // We need to measure the height first, but since immediate mode means we draw linearly, 
            // we will simulate height calculation. Since we know the layout is deterministic, we can just track h.
            float fakeH = drawContent(null, x + 10, contentY, w - 20);
            float totalH = 24 + fakeH + 8;

            using var bgPaint = new SKPaint { Color = CardBg, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(x, y, x + w, y + totalH), 6, 6, bgPaint);

            using var titleFont = new SKPaint { Color = DimText, TextSize = 11, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), IsAntialias = true };
            titleFont.GetFontMetrics(out SKFontMetrics tm);
            canvas.DrawText(title, x + 10, y + 6 - tm.Ascent, titleFont);

            drawContent(canvas, x + 10, contentY, w - 20);

            return y + totalH;
        }

        private float DrawGpsContent(SKCanvas? canvas, float x, float y, float w)
        {
            using var coordFont = new SKPaint { Color = BrightText, TextSize = 12, Typeface = SKTypeface.FromFamilyName("Consolas"), IsAntialias = true };
            using var smallFont = new SKPaint { Color = DimText, TextSize = 11, Typeface = SKTypeface.FromFamilyName("Segoe UI"), IsAntialias = true };
            using var accentFont = new SKPaint { Color = AccentCyan, TextSize = 11, Typeface = SKTypeface.FromFamilyName("Segoe UI"), IsAntialias = true };

            float h = 0;

            if (_s.Latitude == 0 && _s.Longitude == 0)
            {
                if (canvas != null) DrawText(canvas, "Очікування GPS…", x, y, smallFont, out float th);
                return 20;
            }

            if (canvas != null) { DrawText(canvas, $"Lat:  {_s.Latitude:F7}°", x, y + h, coordFont, out _); }
            h += 16;
            if (canvas != null) { DrawText(canvas, $"Lon: {_s.Longitude:F7}°", x, y + h, coordFont, out _); }
            h += 20;

            string compass = GetCompassDirection(_s.HeadingDeg);
            string headingText = $"Курс: {_s.HeadingDeg:F0}° {compass}";
            if (canvas != null) { DrawText(canvas, headingText, x, y + h, accentFont, out _); }

            if (canvas != null)
            {
                float arrowCx = x + w - 24;
                float arrowCy = y + h + 8;
                DrawCompassArrow(canvas, arrowCx, arrowCy, 16, (float)_s.HeadingDeg);
            }
            h += 20;

            return h;
        }

        private float DrawPositionContent(SKCanvas? canvas, float x, float y, float w)
        {
            using var bigFont = new SKPaint { TextSize = 21, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), IsAntialias = true };
            using var smallFont = new SKPaint { Color = DimText, TextSize = 11, Typeface = SKTypeface.FromFamilyName("Segoe UI"), IsAntialias = true };
            using var accentFont = new SKPaint { Color = AccentCyan, TextSize = 11, Typeface = SKTypeface.FromFamilyName("Segoe UI"), IsAntialias = true };

            float h = 0;

            if (_s.LastResult == null)
            {
                if (canvas != null) DrawText(canvas, "Очікування GPS…", x, y, smallFont, out _);
                return 20;
            }

            string posText;
            SKColor stateColor;
            switch (_s.LastResult.State)
            {
                case BoomState.InPlot:
                    posText = _s.LastResult.ActivePlot != null ? $"R{_s.LastResult.ActivePlot.Row + 1}C{_s.LastResult.ActivePlot.Column + 1}" : "—";
                    stateColor = AccentGreen;
                    break;
                case BoomState.ApproachingPlot:
                    posText = "→ Наближення";
                    stateColor = AccentOrange;
                    break;
                case BoomState.LeavingPlot:
                    posText = "← Вихід";
                    stateColor = AccentOrange;
                    break;
                case BoomState.InAlley:
                    posText = "Алея";
                    stateColor = DimText;
                    break;
                default:
                    posText = "Поза поля";
                    stateColor = DimText;
                    break;
            }

            bigFont.Color = stateColor;
            if (canvas != null) DrawText(canvas, posText, x, y, bigFont, out _);
            h += 28;

            string stateLabel = _s.LastResult.State.ToString();
            if (canvas != null) DrawText(canvas, stateLabel, x, y + h, smallFont, out _);
            h += 16;

            if (_s.LastResult.DistanceToBoundaryMeters > 0 && _s.LastResult.State != BoomState.OutsideGrid)
            {
                string dist = $"▸ До межі: {_s.LastResult.DistanceToBoundaryMeters:F1}м";
                if (canvas != null) DrawText(canvas, dist, x, y + h, accentFont, out _);
                h += 16;
            }

            string speedText = $"🏎 {_s.SpeedKmh:F1} км/год";
            SKColor speedColor = BrightText;
            if (_s.TargetSpeedKmh > 0)
            {
                double deviation = Math.Abs(_s.SpeedKmh - _s.TargetSpeedKmh) / _s.TargetSpeedKmh * 100;
                if (deviation > 20) speedColor = AccentRed;
                else if (deviation > 10) speedColor = AccentOrange;
                speedText += $" (ціль: {_s.TargetSpeedKmh:F1})";
            }
            using var speedFont = new SKPaint { Color = speedColor, TextSize = 11, Typeface = SKTypeface.FromFamilyName("Segoe UI"), IsAntialias = true };
            if (canvas != null) DrawText(canvas, speedText, x, y + h, speedFont, out _);
            h += 16;

            return h;
        }

        private float DrawProductContent(SKCanvas? canvas, float x, float y, float w)
        {
            using var bigFont = new SKPaint { TextSize = 24, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), IsAntialias = true };
            using var nextFont = new SKPaint { Color = AccentPurple, TextSize = 15, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), IsAntialias = true };
            using var smallFont = new SKPaint { Color = DimText, TextSize = 11, Typeface = SKTypeface.FromFamilyName("Segoe UI"), IsAntialias = true };

            float h = 0;

            string product = _s.LastResult?.ActiveProduct ?? "—";
            bigFont.Color = product == "—" ? DimText : AccentCyan;
            if (canvas != null) DrawText(canvas, product, x, y, bigFont, out _);
            h += 30;

            if (_s.LastResult?.ActiveProduct != null)
            {
                if (canvas != null) DrawText(canvas, "Поточний продукт", x, y + h, smallFont, out _);
                h += 16;
            }

            if (!string.IsNullOrEmpty(_s.NextProduct))
            {
                if (canvas != null) DrawText(canvas, "Наступний:", x, y + h, smallFont, out _);
                h += 14;
                if (canvas != null) DrawText(canvas, _s.NextProduct, x, y + h, nextFont, out _);
                h += 20;
            }

            return Math.Max(h, 20);
        }

        private float DrawPassContent(SKCanvas? canvas, float x, float y, float w)
        {
            using var medFont = new SKPaint { Color = BrightText, TextSize = 15, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), IsAntialias = true };
            using var smallFont = new SKPaint { Color = DimText, TextSize = 11, Typeface = SKTypeface.FromFamilyName("Segoe UI"), IsAntialias = true };
            
            float h = 0;

            if (_s.CurrentPass == null || !_s.CurrentPass.IsActive)
            {
                if (canvas != null) DrawText(canvas, "Між проходами", x, y, smallFont, out _);
                return 20;
            }

            string arrow = _s.CurrentPass.Direction == PassDirection.Up ? "↑" : "↓";
            if (canvas != null) DrawText(canvas, $"Прохід {_s.CurrentPass.PassNumber} {arrow}", x, y, medFont, out _);
            h += 22;

            if (canvas != null) DrawText(canvas, $"Колонка: {_s.CurrentPass.ColumnIndex + 1}", x, y + h, smallFont, out _);
            h += 16;

            if (_s.CurrentPass.MaxSpeedDeviationPercent > 5)
            {
                using var devFont = new SKPaint { Color = _s.CurrentPass.MaxSpeedDeviationPercent > 15 ? AccentRed : AccentOrange, TextSize = 11, Typeface = SKTypeface.FromFamilyName("Segoe UI"), IsAntialias = true };
                if (canvas != null) DrawText(canvas, $"⚠ Відхилення: {_s.CurrentPass.MaxSpeedDeviationPercent:F0}%", x, y + h, devFont, out _);
                h += 16;
            }

            if (canvas != null) DrawText(canvas, $"Замірів: {_s.CurrentPass.SpeedSampleCount}", x, y + h, smallFont, out _);
            h += 16;

            return h;
        }

        private float DrawTrialContent(SKCanvas? canvas, float x, float y, float w)
        {
            using var medFont = new SKPaint { TextSize = 13, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), IsAntialias = true };
            using var smallFont = new SKPaint { Color = DimText, TextSize = 11, Typeface = SKTypeface.FromFamilyName("Segoe UI"), IsAntialias = true };
            using var sepPaint = new SKPaint { Color = new SKColor(55, 55, 60), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };

            float h = 0;

            if (_s.TrialActive)
            {
                medFont.Color = AccentGreen;
                if (canvas != null) DrawText(canvas, $"● {_s.TrialName ?? "Trial"}", x, y, medFont, out _);
                h += 20;

                if (canvas != null) DrawText(canvas, $"Записів: {_s.TrialRecordCount}", x, y + h, smallFont, out _);
                h += 16;
            }
            else
            {
                if (canvas != null) DrawText(canvas, "Trial: не активний", x, y, smallFont, out _);
                h += 16;
            }

            h += 4;
            if (canvas != null) canvas.DrawLine(x, y + h, x + w, y + h, sepPaint);
            h += 6;

            if (canvas != null) DrawText(canvas, $"Logger: {_s.LoggerStatus}", x, y + h, smallFont, out _);
            h += 14;

            if (_s.LogEntryCount > 0)
            {
                if (canvas != null) DrawText(canvas, $"Записів логу: {_s.LogEntryCount}", x, y + h, smallFont, out _);
                h += 14;
            }

            return h;
        }

        private void DrawText(SKCanvas canvas, string text, float x, float y, SKPaint paint, out float h)
        {
            paint.GetFontMetrics(out SKFontMetrics m);
            canvas.DrawText(text, x, y - m.Ascent, paint);
            h = m.Descent - m.Ascent;
        }

        private void DrawCompassArrow(SKCanvas canvas, float cx, float cy, float size, float headingDeg)
        {
            float rad = (headingDeg - 90) * (float)Math.PI / 180f;
            float dx = (float)Math.Cos(rad) * size / 2;
            float dy = (float)Math.Sin(rad) * size / 2;

            using var pen = new SKPaint { Color = AccentCyan, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
            canvas.DrawLine(cx - dx, cy - dy, cx + dx, cy + dy, pen);

            float headRad1 = rad + 2.6f;
            float headRad2 = rad - 2.6f;
            float hs = size / 3;
            canvas.DrawLine(cx + dx, cy + dy, cx + dx - (float)Math.Cos(headRad1) * hs, cy + dy - (float)Math.Sin(headRad1) * hs, pen);
            canvas.DrawLine(cx + dx, cy + dy, cx + dx - (float)Math.Cos(headRad2) * hs, cy + dy - (float)Math.Sin(headRad2) * hs, pen);
        }

        private static string GetCompassDirection(double deg)
        {
            deg = ((deg % 360) + 360) % 360;
            if (deg < 22.5 || deg >= 337.5) return "N";
            if (deg < 67.5) return "NE";
            if (deg < 112.5) return "E";
            if (deg < 157.5) return "SE";
            if (deg < 202.5) return "S";
            if (deg < 247.5) return "SW";
            if (deg < 292.5) return "W";
            return "NW";
        }
    }
}
