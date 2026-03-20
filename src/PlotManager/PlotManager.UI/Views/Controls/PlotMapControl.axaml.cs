using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using PlotManager.Core.Models;
using PlotManager.Core.Protocol;
using PlotManager.Core.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlotManager.UI.Views.Controls;

public partial class PlotMapControl : UserControl
{
    private PlotGrid? _grid;
    private TrialMap? _trialMap;
    
    private SKPoint _sprayerLocation;
    private float _sprayerHeading;
    private ushort _valveMask;
    private Plot? _activePlot;
    private BoomState _boomState;
    private double _distanceToBoundary;

    private float _pixelsPerMeter = 8f;
    private const float MinPixelsPerMeter = 2f;
    private const float MaxPixelsPerMeter = 40f;
    private const float BoomWidthMeters = 14f;
    private const float ZoomStep = 1.2f;

    private readonly Dictionary<string, SKColor> _productColorMap = new();

    private static readonly SKColor[] ProductColors =
    {
        new(76, 175, 80), new(33, 150, 243), new(255, 152, 0), new(156, 39, 176),
        new(244, 67, 54), new(0, 188, 212), new(255, 235, 59), new(121, 85, 72),
        new(233, 30, 99), new(63, 81, 181), new(255, 87, 34), new(0, 150, 136),
        new(96, 125, 139), new(139, 195, 74)
    };
    
    private static readonly SKColor ControlPlotColor = new(80, 80, 85);

    public PlotMapControl()
    {
        InitializeComponent();
        Background = new SolidColorBrush(Color.Parse("#1E1E23"));

        PointerWheelChanged += OnMouseWheelZoom;
    }

    public void SetGridAndMap(PlotGrid? grid, TrialMap? trialMap)
    {
        _grid = grid;
        _trialMap = trialMap;
        BuildColorMap();
        InvalidateVisual();
    }

