using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

using PlotManager.Core.Models;
using PlotManager.Core.Services;

namespace PlotManager.UI.Controls;

/// <summary>
/// Real-time GDI+ spatial map centered on the sprayer position.
/// Renders the plot grid (color-coded by product), sprayer boom, heading arrow,
/// and active plot highlight. Updated at 10 Hz from PlotModeController.OnSpatialUpdate.
///
/// Coordinate system: world coordinates are in local meters (Easting/Northing
/// relative to the grid origin). The view is always centered on the sprayer.
/// </summary>
public class PlotMapControl : UserControl
{
    // ── Data inputs ──
    private PlotGrid? _grid;
    private TrialMap? _trialMap;
    private PointF _sprayerLocation;      // Local meters (Easting, Northing) relative to grid origin
    private float _sprayerHeading;        // Degrees, 0 = North, 90 = East
    private ushort _valveMask;
    private Plot? _activePlot;
    private BoomState _boomState;
    private double _distanceToBoundary;

    // ── Rendering config ──
    private float _pixelsPerMeter = 8f;
    private const float MinPixelsPerMeter = 2f;
    private const float MaxPixelsPerMeter = 40f;
    private const float BoomWidthMeters = 14f;   // 14 sections, ~1m each
    private const float ZoomStep = 1.2f;

    // Color palette reused from PlotGridPreview
    private static readonly Color[] ProductColors =
    {
        Color.FromArgb(76, 175, 80),    // Green
        Color.FromArgb(33, 150, 243),   // Blue
        Color.FromArgb(255, 152, 0),    // Orange
        Color.FromArgb(156, 39, 176),   // Purple
        Color.FromArgb(244, 67, 54),    // Red
        Color.FromArgb(0, 188, 212),    // Cyan
        Color.FromArgb(255, 235, 59),   // Yellow
        Color.FromArgb(121, 85, 72),    // Brown
        Color.FromArgb(233, 30, 99),    // Pink
        Color.FromArgb(63, 81, 181),    // Indigo
        Color.FromArgb(255, 87, 34),    // Deep Orange
        Color.FromArgb(0, 150, 136),    // Teal
        Color.FromArgb(96, 125, 139),   // Blue Grey
        Color.FromArgb(139, 195, 74),   // Light Green
    };

    private static readonly Color BackgroundColor = Color.FromArgb(30, 30, 35);
    private static readonly Color AlleyColor = Color.FromArgb(45, 45, 50);
    private static readonly Color GridLineColor = Color.FromArgb(60, 60, 65);
    private static readonly Color ControlPlotColor = Color.FromArgb(80, 80, 85);
    private static readonly Color ActiveGlowColor = Color.FromArgb(100, 255, 255, 0);
    private static readonly Color BoomColorOn = Color.FromArgb(0, 255, 200);
    private static readonly Color BoomColorOff = Color.FromArgb(80, 80, 80);
    private static readonly Color HeadingArrowColor = Color.FromArgb(255, 235, 59);
    private static readonly Color TextColor = Color.FromArgb(200, 200, 200);
    private static readonly Color DimTextColor = Color.FromArgb(100, 100, 100);

    private readonly Dictionary<string, Color> _productColorMap = new();

    // P1 FIX: Cached GDI objects — avoid per-frame allocs in hot paint loop
    private readonly Pen _gridBorderPen = new(GridLineColor, 0.05f);
    private readonly Pen _activeGlowPen = new(ActiveGlowColor, 0.4f);
    private readonly SolidBrush _labelBrush = new(Color.FromArgb(180, 255, 255, 255));

