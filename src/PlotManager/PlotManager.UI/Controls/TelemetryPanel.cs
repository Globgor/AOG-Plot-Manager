using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

using PlotManager.Core.Models;

namespace PlotManager.UI.Controls;

/// <summary>
/// Displays air pressure gauge and 10 flow-meter bars from SensorSnapshot.
/// Air pressure shows a red zone below MinSafeAirPressureBar.
/// Flow meters show actual vs target rate.
/// </summary>
public class TelemetryPanel : UserControl
{
    private SensorSnapshot? _snapshot;
    private double _minSafePressureBar = 2.0;
    private double _maxPressureBar = 10.0;
    private double _targetFlowLpm;

    private const int Padding = 12;
    private const int GaugeHeight = 30;
    private const int BarWidth = 20;
    private const int BarMaxHeight = 120;

    private static readonly Color BackgroundColor = Color.FromArgb(35, 35, 40);
    private static readonly Color TextColor = Color.FromArgb(220, 220, 220);
    private static readonly Color DimTextColor = Color.FromArgb(120, 120, 120);
    private static readonly Color GaugeGreenColor = Color.FromArgb(76, 175, 80);
    private static readonly Color GaugeRedColor = Color.FromArgb(244, 67, 54);
    private static readonly Color GaugeYellowColor = Color.FromArgb(255, 193, 7);
    private static readonly Color BarActualColor = Color.FromArgb(33, 150, 243);
    private static readonly Color BarTargetColor = Color.FromArgb(255, 152, 0);
    private static readonly Color StaleOverlayColor = Color.FromArgb(180, 30, 30, 30);

