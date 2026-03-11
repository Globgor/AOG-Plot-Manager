using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

using PlotManager.Core.Models;
using PlotManager.Core.Services;

namespace PlotManager.UI.Controls;

/// <summary>
/// Bottom status bar showing critical interlock states:
/// E-STOP | Speed corridor | RTK Fix | Air Pressure | Teensy UDP | AOG PGN.
/// </summary>
public class InterlockStatusBar : UserControl
{
    private bool _estopActive;
    private double _currentSpeed;
    private double _minSpeed;
    private double _maxSpeed;
    private bool _speedInterlock;
    private bool _rtkLost;
    private bool _rtkDegraded;
    private GpsFixQuality _rtkQuality = GpsFixQuality.RtkFix;
    private bool _airPressureLost;
    private bool _airPressureDegraded;
    private double _airPressureBar;
    private bool _teensyStale = true;
    private bool _aogStale = true;
    private bool _teensyEstop;
    // U2 FIX: ServiceHealth indicators
    private ServiceHealth _sensorHealth = ServiceHealth.Healthy;
    private ServiceHealth _aogHealth = ServiceHealth.Healthy;

    private int _blinkCounter;

    private const int IndicatorSpacing = 16;
    private static readonly Color BackgroundColor = Color.FromArgb(25, 25, 30);
    private static readonly Color TextColor = Color.FromArgb(200, 200, 200);
    private static readonly Color GreenColor = Color.FromArgb(76, 175, 80);
    private static readonly Color RedColor = Color.FromArgb(244, 67, 54);
    private static readonly Color YellowColor = Color.FromArgb(255, 193, 7);
    private static readonly Color DimColor = Color.FromArgb(80, 80, 80);

    private readonly System.Windows.Forms.Timer _blinkTimer;

    // P4 FIX: Cached GDI objects
    private readonly SolidBrush _greenBrush = new(GreenColor);
    private readonly SolidBrush _redBrush = new(RedColor);
    private readonly SolidBrush _yellowBrush = new(YellowColor);
    private readonly SolidBrush _dimBrush = new(DimColor);
    private readonly SolidBrush _labelBrush = new(Color.FromArgb(140, 140, 140));
    private readonly Pen _separatorPen = new(Color.FromArgb(60, 60, 65), 1);

    public InterlockStatusBar()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = BackgroundColor;
        Height = 40;