    public PlotMapControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = BackgroundColor;
        MouseWheel += OnMouseWheelZoom;
    }

    /// <summary>Sets the plot grid and trial map for rendering.</summary>
    public void SetGridAndMap(PlotGrid? grid, TrialMap? trialMap)
    {
        _grid = grid;
        _trialMap = trialMap;
        BuildColorMap();
        Invalidate();
    }

    /// <summary>
    /// Updates the sprayer state from the latest spatial evaluation.
    /// Call from PlotModeController.OnSpatialUpdate handler.
    /// </summary>
    public void UpdateSprayer(SpatialResult result, AogGpsData? gps, PlotGrid grid)
    {
        if (gps == null) return;

        // Convert GPS lat/lon to local meters relative to grid origin
        double cosLat = Math.Cos(grid.Origin.Latitude * Math.PI / 180.0);
        float easting = (float)((gps.Longitude - grid.Origin.Longitude) * 111320.0 * cosLat);
        float northing = (float)((gps.Latitude - grid.Origin.Latitude) * 110540.0);

        _sprayerLocation = new PointF(easting, northing);
        _sprayerHeading = (float)gps.HeadingDegrees;
        _valveMask = result.ValveMask;
        _activePlot = result.ActivePlot;
        _boomState = result.State;
        _distanceToBoundary = result.DistanceToBoundaryMeters;

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (_grid == null || _grid.Rows == 0)
        {
            DrawPlaceholder(g);
            return;
        }

        // Apply world→screen transform: center on sprayer
        g.TranslateTransform(Width / 2f, Height / 2f);
        g.ScaleTransform(_pixelsPerMeter, -_pixelsPerMeter); // Y-flip (North = up)
        g.TranslateTransform(-_sprayerLocation.X, -_sprayerLocation.Y);

        // Draw plots
        DrawPlots(g);

        // Draw sprayer (heading arrow + boom line with section indicators)
        DrawSprayer(g);

        // Reset transform for HUD overlay
        g.ResetTransform();
        DrawHud(g);
    }

    private void DrawPlots(Graphics g)
    {
        if (_grid == null) return;

        double cosLat = Math.Cos(_grid.Origin.Latitude * Math.PI / 180.0);

        for (int row = 0; row < _grid.Rows; row++)
        {
            for (int col = 0; col < _grid.Columns; col++)
            {
                Plot plot = _grid.Plots[row, col];

                // Convert plot corners to local meters
                float x1 = (float)((plot.SouthWest.Longitude - _grid.Origin.Longitude) * 111320.0 * cosLat);
                float y1 = (float)((plot.SouthWest.Latitude - _grid.Origin.Latitude) * 110540.0);
                float x2 = (float)((plot.NorthEast.Longitude - _grid.Origin.Longitude) * 111320.0 * cosLat);
                float y2 = (float)((plot.NorthEast.Latitude - _grid.Origin.Latitude) * 110540.0);

                float w = x2 - x1;
                float h = y2 - y1;

                // Get product color
                Color plotColor = GetPlotColor(row, col);
                bool isActive = (_activePlot != null && _activePlot.Row == row && _activePlot.Column == col);

                // Active plot glow
                if (isActive)
                {
                    using var glowPen = new Pen(ActiveGlowColor, 0.4f);
                    g.DrawRectangle(glowPen, x1 - 0.3f, y1 - 0.3f, w + 0.6f, h + 0.6f);
                }

                // Plot fill
                using var brush = new SolidBrush(isActive ? Color.FromArgb(220, plotColor) : plotColor);
                g.FillRectangle(brush, x1, y1, w, h);

                // Grid border (P1 FIX: use cached pen)
                g.DrawRectangle(_gridBorderPen, x1, y1, w, h);

                // Plot label (only if zoomed in enough)
                if (_pixelsPerMeter > 6)
                {
                    DrawPlotLabel(g, plot, x1, y1, w, h);
                }
            }
        }
    }

    private void DrawPlotLabel(Graphics g, Plot plot, float x, float y, float w, float h)
    {
        // Save and apply non-flipped transform for text
        var state = g.Save();

        float centerX = x + w / 2;
        float centerY = y + h / 2;

        // Undo the Y-flip for text rendering
        g.TranslateTransform(centerX, centerY);
        g.ScaleTransform(1, -1);

        string label = plot.PlotId;
        float fontSize = Math.Max(0.5f, Math.Min(w / 5f, 1.5f));
        using var font = new Font("Segoe UI", fontSize, GraphicsUnit.World);
        SizeF sz = g.MeasureString(label, font);
        g.DrawString(label, font, _labelBrush, -sz.Width / 2, -sz.Height / 2); // P1 FIX: cached brush

        g.Restore(state);
    }

    private void DrawSprayer(Graphics g)
    {
        float headingRad = _sprayerHeading * (float)Math.PI / 180f;

        var state = g.Save();
        g.TranslateTransform(_sprayerLocation.X, _sprayerLocation.Y);
        g.RotateTransform(-_sprayerHeading); // Negative because Y is flipped

        // Heading arrow (triangle pointing "up" = direction of travel)
        float arrowLen = 3f;    // meters
        float arrowWidth = 1.5f;
        PointF[] arrow =
        {
            new PointF(0, arrowLen),
            new PointF(-arrowWidth / 2, -arrowLen * 0.3f),
            new PointF(arrowWidth / 2, -arrowLen * 0.3f),
        };
        using var arrowBrush = new SolidBrush(HeadingArrowColor);
        g.FillPolygon(arrowBrush, arrow);

        // Boom line: perpendicular to heading, centered on sprayer
        float halfBoom = BoomWidthMeters / 2f;
        float sectionWidth = BoomWidthMeters / 14f;

        for (int i = 0; i < 14; i++)
        {
            float sectionX = -halfBoom + i * sectionWidth;
            bool isOn = (_valveMask & (1 << i)) != 0;

            Color sectionColor = isOn ? BoomColorOn : BoomColorOff;
            using var sectionBrush = new SolidBrush(sectionColor);
            g.FillRectangle(sectionBrush, sectionX, -0.15f, sectionWidth - 0.05f, 0.3f);
        }

        g.Restore(state);
    }

    private void DrawHud(Graphics g)
    {
        // Top-left info overlay
        using var font = new Font("Segoe UI", 9f);
        using var boldFont = new Font("Segoe UI Semibold", 9f);
        using var brush = new SolidBrush(TextColor);
        using var dimBrush = new SolidBrush(DimTextColor);

        float y = 8;
        float x = 8;

        // Boom state
        string stateText = _boomState switch
        {
            BoomState.InPlot => "● IN PLOT",
            BoomState.ApproachingPlot => "▸ APPROACHING",
            BoomState.LeavingPlot => "◂ LEAVING",
            BoomState.InAlley => "─ ALLEY",
            BoomState.OutsideGrid => "○ OUTSIDE",
            _ => "?"
        };
        Color stateColor = _boomState switch
        {
            BoomState.InPlot => Color.FromArgb(76, 175, 80),
            BoomState.ApproachingPlot => Color.FromArgb(255, 193, 7),
            BoomState.LeavingPlot => Color.FromArgb(255, 152, 0),
            _ => DimTextColor,
        };
        using var stateBrush = new SolidBrush(stateColor);
        g.DrawString(stateText, boldFont, stateBrush, x, y);
        y += 20;

        // Active product
        if (_activePlot != null)
        {
            string productInfo = _trialMap?.GetProduct(_activePlot.Row, _activePlot.Column) ?? "—";
            g.DrawString($"Product: {productInfo}", font, brush, x, y);
            y += 18;
        }

        // Distance to boundary
        if (_distanceToBoundary < 100)
        {
            g.DrawString($"Boundary: {_distanceToBoundary:F1} m", font, dimBrush, x, y);
            y += 18;
        }

        // Scale indicator (bottom-right)
        DrawScaleBar(g);
    }

    private void DrawScaleBar(Graphics g)
    {
        float scaleMeters = 10f;
        if (_pixelsPerMeter < 4) scaleMeters = 50f;
        else if (_pixelsPerMeter < 8) scaleMeters = 20f;

        float scalePixels = scaleMeters * _pixelsPerMeter;
        float x = Width - scalePixels - 16;
        float y = Height - 24;

        using var pen = new Pen(TextColor, 2);
        g.DrawLine(pen, x, y, x + scalePixels, y);
        g.DrawLine(pen, x, y - 4, x, y + 4);
        g.DrawLine(pen, x + scalePixels, y - 4, x + scalePixels, y + 4);

        using var font = new Font("Segoe UI", 8f);
        using var brush = new SolidBrush(TextColor);
        string label = $"{scaleMeters:F0} m";
        SizeF sz = g.MeasureString(label, font);
        g.DrawString(label, font, brush, x + (scalePixels - sz.Width) / 2, y - 16);
    }

    private void DrawPlaceholder(Graphics g)
    {
        using var font = new Font("Segoe UI", 12f, FontStyle.Italic);
        using var brush = new SolidBrush(DimTextColor);
        string text = "No grid loaded — configure trial to enable map";
        SizeF sz = g.MeasureString(text, font);
        g.DrawString(text, font, brush, (Width - sz.Width) / 2, (Height - sz.Height) / 2);
    }

    // ── Zoom ──

    private void OnMouseWheelZoom(object? sender, MouseEventArgs e)
    {
        if (e.Delta > 0)
            _pixelsPerMeter = Math.Min(MaxPixelsPerMeter, _pixelsPerMeter * ZoomStep);
        else
            _pixelsPerMeter = Math.Max(MinPixelsPerMeter, _pixelsPerMeter / ZoomStep);

        Invalidate();
    }

    // P1 FIX: Dispose cached GDI objects
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gridBorderPen.Dispose();
            _activeGlowPen.Dispose();
            _labelBrush.Dispose();
        }
        base.Dispose(disposing);
    }

    // ── Product color map ──

    private Color GetPlotColor(int row, int col)
    {
        if (_trialMap == null) return Color.FromArgb(76, 175, 80);

        string? product = _trialMap.GetProduct(row, col);
        if (product == null) return ControlPlotColor;

        if (product.Equals("Control", StringComparison.OrdinalIgnoreCase))
            return ControlPlotColor;

        return _productColorMap.TryGetValue(product, out Color color) ? color : ProductColors[0];
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
}
