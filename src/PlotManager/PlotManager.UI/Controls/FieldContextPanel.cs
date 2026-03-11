using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

using PlotManager.Core.Models;
using PlotManager.Core.Services;

namespace PlotManager.UI.Controls;

/// <summary>
/// Left-side field context panel for the Pass Monitor.
/// Provides the operator with critical situational awareness:
///   - Current position (plot ID, boom state, distance to boundary)
///   - Current product (large bold display)
///   - Next product preview (look-ahead from trial map)
///   - Pass progress (number, direction, speed deviation)
///   - Trial/Logger status (session active, record count, elapsed)
/// </summary>
public class FieldContextPanel : UserControl
{
    // ── State ──
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

    // ── Colors ──
    private static readonly Color BgColor = Color.FromArgb(30, 30, 35);
    private static readonly Color CardBg = Color.FromArgb(40, 40, 48);
    private static readonly Color DimText = Color.FromArgb(130, 130, 140);
    private static readonly Color BrightText = Color.FromArgb(230, 230, 230);
    private static readonly Color AccentCyan = Color.FromArgb(0, 188, 212);
    private static readonly Color AccentGreen = Color.FromArgb(76, 175, 80);
    private static readonly Color AccentOrange = Color.FromArgb(255, 152, 0);
    private static readonly Color AccentRed = Color.FromArgb(244, 67, 54);
    private static readonly Color AccentPurple = Color.FromArgb(156, 39, 176);

    // ── Cached GDI objects — GDI-FIX: avoid allocating Font/Brush every OnPaint (10 Hz) ──
    // DrawCard
    private readonly SolidBrush _cardBrush      = new(Color.FromArgb(40, 40, 48));
    private readonly Font        _titleFont       = new("Segoe UI Semibold", 8f);
    private readonly SolidBrush _titleBrush      = new(Color.FromArgb(130, 130, 140));
    // Position card
    private readonly Font        _posBigFont      = new("Segoe UI", 16f, FontStyle.Bold);
    private readonly Font        _posSmallFont    = new("Segoe UI", 8.5f);
    private readonly SolidBrush _posBrightBrush  = new(Color.FromArgb(230, 230, 230));
    private readonly SolidBrush _posDimBrush     = new(Color.FromArgb(130, 130, 140));
    private readonly SolidBrush _posAccentBrush  = new(Color.FromArgb(0, 188, 212));
    // Product card
    private readonly Font        _prodBigFont     = new("Segoe UI", 18f, FontStyle.Bold);
    private readonly SolidBrush _prodDimBrush    = new(Color.FromArgb(130, 130, 140));
    private readonly Font        _nextFont        = new("Segoe UI Semibold", 11f);
    private readonly SolidBrush _nextBrush       = new(Color.FromArgb(156, 39, 176));
    // Pass card
    private readonly Font        _passFont        = new("Segoe UI Semibold", 11f);
    private readonly Font        _passSmallFont   = new("Segoe UI", 8.5f);
    private readonly SolidBrush _passBrightBrush = new(Color.FromArgb(230, 230, 230));
    private readonly SolidBrush _passDimBrush    = new(Color.FromArgb(130, 130, 140));
    // Trial card
    private readonly Font        _trialFont       = new("Segoe UI Semibold", 10f);
    private readonly Font        _trialSmallFont  = new("Segoe UI", 8.5f);
    private readonly SolidBrush _trialDimBrush   = new(Color.FromArgb(130, 130, 140));
    private readonly SolidBrush _trialActiveBrush= new(Color.FromArgb(76, 175, 80));
    private readonly Pen         _sepPen          = new(Color.FromArgb(55, 55, 60), 1);
    private readonly SolidBrush _logBrush        = new(Color.FromArgb(130, 130, 140));
    // GPS card
    private readonly Font        _coordFont       = new("Consolas", 9f);
    private readonly Font        _gpsSmallFont    = new("Segoe UI", 8.5f);
    private readonly SolidBrush _gpsBrightBrush  = new(Color.FromArgb(230, 230, 230));
    private readonly SolidBrush _gpsDimBrush     = new(Color.FromArgb(130, 130, 140));
    private readonly SolidBrush _gpsAccentBrush  = new(Color.FromArgb(0, 188, 212));

