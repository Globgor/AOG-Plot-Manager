using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using PlotManager.Core.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlotManager.UI.Views.Controls;

public partial class PlotGridPreview : UserControl
{
    private PlotGrid? _grid;
    private TrialMap? _trialMap;
    // We can support routing for future expansions if needed
    private HardwareRouting? _routing;

    private readonly Dictionary<string, SKColor> _productColorMap = new();
    
    private static readonly SKColor[] ProductColors =
    {
        new(76, 175, 80), new(33, 150, 243), new(255, 152, 0), new(156, 39, 176),
        new(244, 67, 54), new(0, 188, 212), new(255, 235, 59), new(121, 85, 72),
        new(233, 30, 99), new(63, 81, 181), new(255, 87, 34), new(0, 150, 136),
        new(96, 125, 139), new(139, 195, 74)
    };
    private static readonly SKColor ControlColor = new(220, 220, 220);
    private static readonly SKColor DefaultPlotColor = new(76, 175, 80);
    
    // Tooltip logic
    private string _lastTooltip = string.Empty;

    public PlotGridPreview()
    {
        InitializeComponent();
        Background = Brushes.Transparent; // Important for HitTest to work if needed

        PointerMoved += (s, e) =>
        {
            var p = e.GetPosition(this);
            UpdateTooltip((float)p.X, (float)p.Y);
        };
    }

    public void SetGrid(PlotGrid? grid)
    {
        _grid = grid;
        BuildColorMap();
        InvalidateVisual();
    }

    public void SetTrialMap(TrialMap? map)
    {
        _trialMap = map;
        BuildColorMap();
        InvalidateVisual();
    }

    private void BuildColorMap()
    {
        _productColorMap.Clear();
        if (_trialMap == null) return;

        int idx = 0;
        foreach (var product in _trialMap.Products.OrderBy(p => p))
        {
            if (!product.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                _productColorMap[product] = ProductColors[idx % ProductColors.Length];
                idx++;
            }
        }
        if (_trialMap.Products.Any(p => p.Equals("Control", StringComparison.OrdinalIgnoreCase)))
            _productColorMap["Control"] = ControlColor;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var custom = new PlotGridDrawOp(Bounds, _grid, _trialMap, _productColorMap);
        context.Custom(custom);
    }

    private void UpdateTooltip(float mouseX, float mouseY)
    {
        if (_grid == null) return;
        var layout = PlotGridDrawOp.CalculateLayout(Bounds, _grid);
        
        for (int row = 0; row < _grid.Rows; row++)
        {
            for (int col = 0; col < _grid.Columns; col++)
            {
                float x = layout.OffsetX + col * (layout.CellW + layout.BufW);
                float y = layout.OffsetY + (_grid.Rows - 1 - row) * (layout.CellH + layout.BufH);

                if (mouseX >= x && mouseX <= x + layout.CellW && mouseY >= y && mouseY <= y + layout.CellH)
                {
                    Plot plot = _grid.Plots[row, col];
                    string product = _trialMap?.GetProduct(row, col) ?? "—";
                    string tip = $"{plot.PlotId}\nProduct: {product}\nSize: {plot.WidthMeters:F1} × {plot.LengthMeters:F1} m";
                    
                    if (tip != _lastTooltip)
                    {
                        _lastTooltip = tip;
                        ToolTip.SetTip(this, tip);
                    }
                    return;
                }
            }
        }

        if (_lastTooltip != string.Empty)
        {
            _lastTooltip = string.Empty;
            ToolTip.SetTip(this, null);
        }
    }
}

public class PlotGridDrawOp : ICustomDrawOperation
{
    private readonly Rect _bounds;
    private readonly PlotGrid? _grid;
    private readonly TrialMap? _trialMap;
    private readonly Dictionary<string, SKColor> _colorMap;

    public PlotGridDrawOp(Rect bounds, PlotGrid? grid, TrialMap? trialMap, Dictionary<string, SKColor> colorMap)
    {
        _bounds = bounds;
        _grid = grid;
        _trialMap = trialMap;
        _colorMap = colorMap;
    }