        _blinkTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _blinkTimer.Tick += (_, _) => { _blinkCounter++; Invalidate(); };
        _blinkTimer.Start();
    }

    /// <summary>Updates all interlock states from SectionController.</summary>
    public void UpdateFromController(SectionController ctrl)
    {
        _estopActive = ctrl.EmergencyStopActive;
        _currentSpeed = ctrl.LastSpeedKmh;
        _minSpeed = ctrl.MinSpeedKmh;
        _maxSpeed = ctrl.MaxSpeedKmh;
        _speedInterlock = ctrl.SpeedInterlockActive;
        _rtkLost = ctrl.RtkLostActive;
        _rtkDegraded = ctrl.RtkDegraded;
        _rtkQuality = ctrl.LastFixQuality;
        _airPressureLost = ctrl.AirPressureLostActive;
        _airPressureDegraded = ctrl.AirPressureDegraded;
        _airPressureBar = ctrl.LastAirPressureBar;
        Invalidate();
    }

    /// <summary>Updates Teensy communication status.</summary>
    public void UpdateTeensyStatus(bool stale, bool estop)
    {
        _teensyStale = stale;
        _teensyEstop = estop;
        Invalidate();
    }

    /// <summary>Updates AOG GPS communication status.</summary>
    public void UpdateAogStatus(bool stale)
    {
        _aogStale = stale;
        Invalidate();
    }

    /// <summary>U2 FIX: Updates service health indicators for SensorHub and AogUdp.</summary>
    public void UpdateServiceHealth(ServiceHealth sensorHealth, ServiceHealth aogHealth)
    {
        _sensorHealth = sensorHealth;
        _aogHealth = aogHealth;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var font = new Font("Segoe UI", 9f);
        using var boldFont = new Font("Segoe UI Semibold", 9f);

        float x = 12;
        float y = (Height - 20) / 2f;

        // 1. E-STOP indicator
        x = DrawIndicator(g, x, y, "E-STOP",
            _estopActive ? ((_blinkCounter % 2 == 0) ? RedColor : DimColor) : GreenColor,
            _estopActive ? "ACTIVE" : "OK",
            _estopActive ? RedColor : GreenColor,
            boldFont, font);

        x = DrawSeparator(g, x, y);

        // 2. Speed corridor
        Color speedColor = _speedInterlock ? RedColor : GreenColor;
        string speedText = $"{_currentSpeed:F1} km/h [{_minSpeed:F1}–{_maxSpeed:F1}]";
        x = DrawIndicator(g, x, y, "SPEED", speedColor, speedText, speedColor, boldFont, font);

        x = DrawSeparator(g, x, y);

        // 3. RTK quality
        Color rtkColor = _rtkLost ? RedColor : _rtkDegraded ? YellowColor : GreenColor;
        string rtkText = _rtkLost ? "LOST" : _rtkDegraded ? "Degraded" : _rtkQuality.ToString();
        x = DrawIndicator(g, x, y, "RTK", rtkColor, rtkText, rtkColor, boldFont, font);

        x = DrawSeparator(g, x, y);

        // 4. Air Pressure
        Color airColor = _airPressureLost ? RedColor : _airPressureDegraded ? YellowColor : GreenColor;
        string airText = $"{_airPressureBar:F1} Bar";
        if (_airPressureLost) airText = "LOST";
        x = DrawIndicator(g, x, y, "AIR", airColor, airText, airColor, boldFont, font);

        x = DrawSeparator(g, x, y);

        // 5. Teensy UDP
        Color teensyColor = _teensyStale ? RedColor : _teensyEstop ? YellowColor : GreenColor;
        string teensyText = _teensyStale ? "LOST" : _teensyEstop ? "E-STOP" : "OK";
        x = DrawIndicator(g, x, y, "TEENSY", teensyColor, teensyText, teensyColor, boldFont, font);

        x = DrawSeparator(g, x, y);

        // 6. AOG GPS
        Color aogColor = _aogStale ? RedColor : GreenColor;
        string aogText = _aogStale ? "LOST" : "OK";
        x = DrawIndicator(g, x, y, "AOG", aogColor, aogText, aogColor, boldFont, font);

        // U2 FIX: 7. SensorHub Health
        x = DrawSeparator(g, x, y);
        Color shColor = _sensorHealth switch
        {
            ServiceHealth.Healthy => GreenColor,
            ServiceHealth.Degraded => YellowColor,
            _ => RedColor
        };
        x = DrawIndicator(g, x, y, "SEN", shColor, _sensorHealth.ToString(), shColor, boldFont, font);

        // U2 FIX: 8. AogUdp Health
        x = DrawSeparator(g, x, y);
        Color ahColor = _aogHealth switch
        {
            ServiceHealth.Healthy => GreenColor,
            ServiceHealth.Degraded => YellowColor,
            _ => RedColor
        };
        DrawIndicator(g, x, y, "UDP", ahColor, _aogHealth.ToString(), ahColor, boldFont, font);
    }

    private SolidBrush GetCachedBrush(Color color)
    {
        if (color == GreenColor) return _greenBrush;
        if (color == RedColor) return _redBrush;
        if (color == YellowColor) return _yellowBrush;
        if (color == DimColor) return _dimBrush;
        return _greenBrush; // Fallback
    }

    private float DrawIndicator(Graphics g, float x, float y,
        string label, Color dotColor, string value, Color valueColor,
        Font boldFont, Font font)
    {
        // Status dot (P4 FIX: cached brush)
        g.FillEllipse(GetCachedBrush(dotColor), x, y + 4, 12, 12);

        x += 16;

        // Label (P4 FIX: cached brush)
        g.DrawString(label, boldFont, _labelBrush, x, y);
        x += g.MeasureString(label, boldFont).Width + 4;

        // Value (P4 FIX: cached brush)
        g.DrawString(value, font, GetCachedBrush(valueColor), x, y);
        x += g.MeasureString(value, font).Width;

        return x + IndicatorSpacing;
    }

    private float DrawSeparator(Graphics g, float x, float y)
    {
        // P4 FIX: cached pen
        g.DrawLine(_separatorPen, x - 6, y, x - 6, y + 20);
        return x;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _blinkTimer.Stop();
            _blinkTimer.Dispose();
            // P4 FIX: dispose cached GDI objects
            _greenBrush.Dispose();
            _redBrush.Dispose();
            _yellowBrush.Dispose();
            _dimBrush.Dispose();
            _labelBrush.Dispose();
            _separatorPen.Dispose();
        }
        base.Dispose(disposing);
    }
}