    public FieldContextPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = BgColor;
        Width = 260;
    }

    // ════════════════════════════════════════════════════════════════════
    // Public Update Methods
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Update with latest spatial result from PlotModeController.</summary>
    public void UpdateSpatial(SpatialResult result, double speedKmh, double targetSpeedKmh)
    {
        _lastResult = result;
        _speedKmh = speedKmh;
        _targetSpeedKmh = targetSpeedKmh;
        Invalidate();
    }

    /// <summary>Update with current pass state from PassTracker.</summary>
    public void UpdatePass(PassState? pass)
    {
        _currentPass = pass;
        Invalidate();
    }

    /// <summary>Update trial/logger status.</summary>
    public void UpdateTrialStatus(bool isActive, string? trialName, long recordCount)
    {
        _trialActive = isActive;
        _trialName = trialName;
        _trialRecordCount = recordCount;
        Invalidate();
    }

    /// <summary>Update diagnostic logger status.</summary>
    public void UpdateLoggerStatus(string status, long entryCount)
    {
        _loggerStatus = status;
        _logEntryCount = entryCount;
        Invalidate();
    }

    /// <summary>Update GPS coordinates and heading.</summary>
    public void UpdateGps(double lat, double lon, double heading)
    {
        _latitude = lat;
        _longitude = lon;
        _headingDeg = heading;
        Invalidate();
    }

    /// <summary>Set the next product preview (from trial map look-ahead).</summary>
    public void SetNextProduct(string? product)
    {
        _nextProduct = product;
        Invalidate();
    }

    // ════════════════════════════════════════════════════════════════════
    // Rendering
    // ════════════════════════════════════════════════════════════════════

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        float y = 8;
        float cardW = Width - 16;
        float cardX = 8;

        // ═══ Card 0: GPS / HEADING ═══
        y = DrawCard(g, cardX, y, cardW, "🧭 GPS & HEADING", DrawGpsContent);

        // ═══ Card 1: FIELD POSITION ═══
        y = DrawCard(g, cardX, y + 6, cardW, "📍 POSITION", DrawPositionContent);

        // ═══ Card 2: CURRENT PRODUCT ═══
        y = DrawCard(g, cardX, y + 6, cardW, "🧪 PRODUCT", DrawProductContent);

        // ═══ Card 3: PASS PROGRESS ═══
        y = DrawCard(g, cardX, y + 6, cardW, "🚜 PASS", DrawPassContent);

        // ═══ Card 4: TRIAL / LOGGER ═══
        DrawCard(g, cardX, y + 6, cardW, "📝 TRIAL & LOG", DrawTrialContent);
    }

    private float DrawCard(Graphics g, float x, float y, float w, string title, Func<Graphics, float, float, float, float> drawContent)
    {
        // GDI-FIX: use cached cardBrush/titleFont/titleBrush (were `using var` per-call before)
        float contentY = y + 24;
        float contentH = drawContent(g, x + 10, contentY, w - 20);
        float totalH = 24 + contentH + 8;

        // Draw card bg
        using var path = RoundedRect(new RectangleF(x, y, w, totalH), 6);
        g.FillPath(_cardBrush, path);

        // Draw title
        g.DrawString(title, _titleFont, _titleBrush, x + 10, y + 4);

        return y + totalH;
    }

    // ── Card Content Renderers ──

    private float DrawPositionContent(Graphics g, float x, float y, float w)
    {
        // GDI-FIX: use cached fonts/brushes
        Font bigFont    = _posBigFont;
        Font smallFont  = _posSmallFont;
        SolidBrush brightBrush = _posBrightBrush;
        SolidBrush dimBrush    = _posDimBrush;
        SolidBrush accentBrush = _posAccentBrush;

        float h = 0;

        if (_lastResult == null)
        {
            g.DrawString("Очікування GPS…", smallFont, dimBrush, x, y);
            return 20;
        }

        // Plot ID or state
        string posText;
        Color stateColor;
        switch (_lastResult.State)
        {
            case BoomState.InPlot:
                string plotId = _lastResult.ActivePlot != null
                    ? $"R{_lastResult.ActivePlot.Row + 1}C{_lastResult.ActivePlot.Column + 1}"
                    : "—";
                posText = plotId;
                // DEAD-CODE-FIX: was `AccentGreen.ToArgb() == 0 ? AccentGreen : AccentGreen` — always true
                stateColor = AccentGreen;
                break;
            case BoomState.ApproachingPlot:
                posText = "→ Наближення";  // uk: approach
                stateColor = AccentOrange;
                break;
            case BoomState.LeavingPlot:
                posText = "← Вихід";     // uk: exit
                stateColor = AccentOrange;
                break;
            case BoomState.InAlley:
                posText = "Алея";         // uk: alley
                stateColor = DimText;
                break;
            default:
                posText = "Поза поля";   // uk: outside field
                stateColor = DimText;
                break;
        }

        using var stateBrush = new SolidBrush(stateColor);
        g.DrawString(posText, bigFont, stateBrush, x, y);
        h += 28;

        // BoomState label
        string stateLabel = _lastResult.State.ToString();
        g.DrawString(stateLabel, smallFont, dimBrush, x, y + h);
        h += 16;

        // Distance to boundary
        if (_lastResult.DistanceToBoundaryMeters > 0 && _lastResult.State != BoomState.OutsideGrid)
        {
            string dist = $"▸ До межі: {_lastResult.DistanceToBoundaryMeters:F1}м";  // uk: to boundary
            g.DrawString(dist, smallFont, accentBrush, x, y + h);
            h += 16;
        }

        // Speed
        string speedText = $"🏎 {_speedKmh:F1} км/год";  // uk: km/h
        Color speedColor = BrightText;
        if (_targetSpeedKmh > 0)
        {
            double deviation = Math.Abs(_speedKmh - _targetSpeedKmh) / _targetSpeedKmh * 100;
            if (deviation > 20) speedColor = AccentRed;
            else if (deviation > 10) speedColor = AccentOrange;
            speedText += $" (ціль: {_targetSpeedKmh:F1})";  // uk: target
        }
        using var speedBrush = new SolidBrush(speedColor);
        g.DrawString(speedText, smallFont, speedBrush, x, y + h);
        h += 16;

        return h;
    }

    private float DrawProductContent(Graphics g, float x, float y, float w)
    {
        // GDI-FIX: use cached fonts/brushes
        Font bigFont   = _prodBigFont;
        Font smallFont = _posSmallFont;
        SolidBrush dimBrush = _prodDimBrush;

        float h = 0;

        // Current product
        string product = _lastResult?.ActiveProduct ?? "—";
        Color productColor = product == "—" ? DimText : AccentCyan;
        using var productBrush = new SolidBrush(productColor);
        g.DrawString(product, bigFont, productBrush, x, y);
        h += 30;

        // "Current" label
        if (_lastResult?.ActiveProduct != null)
        {
            g.DrawString("Поточний продукт", smallFont, dimBrush, x, y + h);  // uk: current product
            h += 16;
        }

        // Next product preview
        if (!string.IsNullOrEmpty(_nextProduct))
        {
            g.DrawString("Наступний:", smallFont, dimBrush, x, y + h);  // uk: next
            h += 14;
            g.DrawString(_nextProduct, _nextFont, _nextBrush, x, y + h);
            h += 20;
        }

        return Math.Max(h, 20);
    }

    private float DrawPassContent(Graphics g, float x, float y, float w)
    {
        // GDI-FIX: use cached fonts/brushes
        Font medFont   = _passFont;
        Font smallFont = _passSmallFont;
        SolidBrush brightBrush = _passBrightBrush;
        SolidBrush dimBrush    = _passDimBrush;

        float h = 0;

        if (_currentPass == null || !_currentPass.IsActive)
        {
            g.DrawString("Між проходами", smallFont, dimBrush, x, y);  // uk: between passes
            return 20;
        }

        // Pass number + direction
        string arrow = _currentPass.Direction == PassDirection.Up ? "↑" : "↓";
        string passText = $"Прохід {_currentPass.PassNumber} {arrow}";  // uk: pass
        g.DrawString(passText, medFont, brightBrush, x, y);
        h += 22;

        // Column
        g.DrawString($"Колонка: {_currentPass.ColumnIndex + 1}", smallFont, dimBrush, x, y + h);  // uk: column
        h += 16;

        // Speed deviation
        if (_currentPass.MaxSpeedDeviationPercent > 5)
        {
            Color devColor = _currentPass.MaxSpeedDeviationPercent > 15 ? AccentRed : AccentOrange;
            using var devBrush = new SolidBrush(devColor);
            string devText = $"⚠ Відхилення: {_currentPass.MaxSpeedDeviationPercent:F0}%";  // uk: deviation
            g.DrawString(devText, smallFont, devBrush, x, y + h);
            h += 16;
        }

        // Sample count
        g.DrawString($"Замірів: {_currentPass.SpeedSampleCount}", smallFont, dimBrush, x, y + h);  // uk: samples
        h += 16;

        return h;
    }

    private float DrawTrialContent(Graphics g, float x, float y, float w)
    {
        // GDI-FIX: use cached fonts/brushes
        Font medFont   = _trialFont;
        Font smallFont = _trialSmallFont;
        SolidBrush dimBrush = _trialDimBrush;

        float h = 0;

        // Trial session
        if (_trialActive)
        {
            g.DrawString($"● {_trialName ?? "Trial"}", medFont, _trialActiveBrush, x, y);
            h += 20;

            g.DrawString($"Записів: {_trialRecordCount}", smallFont, dimBrush, x, y + h);  // uk: records
            h += 16;
        }
        else
        {
            g.DrawString("Trial: не активний", smallFont, dimBrush, x, y);  // uk: not active
            h += 16;
        }

        // Separator line
        h += 4;
        g.DrawLine(_sepPen, x, y + h, x + w, y + h);
        h += 6;

        // Logger status
        g.DrawString($"Logger: {_loggerStatus}", smallFont, _logBrush, x, y + h);
        h += 14;

        if (_logEntryCount > 0)
        {
            g.DrawString($"Записів логу: {_logEntryCount}", smallFont, dimBrush, x, y + h);  // uk: log entries
            h += 14;
        }

        return h;
    }

    // ════════════════════════════════════════════════════════════════════
    // GPS / Heading Card
    // ════════════════════════════════════════════════════════════════════

    private float DrawGpsContent(Graphics g, float x, float y, float w)
    {
        // GDI-FIX: use cached fonts/brushes
        Font coordFont  = _coordFont;
        Font smallFont  = _gpsSmallFont;
        SolidBrush brightBrush = _gpsBrightBrush;
        SolidBrush dimBrush    = _gpsDimBrush;
        SolidBrush accentBrush = _gpsAccentBrush;

        float h = 0;

        if (_latitude == 0 && _longitude == 0)
        {
            g.DrawString("Очікування GPS…", smallFont, dimBrush, x, y);  // uk: waiting GPS
            return 20;
        }

        // Coordinates
        g.DrawString($"Lat:  {_latitude:F7}°", coordFont, brightBrush, x, y + h);
        h += 16;
        g.DrawString($"Lon: {_longitude:F7}°", coordFont, brightBrush, x, y + h);
        h += 20;

        // Heading with compass arrow
        string compass = GetCompassDirection(_headingDeg);
        string headingText = $"Курс: {_headingDeg:F0}° {compass}";  // uk: heading
        g.DrawString(headingText, smallFont, accentBrush, x, y + h);

        // Draw small compass arrow
        float arrowCx = x + w - 24;
        float arrowCy = y + h + 2;
        DrawCompassArrow(g, arrowCx, arrowCy, 16, (float)_headingDeg);
        h += 20;

        return h;
    }

    private void DrawCompassArrow(Graphics g, float cx, float cy, float size, float headingDeg)
    {
        float rad = (headingDeg - 90) * (float)Math.PI / 180f;
        float dx = (float)Math.Cos(rad) * size / 2;
        float dy = (float)Math.Sin(rad) * size / 2;

        using var pen = new Pen(AccentCyan, 2);
        g.DrawLine(pen, cx - dx, cy - dy, cx + dx, cy + dy);

        // Arrowhead
        float headRad1 = rad + 2.6f;
        float headRad2 = rad - 2.6f;
        float hs = size / 3;
        g.DrawLine(pen, cx + dx, cy + dy,
            cx + dx - (float)Math.Cos(headRad1) * hs,
            cy + dy - (float)Math.Sin(headRad1) * hs);
        g.DrawLine(pen, cx + dx, cy + dy,
            cx + dx - (float)Math.Cos(headRad2) * hs,
            cy + dy - (float)Math.Sin(headRad2) * hs);
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

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2;
        var arc = new RectangleF(rect.X, rect.Y, diameter, diameter);

        // Top left arc
        path.AddArc(arc, 180, 90);
        // Top right arc
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        // Bottom right arc
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        // Bottom left arc
        arc.X = rect.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    // GDI-FIX: Dispose all cached GDI objects
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cardBrush.Dispose();      _titleFont.Dispose();      _titleBrush.Dispose();
            _posBigFont.Dispose();     _posSmallFont.Dispose();
            _posBrightBrush.Dispose(); _posDimBrush.Dispose();    _posAccentBrush.Dispose();
            _prodBigFont.Dispose();    _prodDimBrush.Dispose();
            _nextFont.Dispose();       _nextBrush.Dispose();
            _passFont.Dispose();       _passSmallFont.Dispose();
            _passBrightBrush.Dispose();_passDimBrush.Dispose();
            _trialFont.Dispose();      _trialSmallFont.Dispose();
            _trialDimBrush.Dispose();  _trialActiveBrush.Dispose();
            _sepPen.Dispose();         _logBrush.Dispose();
            _coordFont.Dispose();      _gpsSmallFont.Dispose();
            _gpsBrightBrush.Dispose(); _gpsDimBrush.Dispose();    _gpsAccentBrush.Dispose();
        }
        base.Dispose(disposing);
    }
}