    public void UpdateSprayer(SpatialResult result, AogGpsData? gps, PlotGrid grid)
    {
        if (gps == null) return;

        double cosLat = Math.Cos(grid.Origin.Latitude * Math.PI / 180.0);
        float easting = (float)((gps.Longitude - grid.Origin.Longitude) * 111320.0 * cosLat);
        float northing = (float)((gps.Latitude - grid.Origin.Latitude) * 110540.0);

        _sprayerLocation = new SKPoint(easting, northing);
        _sprayerHeading = (float)gps.HeadingDegrees;
        _valveMask = result.ValveMask;
        _activePlot = result.ActivePlot;
        _boomState = result.State;
        _distanceToBoundary = result.DistanceToBoundaryMeters;

        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);
    }

    private void OnMouseWheelZoom(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y > 0)
            _pixelsPerMeter = Math.Min(MaxPixelsPerMeter, _pixelsPerMeter * ZoomStep);
        else if (e.Delta.Y < 0)
            _pixelsPerMeter = Math.Max(MinPixelsPerMeter, _pixelsPerMeter / ZoomStep);

        InvalidateVisual();
    }

    private void BuildColorMap()
    {
        _productColorMap.Clear();
        if (_trialMap == null) return;

        int idx = 0;
        foreach (string product in _trialMap.Products.OrderBy(p => p))
        {
            if (!product.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                _productColorMap[product] = ProductColors[idx % ProductColors.Length];
                idx++;
            }
        }
    }

    private SKColor GetPlotColor(int row, int col)
    {
        if (_trialMap == null) return new SKColor(76, 175, 80);

        string? product = _trialMap.GetProduct(row, col);
        if (product == null || product.Equals("Control", StringComparison.OrdinalIgnoreCase))
            return ControlPlotColor;

        return _productColorMap.TryGetValue(product, out SKColor color) ? color : ProductColors[0];
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var snapshot = new RenderSnapshot(
            _grid, _trialMap, _sprayerLocation, _sprayerHeading, _valveMask,
            _activePlot, _boomState, _distanceToBoundary, _pixelsPerMeter,
            (row, col) => GetPlotColor(row, col)
        );

        context.Custom(new PlotMapDrawOp(Bounds, snapshot));
    }

    private class RenderSnapshot
    {
        public PlotGrid? Grid { get; }
        public TrialMap? TrialMap { get; }
        public SKPoint SprayerLocation { get; }
        public float SprayerHeading { get; }
        public ushort ValveMask { get; }
        public Plot? ActivePlot { get; }
        public BoomState BoomState { get; }
        public double DistanceToBoundary { get; }
        public float PixelsPerMeter { get; }
        public Func<int, int, SKColor> GetPlotColor { get; }

        public RenderSnapshot(PlotGrid? grid, TrialMap? map, SKPoint loc, float heading, ushort mask, Plot? plot, BoomState state, double dist, float ppm, Func<int, int, SKColor> colorFunc)
        {
            Grid = grid; TrialMap = map; SprayerLocation = loc; SprayerHeading = heading;
            ValveMask = mask; ActivePlot = plot; BoomState = state; DistanceToBoundary = dist;
            PixelsPerMeter = ppm; GetPlotColor = colorFunc;
        }
    }

    private class PlotMapDrawOp : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly RenderSnapshot _state;

        public PlotMapDrawOp(Rect bounds, RenderSnapshot state)
        {
            _bounds = bounds;
            _state = state;
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

            if (_state.Grid == null || _state.Grid.Rows == 0)
            {
                DrawPlaceholder(canvas);
                canvas.Restore();
                return;
            }

            // World transform
            canvas.Translate((float)_bounds.Width / 2f, (float)_bounds.Height / 2f);
            canvas.Scale(_state.PixelsPerMeter, -_state.PixelsPerMeter);
            canvas.Translate(-_state.SprayerLocation.X, -_state.SprayerLocation.Y);

            DrawPlots(canvas);
            DrawSprayer(canvas);

            canvas.Restore();
            DrawHud(canvas);
        }

        private void DrawPlaceholder(SKCanvas canvas)
        {
            using var paint = new SKPaint { Color = new SKColor(100, 100, 100), TextSize = 16, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic) };
            string text = "No grid loaded — configure trial to enable map";
            float w = paint.MeasureText(text);
            canvas.DrawText(text, (float)(_bounds.Width - w) / 2, (float)(_bounds.Height) / 2, paint);
        }

        private void DrawPlots(SKCanvas canvas)
        {
            using var gridBorderPaint = new SKPaint { Color = new SKColor(60, 60, 65), Style = SKPaintStyle.Stroke, StrokeWidth = 0.05f, IsAntialias = true };
            using var activeGlowPaint = new SKPaint { Color = new SKColor(100, 255, 255, 0), Style = SKPaintStyle.Stroke, StrokeWidth = 0.4f, IsAntialias = true };
            using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
            using var labelPaint = new SKPaint { Color = new SKColor(180, 255, 255, 255), IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") };

            double origLon = _state.Grid!.Origin.Longitude;
            double origLat = _state.Grid!.Origin.Latitude;
            double cosLat = Math.Cos(origLat * Math.PI / 180.0);

            for (int row = 0; row < _state.Grid.Rows; row++)
            {
                for (int col = 0; col < _state.Grid.Columns; col++)
                {
                    Plot plot = _state.Grid.Plots[row, col];

                    float x1 = (float)((plot.SouthWest.Longitude - origLon) * 111320.0 * cosLat);
                    float y1 = (float)((plot.SouthWest.Latitude - origLat) * 110540.0);
                    float x2 = (float)((plot.NorthEast.Longitude - origLon) * 111320.0 * cosLat);
                    float y2 = (float)((plot.NorthEast.Latitude - origLat) * 110540.0);

                    float w = x2 - x1;
                    float h = y2 - y1;

                    SKColor plotColor = _state.GetPlotColor(row, col);
                    bool isActive = (_state.ActivePlot != null && _state.ActivePlot.Row == row && _state.ActivePlot.Column == col);

                    if (isActive)
                    {
                        canvas.DrawRect(x1 - 0.3f, y1 - 0.3f, w + 0.6f, h + 0.6f, activeGlowPaint);
                        fillPaint.Color = new SKColor(plotColor.Red, plotColor.Green, plotColor.Blue, 220);
                    }
                    else
                    {
                        fillPaint.Color = plotColor;
                    }

                    canvas.DrawRect(x1, y1, w, h, fillPaint);
                    canvas.DrawRect(x1, y1, w, h, gridBorderPaint);

                    if (_state.PixelsPerMeter > 6)
                    {
                        canvas.Save();
                        float cx = x1 + w / 2;
                        float cy = y1 + h / 2;
                        canvas.Translate(cx, cy);
                        canvas.Scale(1, -1); // Unflip Y for text

                        float fontSize = Math.Max(0.5f, Math.Min(w / 5f, 1.5f));
                        labelPaint.TextSize = fontSize;
                        float tw = labelPaint.MeasureText(plot.PlotId);
                        labelPaint.GetFontMetrics(out SKFontMetrics metrics);
                        
                        canvas.DrawText(plot.PlotId, -tw / 2, -(metrics.Ascent + metrics.Descent) / 2, labelPaint);
                        canvas.Restore();
                    }
                }
            }
        }

        private void DrawSprayer(SKCanvas canvas)
        {
            canvas.Save();
            canvas.Translate(_state.SprayerLocation.X, _state.SprayerLocation.Y);
            canvas.RotateDegrees(-_state.SprayerHeading);

            // Heading arrow
            float arrowLen = 3f;
            float arrowWidth = 1.5f;
            var path = new SKPath();
            path.MoveTo(0, arrowLen);
            path.LineTo(-arrowWidth / 2, -arrowLen * 0.3f);
            path.LineTo(arrowWidth / 2, -arrowLen * 0.3f);
            path.Close();

            using var arrowPaint = new SKPaint { Color = new SKColor(255, 235, 59), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawPath(path, arrowPaint);

            // Boom
            float halfBoom = PlotMapControl.BoomWidthMeters / 2f;
            float sectionWidth = PlotMapControl.BoomWidthMeters / 14f;

            using var onPaint = new SKPaint { Color = new SKColor(0, 255, 200), Style = SKPaintStyle.Fill, IsAntialias = true };
            using var offPaint = new SKPaint { Color = new SKColor(80, 80, 80), Style = SKPaintStyle.Fill, IsAntialias = true };

            for (int i = 0; i < 14; i++)
            {
                float sectionX = -halfBoom + i * sectionWidth;
                bool isOn = (_state.ValveMask & (1 << i)) != 0;
                canvas.DrawRect(sectionX, -0.15f, sectionWidth - 0.05f, 0.3f, isOn ? onPaint : offPaint);
            }

            canvas.Restore();
        }

        private void DrawHud(SKCanvas canvas)
        {
            using var fontPaint = new SKPaint { Color = new SKColor(200, 200, 200), TextSize = 12, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") };
            using var boldFontPaint = new SKPaint { TextSize = 12, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) };
            
            float y = 20;
            float x = 10;

            string stateText = _state.BoomState switch
            {
                BoomState.InPlot => "● IN PLOT",
                BoomState.ApproachingPlot => "▸ APPROACHING",
                BoomState.LeavingPlot => "◂ LEAVING",
                BoomState.InAlley => "─ ALLEY",
                BoomState.OutsideGrid => "○ OUTSIDE",
                _ => "?"
            };
            
            boldFontPaint.Color = _state.BoomState switch
            {
                BoomState.InPlot => new SKColor(76, 175, 80),
                BoomState.ApproachingPlot => new SKColor(255, 193, 7),
                BoomState.LeavingPlot => new SKColor(255, 152, 0),
                _ => new SKColor(100, 100, 100)
            };

            canvas.DrawText(stateText, x, y, boldFontPaint);
            y += 20;

            if (_state.ActivePlot != null)
            {
                string product = _state.TrialMap?.GetProduct(_state.ActivePlot.Row, _state.ActivePlot.Column) ?? "—";
                canvas.DrawText($"Product: {product}", x, y, fontPaint);
                y += 20;
            }

            if (_state.DistanceToBoundary < 100)
            {
                fontPaint.Color = new SKColor(100, 100, 100);
                canvas.DrawText($"Boundary: {_state.DistanceToBoundary:F1} m", x, y, fontPaint);
            }

            // Scale Bar
            float scaleMeters = 10f;
            if (_state.PixelsPerMeter < 4) scaleMeters = 50f;
            else if (_state.PixelsPerMeter < 8) scaleMeters = 20f;

            float scalePixels = scaleMeters * _state.PixelsPerMeter;
            float scaleX = (float)_bounds.Width - scalePixels - 20;
            float scaleY = (float)_bounds.Height - 30;

            using var scalePen = new SKPaint { Color = new SKColor(200, 200, 200), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
            canvas.DrawLine(scaleX, scaleY, scaleX + scalePixels, scaleY, scalePen);
            canvas.DrawLine(scaleX, scaleY - 4, scaleX, scaleY + 4, scalePen);
            canvas.DrawLine(scaleX + scalePixels, scaleY - 4, scaleX + scalePixels, scaleY + 4, scalePen);

            fontPaint.Color = new SKColor(200, 200, 200);
            string label = $"{scaleMeters:F0} m";
            float w = fontPaint.MeasureText(label);
            canvas.DrawText(label, scaleX + (scalePixels - w) / 2, scaleY - 8, fontPaint);
        }
    }
}
