using System.Drawing;
using System.Drawing.Drawing2D;

using PlotManager.Core.Models;

namespace PlotManager.UI.Controls;

/// <summary>
/// Custom GDI+ control that renders a top-down 2D schematic of a PlotGrid.
/// Plots are drawn as colored rectangles; alleys/buffers as empty space.
/// When a TrialMap is loaded, plots are color-coded by product.
/// </summary>
public class PlotGridPreview : Control
{
    private PlotGrid? _grid;
    private TrialMap? _trialMap;
    private HardwareRouting? _routing;

    private const int PreviewPadding = 20;
    private const int PlotLabelFontSize = 8;

    // Color palette for up to 14 products
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

    private static readonly Color BufferColor = Color.FromArgb(245, 245, 245);
    private static readonly Color GridLineColor = Color.FromArgb(200, 200, 200);
    private static readonly Color ControlColor = Color.FromArgb(220, 220, 220);
    private static readonly Color DefaultPlotColor = Color.FromArgb(76, 175, 80);

    private readonly Dictionary<string, Color> _productColorMap = new();
    private readonly ToolTip _tooltip = new();
    private string _lastTooltip = string.Empty;

    public PlotGridPreview()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.OptimizedDoubleBuffer, true);

        BackColor = Color.White;
        _tooltip.SetToolTip(this, string.Empty);
    }

    /// <summary>
    /// Sets the grid to display and triggers a repaint.
    /// </summary>
    public void SetGrid(PlotGrid? grid)
    {
        _grid = grid;
        BuildColorMap();
        Invalidate();
    }

    /// <summary>
    /// Sets the trial map for color-coding plots by product.
    /// </summary>
    public void SetTrialMap(TrialMap? trialMap)
    {
        _trialMap = trialMap;
        BuildColorMap();
        Invalidate();
    }

    /// <summary>
    /// Sets the hardware routing for section labels.
    /// </summary>
    public void SetRouting(HardwareRouting? routing)
    {
        _routing = routing;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_grid == null || _grid.Rows == 0 || _grid.Columns == 0)
        {
            DrawPlaceholder(e.Graphics);
            return;
        }

        DrawGrid(e.Graphics);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_grid == null) return;

        var (cellW, cellH, bufW, bufH, offsetX, offsetY) = CalculateLayout();

        for (int row = 0; row < _grid.Rows; row++)
        {
            for (int col = 0; col < _grid.Columns; col++)
            {
                float x = offsetX + col * (cellW + bufW);
                float y = offsetY + (_grid.Rows - 1 - row) * (cellH + bufH);

                if (e.X >= x && e.X <= x + cellW && e.Y >= y && e.Y <= y + cellH)
                {
                    Plot plot = _grid.Plots[row, col];
                    string product = _trialMap?.GetProduct(row, col) ?? "—";
                    string tip = $"{plot.PlotId}\n" +
                                 $"Product: {product}\n" +
                                 $"Size: {plot.WidthMeters:F1} × {plot.LengthMeters:F1} m";

                    if (tip != _lastTooltip)
                    {
                        _lastTooltip = tip;
                        _tooltip.SetToolTip(this, tip);
                    }
                    return;
                }
            }
        }

        if (_lastTooltip != string.Empty)
        {
            _lastTooltip = string.Empty;
            _tooltip.SetToolTip(this, string.Empty);
        }
    }

    private void DrawPlaceholder(Graphics g)
    {
        using var font = new Font("Segoe UI", 11, FontStyle.Italic);
        using var brush = new SolidBrush(Color.FromArgb(150, 150, 150));
        string text = "Generate a grid to see preview";
        SizeF size = g.MeasureString(text, font);
        g.DrawString(text, font, brush,
            (Width - size.Width) / 2,
            (Height - size.Height) / 2);
    }

    private void DrawGrid(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var (cellW, cellH, bufW, bufH, offsetX, offsetY) = CalculateLayout();

        using var labelFont = new Font("Segoe UI", Math.Max(6, Math.Min(PlotLabelFontSize, cellW / 5)), FontStyle.Regular);
        using var labelBrush = new SolidBrush(Color.FromArgb(50, 50, 50));
        using var borderPen = new Pen(GridLineColor, 1);
        using var headerFont = new Font("Segoe UI", 9, FontStyle.Bold);
        using var headerBrush = new SolidBrush(Color.FromArgb(100, 100, 100));

        // Draw column headers
        for (int col = 0; col < _grid!.Columns; col++)
        {
            float x = offsetX + col * (cellW + bufW) + cellW / 2;
            string text = $"C{col + 1}";
            SizeF sz = g.MeasureString(text, headerFont);
            g.DrawString(text, headerFont, headerBrush, x - sz.Width / 2, offsetY - sz.Height - 4);
        }

        // Draw row headers
        for (int row = 0; row < _grid.Rows; row++)
        {
            float y = offsetY + (_grid.Rows - 1 - row) * (cellH + bufH) + cellH / 2;
            string text = $"R{row + 1}";
            SizeF sz = g.MeasureString(text, headerFont);
            g.DrawString(text, headerFont, headerBrush, offsetX - sz.Width - 6, y - sz.Height / 2);
        }

        // Draw plots
        for (int row = 0; row < _grid.Rows; row++)
        {
            for (int col = 0; col < _grid.Columns; col++)
            {
                float x = offsetX + col * (cellW + bufW);
                float y = offsetY + (_grid.Rows - 1 - row) * (cellH + bufH);

                Plot plot = _grid.Plots[row, col];
                Color plotColor = GetPlotColor(row, col);

                // Draw plot rectangle with slight rounding
                using var fillBrush = new SolidBrush(plotColor);
                var rect = new RectangleF(x, y, cellW, cellH);

                if (cellW > 8 && cellH > 8)
                {
                    using var path = RoundedRect(rect, 3);
                    g.FillPath(fillBrush, path);
                    g.DrawPath(borderPen, path);
                }
                else
                {
                    g.FillRectangle(fillBrush, rect);
                    g.DrawRectangle(borderPen, x, y, cellW, cellH);
                }

                // Draw plot label if there's enough space
                if (cellW > 30 && cellH > 20)
                {
                    string label = plot.PlotId;
                    SizeF labelSize = g.MeasureString(label, labelFont);

                    if (labelSize.Width < cellW - 4)
                    {
                        // Use white text on dark backgrounds, dark on light
                        float brightness = plotColor.GetBrightness();
                        using var textBrush = new SolidBrush(brightness < 0.5f ? Color.White : Color.FromArgb(50, 50, 50));
                        g.DrawString(label, labelFont, textBrush,
                            x + (cellW - labelSize.Width) / 2,
                            y + (cellH - labelSize.Height) / 2);
                    }
                }
            }
        }

        // Draw legend if trial map is loaded
        if (_trialMap != null && _productColorMap.Count > 0)
        {
            DrawLegend(g, offsetX, offsetY + _grid.Rows * (cellH + bufH) + 10);
        }
    }

    private void DrawLegend(Graphics g, float x, float y)
    {
        using var font = new Font("Segoe UI", 8);
        using var textBrush = new SolidBrush(Color.FromArgb(60, 60, 60));
        float legendX = x;

        foreach (var (product, color) in _productColorMap)
        {
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, legendX, y, 12, 12);
            g.DrawRectangle(Pens.Gray, legendX, y, 12, 12);
            g.DrawString(product, font, textBrush, legendX + 16, y - 1);

            SizeF textSize = g.MeasureString(product, font);
            legendX += 16 + textSize.Width + 12;

            // Wrap to next line if needed
            if (legendX > Width - PreviewPadding)
            {
                legendX = x;
                y += 18;
            }
        }
    }

    private (float cellW, float cellH, float bufW, float bufH, float offsetX, float offsetY) CalculateLayout()
    {
        if (_grid == null) return (0, 0, 0, 0, 0, 0);

        float availableW = Width - PreviewPadding * 2 - 30;  // 30 for row headers
        float availableH = Height - PreviewPadding * 2 - 40;  // 40 for col headers + legend

        // Calculate proportional sizes
        double totalPhysicalW = _grid.Columns * _grid.PlotWidthMeters +
                                (_grid.Columns - 1) * _grid.BufferWidthMeters;
        double totalPhysicalH = _grid.Rows * _grid.PlotLengthMeters +
                                (_grid.Rows - 1) * _grid.BufferLengthMeters;

        float scaleX = (float)(availableW / totalPhysicalW);
        float scaleY = (float)(availableH / totalPhysicalH);
        float scale = Math.Min(scaleX, scaleY);

        float cellW = (float)(_grid.PlotWidthMeters * scale);
        float cellH = (float)(_grid.PlotLengthMeters * scale);
        float bufW = (float)(_grid.BufferWidthMeters * scale);
        float bufH = (float)(_grid.BufferLengthMeters * scale);

        float totalDrawW = _grid.Columns * cellW + (_grid.Columns - 1) * bufW;
        float totalDrawH = _grid.Rows * cellH + (_grid.Rows - 1) * bufH;

        float offsetX = 30 + (availableW - totalDrawW) / 2 + PreviewPadding;
        float offsetY = 20 + (availableH - totalDrawH) / 2 + PreviewPadding;

        return (cellW, cellH, bufW, bufH, offsetX, offsetY);
    }

    private Color GetPlotColor(int row, int col)
    {
        if (_trialMap == null) return DefaultPlotColor;

        string? product = _trialMap.GetProduct(row, col);
        if (product == null) return ControlColor;

        if (product.Equals("Control", StringComparison.OrdinalIgnoreCase))
            return ControlColor;

        return _productColorMap.TryGetValue(product, out Color color) ? color : DefaultPlotColor;
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

        // Add Control with its special color
        if (_trialMap.Products.Any(p => p.Equals("Control", StringComparison.OrdinalIgnoreCase)))
        {
            _productColorMap["Control"] = ControlColor;
        }
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}