    public Rect Bounds => _bounds;

    public void Dispose() { }

    public bool Equals(ICustomDrawOperation? other) => false;

    public bool HitTest(Point p) => false; // We use PointerMoved on UserControl instead

    public void Render(ImmediateDrawingContext context)
    {
        var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (leaseFeature == null) return;

        using var lease = leaseFeature.Lease();
        SKCanvas canvas = lease.SkCanvas;
        
        // Setup anti-aliasing
        canvas.Save();

        if (_grid == null || _grid.Rows == 0 || _grid.Columns == 0)
        {
            DrawPlaceholder(canvas);
            canvas.Restore();
            return;
        }

        DrawGrid(canvas);
        canvas.Restore();
    }

    private void DrawPlaceholder(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(150, 150, 150),
            IsAntialias = true,
            TextSize = 14,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic)
        };
        string text = "Generate a grid to see preview";
        float width = paint.MeasureText(text);
        
        // Font metrics for vertical centering
        paint.GetFontMetrics(out SKFontMetrics metrics);
        float height = metrics.Descent - metrics.Ascent;

        canvas.DrawText(text, (float)(_bounds.Width - width) / 2, (float)(_bounds.Height + height) / 2, paint);
    }

    private void DrawGrid(SKCanvas canvas)
    {
        var layout = CalculateLayout(_bounds, _grid!);

        using var borderPaint = new SKPaint { Color = new SKColor(200, 200, 200), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        
        using var headerPaint = new SKPaint { Color = new SKColor(100, 100, 100), TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) };
        float headerYOffset = 4;
        
        // Draw column headers
        for (int col = 0; col < _grid!.Columns; col++)
        {
            float x = layout.OffsetX + col * (layout.CellW + layout.BufW) + layout.CellW / 2;
            string text = $"C{col + 1}";
            float w = headerPaint.MeasureText(text);
            canvas.DrawText(text, x - w / 2, layout.OffsetY - headerYOffset, headerPaint);
        }

        // Draw row headers
        for (int row = 0; row < _grid.Rows; row++)
        {
            float y = layout.OffsetY + (_grid.Rows - 1 - row) * (layout.CellH + layout.BufH) + layout.CellH / 2;
            string text = $"R{row + 1}";
            float w = headerPaint.MeasureText(text);
            headerPaint.GetFontMetrics(out SKFontMetrics metrics);
            canvas.DrawText(text, layout.OffsetX - w - 6, y - (metrics.Ascent + metrics.Descent) / 2, headerPaint);
        }

        // Font calculation for plots
        float fontSize = Math.Max(8, Math.Min(12, layout.CellW / 5));
        using var textPaint = new SKPaint { TextSize = fontSize, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") };

        for (int row = 0; row < _grid.Rows; row++)
        {
            for (int col = 0; col < _grid.Columns; col++)
            {
                float x = layout.OffsetX + col * (layout.CellW + layout.BufW);
                float y = layout.OffsetY + (_grid.Rows - 1 - row) * (layout.CellH + layout.BufH);

                Plot plot = _grid.Plots[row, col];
                SKColor plotColor = GetPlotColor(row, col);

                fillPaint.Color = plotColor;
                var rect = new SKRect(x, y, x + layout.CellW, y + layout.CellH);

                if (layout.CellW > 8 && layout.CellH > 8)
                {
                    canvas.DrawRoundRect(rect, 3, 3, fillPaint);
                    canvas.DrawRoundRect(rect, 3, 3, borderPaint);
                }
                else
                {
                    canvas.DrawRect(rect, fillPaint);
                    canvas.DrawRect(rect, borderPaint);
                }

                if (layout.CellW > 30 && layout.CellH > 20)
                {
                    string label = plot.PlotId;
                    float labelW = textPaint.MeasureText(label);
                    
                    if (labelW < layout.CellW - 4)
                    {
                        // Calculate brightness for contrast (naive formula)
                        float luma = (0.299f * plotColor.Red + 0.587f * plotColor.Green + 0.114f * plotColor.Blue) / 255f;
                        textPaint.Color = luma < 0.5f ? SKColors.White : new SKColor(50, 50, 50);
                        textPaint.GetFontMetrics(out SKFontMetrics metrics);
                        
                        canvas.DrawText(label, x + (layout.CellW - labelW) / 2, y + layout.CellH / 2 - (metrics.Ascent + metrics.Descent) / 2, textPaint);
                    }
                }
            }
        }

        if (_trialMap != null && _colorMap.Count > 0)
        {
            DrawLegend(canvas, layout.OffsetX, layout.OffsetY + _grid.Rows * (layout.CellH + layout.BufH) + 15, _bounds);
        }
    }

    private void DrawLegend(SKCanvas canvas, float x, float y, Rect bounds)
    {
        using var fontPaint = new SKPaint { Color = new SKColor(60, 60, 60), TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") };
        using var borderPaint = new SKPaint { Color = SKColors.Gray, Style = SKPaintStyle.Stroke };
        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill };
        
        float legendX = x;
        fontPaint.GetFontMetrics(out SKFontMetrics metrics);

        foreach (var (product, color) in _colorMap)
        {
            fillPaint.Color = color;
            var box = new SKRect(legendX, y, legendX + 12, y + 12);
            canvas.DrawRect(box, fillPaint);
            canvas.DrawRect(box, borderPaint);
            
            canvas.DrawText(product, legendX + 18, y + 12 - metrics.Descent, fontPaint);
            float w = fontPaint.MeasureText(product);
            legendX += 18 + w + 16;
            
            if (legendX > bounds.Width - 20) // Wrap
            {
                legendX = x;
                y += 20;
            }
        }
    }

    private SKColor GetPlotColor(int row, int col)
    {
        if (_trialMap == null) return new SKColor(76, 175, 80); // Default

        string? product = _trialMap.GetProduct(row, col);
        if (product == null || product.Equals("Control", StringComparison.OrdinalIgnoreCase))
            return new SKColor(220, 220, 220); // Control
            
        return _colorMap.TryGetValue(product, out SKColor color) ? color : new SKColor(76, 175, 80);
    }

    public static LayoutInfo CalculateLayout(Rect bounds, PlotGrid grid)
    {
        float availableW = (float)bounds.Width - 40 - 30; // 30 row headers
        float availableH = (float)bounds.Height - 40 - 50; // 50 headers + legend
        
        double totalPhysicalW = grid.Columns * grid.PlotWidthMeters + (grid.Columns - 1) * grid.BufferWidthMeters;
        double totalPhysicalH = grid.Rows * grid.PlotLengthMeters + (grid.Rows - 1) * grid.BufferLengthMeters;

        float scaleX = (float)(availableW / totalPhysicalW);
        float scaleY = (float)(availableH / totalPhysicalH);
        float scale = Math.Min(scaleX, scaleY);

        var l = new LayoutInfo
        {
            CellW = (float)(grid.PlotWidthMeters * scale),
            CellH = (float)(grid.PlotLengthMeters * scale),
            BufW = (float)(grid.BufferWidthMeters * scale),
            BufH = (float)(grid.BufferLengthMeters * scale)
        };

        float totalDrawW = grid.Columns * l.CellW + (grid.Columns - 1) * l.BufW;
        float totalDrawH = grid.Rows * l.CellH + (grid.Rows - 1) * l.BufH;

        l.OffsetX = 30 + (availableW - totalDrawW) / 2 + 20;
        l.OffsetY = 20 + (availableH - totalDrawH) / 2 + 20;

        return l;
    }

    public class LayoutInfo
    {
        public float CellW { get; set; }
        public float CellH { get; set; }
        public float BufW { get; set; }
        public float BufH { get; set; }
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
    }
}