    public TelemetryPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = BackgroundColor;
    }

    /// <summary>Minimum safe air pressure (Bar) — red zone threshold.</summary>
    public double MinSafePressureBar
    {
        get => _minSafePressureBar;
        set { _minSafePressureBar = value; Invalidate(); }
    }

    /// <summary>Maximum pressure for gauge scale.</summary>
    public double MaxPressureBar
    {
        get => _maxPressureBar;
        set { _maxPressureBar = value; Invalidate(); }
    }

    /// <summary>Target flow rate (L/min) for reference line overlay.</summary>
    public double TargetFlowLpm
    {
        get => _targetFlowLpm;
        set { _targetFlowLpm = value; Invalidate(); }
    }

    /// <summary>Updates the sensor snapshot and repaints.</summary>
    public void UpdateSnapshot(SensorSnapshot snapshot)
    {
        _snapshot = snapshot;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var titleFont = new Font("Segoe UI Semibold", 9f);
        using var valueFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", 7f);
        using var textBrush = new SolidBrush(TextColor);
        using var dimBrush = new SolidBrush(DimTextColor);

        float y = Padding;

        // === Air Pressure Gauge ===
        g.DrawString("AIR PRESSURE", titleFont, dimBrush, Padding, y);
        y += 18;

        double pressure = _snapshot?.AirPressureBar ?? 0;
        DrawPressureGauge(g, Padding, y, Width - Padding * 2, GaugeHeight, pressure);

        string pressureText = $"{pressure:F1} Bar";
        g.DrawString(pressureText, valueFont, textBrush, Width - Padding - g.MeasureString(pressureText, valueFont).Width, y + 2);
        y += GaugeHeight + 20;

        // === Flow Meters ===
        g.DrawString("FLOW RATES (L/min)", titleFont, dimBrush, Padding, y);
        y += 18;

        DrawFlowMeters(g, Padding, y, Width - Padding * 2);

        // === Stale overlay ===
        if (_snapshot == null || _snapshot.IsStale)
        {
            using var overlayBrush = new SolidBrush(StaleOverlayColor);
            g.FillRectangle(overlayBrush, ClientRectangle);

            using var staleFont = new Font("Segoe UI", 14f, FontStyle.Bold);
            using var staleBrush = new SolidBrush(GaugeRedColor);
            string msg = "NO TELEMETRY";
            SizeF sz = g.MeasureString(msg, staleFont);
            g.DrawString(msg, staleFont, staleBrush, (Width - sz.Width) / 2, (Height - sz.Height) / 2);
        }
    }

    private void DrawPressureGauge(Graphics g, float x, float y, float width, float height, double pressure)
    {
        float fillFraction = (float)Math.Clamp(pressure / _maxPressureBar, 0, 1);
        float redZoneFraction = (float)(_minSafePressureBar / _maxPressureBar);

        // Background
        using var bgBrush = new SolidBrush(Color.FromArgb(50, 50, 55));
        var bgRect = new RectangleF(x, y, width * 0.7f, height);
        g.FillRectangle(bgBrush, bgRect);

        // Red zone
        float redWidth = width * 0.7f * redZoneFraction;
        using var redBrush = new SolidBrush(Color.FromArgb(60, GaugeRedColor));
        g.FillRectangle(redBrush, x, y, redWidth, height);

        // Fill bar
        float fillWidth = width * 0.7f * fillFraction;
        Color fillColor = pressure < _minSafePressureBar ? GaugeRedColor :
                          pressure < _minSafePressureBar * 1.5 ? GaugeYellowColor :
                          GaugeGreenColor;
        using var fillBrush = new SolidBrush(fillColor);
        g.FillRectangle(fillBrush, x, y, fillWidth, height);

        // Border
        using var borderPen = new Pen(Color.FromArgb(80, 80, 80), 1);
        g.DrawRectangle(borderPen, x, y, width * 0.7f, height);

        // Threshold mark
        float threshX = x + redWidth;
        using var threshPen = new Pen(GaugeRedColor, 2) { DashStyle = DashStyle.Dash };
        g.DrawLine(threshPen, threshX, y - 2, threshX, y + height + 2);
    }

    // P3 FIX: Cached GDI object
    private readonly Pen _barOutlinePen = new(Color.FromArgb(60, 60, 65), 1);

    private void DrawFlowMeters(Graphics g, float x, float y, float areaWidth)
    {
        int count = SensorSnapshot.FlowMeterCount;
        float spacing = Math.Max(4, (areaWidth - count * BarWidth) / (count + 1));
        float maxLpm = 5.0f; // Auto-scale

        // Find max for auto-scaling
        if (_snapshot?.FlowRatesLpm != null)
        {
            foreach (double v in _snapshot.FlowRatesLpm)
            {
                if (v > maxLpm) maxLpm = (float)v;
            }
        }
        if (_targetFlowLpm > maxLpm) maxLpm = (float)_targetFlowLpm;
        maxLpm *= 1.2f; // 20% headroom

        using var labelFont = new Font("Segoe UI", 7f);
        using var dimBrush = new SolidBrush(DimTextColor);
        using var barBrush = new SolidBrush(BarActualColor);

        for (int i = 0; i < count; i++)
        {
            float barX = x + spacing + i * (BarWidth + spacing);
            double flowLpm = _snapshot?.FlowRatesLpm?[i] ?? 0;
            float barHeight = (float)(flowLpm / maxLpm) * BarMaxHeight;
            barHeight = Math.Clamp(barHeight, 0, BarMaxHeight);

            // Bar
            float barTop = y + BarMaxHeight - barHeight;
            g.FillRectangle(barBrush, barX, barTop, BarWidth, barHeight);

            // Bar outline (P3 FIX: cached pen)
            g.DrawRectangle(_barOutlinePen, barX, y, BarWidth, BarMaxHeight);

            // Channel label
            string label = (i + 1).ToString();
            SizeF sz = g.MeasureString(label, labelFont);
            g.DrawString(label, labelFont, dimBrush,
                barX + (BarWidth - sz.Width) / 2, y + BarMaxHeight + 3);
        }

        // Target rate reference line
        if (_targetFlowLpm > 0)
        {
            float targetY = y + BarMaxHeight - (float)(_targetFlowLpm / maxLpm) * BarMaxHeight;
            using var targetPen = new Pen(BarTargetColor, 2) { DashStyle = DashStyle.Dash };
            g.DrawLine(targetPen, x, targetY, x + areaWidth, targetY);

            using var targetFont = new Font("Segoe UI", 7f, FontStyle.Bold);
            using var targetBrush = new SolidBrush(BarTargetColor);
            g.DrawString($"Target: {_targetFlowLpm:F1}", targetFont, targetBrush, x + areaWidth - 80, targetY - 14);
        }
    }

    // P3 FIX: Dispose cached GDI objects
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _barOutlinePen.Dispose();
        }
        base.Dispose(disposing);
    }
}
