using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PlotManager.UI.Controls;

/// <summary>
/// Displays 14 valve LED indicators (ON/OFF) and the active product + target rate.
/// Updated at 10 Hz from PlotModeController.OnValveMaskSent.
/// </summary>
public class BoomStatusPanel : UserControl
{
    private ushort _valveMask;
    private string _activeProduct = "—";
    private string _targetRate = "—";

    private const int LedSize = 22;
    private const int LedSpacing = 6;
    private const int LabelHeight = 20;

    private static readonly Color LedOnColor = Color.FromArgb(76, 175, 80);
    private static readonly Color LedOffColor = Color.FromArgb(60, 60, 60);
    private static readonly Color LedBorderColor = Color.FromArgb(100, 100, 100);
    private static readonly Color BackgroundColor = Color.FromArgb(35, 35, 40);
    private static readonly Color TextColor = Color.FromArgb(220, 220, 220);
    private static readonly Color ProductColor = Color.FromArgb(0, 188, 212);

    // P2 FIX: Cached GDI objects — avoid per-frame allocs
    private readonly SolidBrush _ledOnBrush = new(LedOnColor);
    private readonly SolidBrush _ledOffBrush = new(LedOffColor);
    private readonly SolidBrush _glowBrush = new(Color.FromArgb(40, LedOnColor));
    private readonly Pen _ledBorderPen = new(LedBorderColor, 1);
    // GDI-FIX: cache fonts+brushes — were created every OnPaint at 10 Hz = 40 allocs/sec
    private readonly Font _labelFont        = new("Segoe UI", 7f);
    private readonly SolidBrush _labelBrush = new(Color.FromArgb(140, 140, 140));
    private readonly Font _productFont      = new("Segoe UI Semibold", 11f);
    private readonly Font _rateFont         = new("Segoe UI", 9f);
    private readonly SolidBrush _productBrush = new(ProductColor);
    private readonly SolidBrush _rateBrush    = new(TextColor);

    public BoomStatusPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = BackgroundColor;
        Height = 60;
    }

    /// <summary>
    /// Updates the 14-bit valve mask (bit N = valve N: 1=open, 0=closed).
    /// </summary>
    public void UpdateValveMask(ushort mask)
    {
        if (_valveMask == mask) return;
        _valveMask = mask;
        Invalidate();
    }

    /// <summary>
    /// Updates the product label and target rate display.
    /// </summary>
    public void UpdateProduct(string? product, string? rate)
    {
        string newProduct = product ?? "—";
        string newRate = rate ?? "—";
        if (_activeProduct == newProduct && _targetRate == newRate) return;
        _activeProduct = newProduct;
        _targetRate = newRate;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Section labels — GDI-FIX: use cached font/brush
        Font labelFont     = _labelFont;
        SolidBrush labelBrush = _labelBrush;

        // Calculate LED positions centered horizontally
        int totalLedsWidth = 14 * LedSize + 13 * LedSpacing;
        float startX = Math.Max(8, (Width / 2f) - (totalLedsWidth / 2f));
        float ledY = 8;

        // Draw 14 valve LEDs
        for (int i = 0; i < 14; i++)
        {
            float x = startX + i * (LedSize + LedSpacing);
            bool isOn = (_valveMask & (1 << i)) != 0;

            // P2 FIX: Use cached brushes
            SolidBrush fillBrush = isOn ? _ledOnBrush : _ledOffBrush;
            g.FillEllipse(fillBrush, x, ledY, LedSize, LedSize);

            // Glow effect when ON
            if (isOn)
            {
                g.FillEllipse(_glowBrush, x - 3, ledY - 3, LedSize + 6, LedSize + 6);
                g.FillEllipse(fillBrush, x, ledY, LedSize, LedSize);
            }

            g.DrawEllipse(_ledBorderPen, x, ledY, LedSize, LedSize);

            // Section number
            string label = (i + 1).ToString();
            SizeF sz = g.MeasureString(label, labelFont);
            g.DrawString(label, labelFont, labelBrush,
                x + (LedSize - sz.Width) / 2,
                ledY + LedSize + 2);
        }

        // Product + Rate label (right side) — GDI-FIX: use cached fonts/brushes
        float labelX = startX + totalLedsWidth + 20;

        g.DrawString(_activeProduct, _productFont, _productBrush, labelX, ledY);
        g.DrawString(_targetRate, _rateFont, _rateBrush, labelX, ledY + 22);
    }

    // P2 FIX + GDI-FIX: Dispose all cached GDI objects
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ledOnBrush.Dispose();
            _ledOffBrush.Dispose();
            _glowBrush.Dispose();
            _ledBorderPen.Dispose();
            _labelFont.Dispose();
            _labelBrush.Dispose();
            _productFont.Dispose();
            _rateFont.Dispose();
            _productBrush.Dispose();
            _rateBrush.Dispose();
        }
        base.Dispose(disposing);
    }
}
