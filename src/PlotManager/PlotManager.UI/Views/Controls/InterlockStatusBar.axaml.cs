#pragma warning disable CS8618
using PlotManager.Core.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using PlotManager.Core.Services;
using SkiaSharp;
using System;

namespace PlotManager.UI.Views.Controls;

public partial class InterlockStatusBar : UserControl
{
    private bool _estopActive;
    private double _currentSpeed;
    private double _minSpeed;
    private double _maxSpeed;
    private bool _speedInterlock;
    private bool _rtkLost;
    private bool _rtkDegraded;
    private GpsFixQuality _rtkQuality;
    private bool _airPressureLost;
    private bool _airPressureDegraded;
    private double _airPressureBar;

    private bool _teensyStale;
    private bool _teensyEstop;
    private bool _aogStale;
    
    private ServiceHealth _sensorHealth = ServiceHealth.Healthy;
    private ServiceHealth _aogHealth = ServiceHealth.Healthy;

    private int _blinkCounter;
    private readonly DispatcherTimer _blinkTimer;

    public InterlockStatusBar()
    {
        InitializeComponent();

        _blinkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkCounter++;
            InvalidateVisual();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _blinkTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _blinkTimer.Stop();
    }

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
        
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);
    }

    public void UpdateTeensyStatus(bool stale, bool estop)
    {
        _teensyStale = stale;
        _teensyEstop = estop;
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);
    }

    public void UpdateAogStatus(bool stale)
    {
        _aogStale = stale;
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);
    }

    public void UpdateServiceHealth(ServiceHealth sensorHealth, ServiceHealth aogHealth)
    {
        _sensorHealth = sensorHealth;
        _aogHealth = aogHealth;
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var snapshot = new StatusSnapshot(this);
        context.Custom(new InterlockDrawOp(Bounds, snapshot));
    }

    private class StatusSnapshot
    {
        public bool EstopActive { get; }
        public double CurrentSpeed { get; }
        public double MinSpeed { get; }
        public double MaxSpeed { get; }
        public bool SpeedInterlock { get; }
        public bool RtkLost { get; }
        public bool RtkDegraded { get; }
        public GpsFixQuality RtkQuality { get; }
        public bool AirPressureLost { get; }
        public bool AirPressureDegraded { get; }
        public double AirPressureBar { get; }
        public bool TeensyStale { get; }
        public bool TeensyEstop { get; }
        public bool AogStale { get; }
        public ServiceHealth SensorHealth { get; }
        public ServiceHealth AogHealth { get; }
        public int BlinkCounter { get; }

        public StatusSnapshot(InterlockStatusBar bar)
        {
            EstopActive = bar._estopActive; CurrentSpeed = bar._currentSpeed;
            MinSpeed = bar._minSpeed; MaxSpeed = bar._maxSpeed; SpeedInterlock = bar._speedInterlock;
            RtkLost = bar._rtkLost; RtkDegraded = bar._rtkDegraded; RtkQuality = bar._rtkQuality;
            AirPressureLost = bar._airPressureLost; AirPressureDegraded = bar._airPressureDegraded;
            AirPressureBar = bar._airPressureBar; TeensyStale = bar._teensyStale;
            TeensyEstop = bar._teensyEstop; AogStale = bar._aogStale;
            SensorHealth = bar._sensorHealth; AogHealth = bar._aogHealth;
            BlinkCounter = bar._blinkCounter;
        }
    }

    private class InterlockDrawOp : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly StatusSnapshot _s;

        private static readonly SKColor GreenColor = new SKColor(76, 175, 80);
        private static readonly SKColor RedColor = new SKColor(244, 67, 54);
        private static readonly SKColor YellowColor = new SKColor(255, 193, 7);
        private static readonly SKColor DimColor = new SKColor(80, 80, 80);

        public InterlockDrawOp(Rect bounds, StatusSnapshot snapshot)
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

            using var labelPaint = new SKPaint { Color = new SKColor(140, 140, 140), TextSize = 12, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), IsAntialias = true };
            using var valuePaint = new SKPaint { TextSize = 12, Typeface = SKTypeface.FromFamilyName("Segoe UI"), IsAntialias = true };
            using var dotPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
            using var separatorPaint = new SKPaint { Color = new SKColor(60, 60, 65), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };

            float x = 12f;
            float cy = (float)_bounds.Height / 2f;

            // 1. E-STOP indicator
            SKColor estopColor = _s.EstopActive ? ((_s.BlinkCounter % 2 == 0) ? RedColor : DimColor) : GreenColor;
            x = DrawIndicator(canvas, x, cy, "E-STOP", estopColor, _s.EstopActive ? "ACTIVE" : "OK", _s.EstopActive ? RedColor : GreenColor, labelPaint, valuePaint, dotPaint);
            x = DrawSeparator(canvas, x, cy, separatorPaint);

            // 2. Speed corridor
            SKColor speedColor = _s.SpeedInterlock ? RedColor : GreenColor;
            string speedText = $"{_s.CurrentSpeed:F1} km/h [{_s.MinSpeed:F1}–{_s.MaxSpeed:F1}]";
            x = DrawIndicator(canvas, x, cy, "SPEED", speedColor, speedText, speedColor, labelPaint, valuePaint, dotPaint);
            x = DrawSeparator(canvas, x, cy, separatorPaint);

            // 3. RTK quality
            SKColor rtkColor = _s.RtkLost ? RedColor : _s.RtkDegraded ? YellowColor : GreenColor;
            string rtkText = _s.RtkLost ? "LOST" : _s.RtkDegraded ? "Degraded" : _s.RtkQuality.ToString();
            x = DrawIndicator(canvas, x, cy, "RTK", rtkColor, rtkText, rtkColor, labelPaint, valuePaint, dotPaint);
            x = DrawSeparator(canvas, x, cy, separatorPaint);

            // 4. Air Pressure
            SKColor airColor = _s.AirPressureLost ? RedColor : _s.AirPressureDegraded ? YellowColor : GreenColor;
            string airText = _s.AirPressureLost ? "LOST" : $"{_s.AirPressureBar:F1} Bar";
            x = DrawIndicator(canvas, x, cy, "AIR", airColor, airText, airColor, labelPaint, valuePaint, dotPaint);
            x = DrawSeparator(canvas, x, cy, separatorPaint);

            // 5. Teensy UDP
            SKColor teensyColor = _s.TeensyStale ? RedColor : _s.TeensyEstop ? YellowColor : GreenColor;
            string teensyText = _s.TeensyStale ? "LOST" : _s.TeensyEstop ? "E-STOP" : "OK";
            x = DrawIndicator(canvas, x, cy, "TEENSY", teensyColor, teensyText, teensyColor, labelPaint, valuePaint, dotPaint);
            x = DrawSeparator(canvas, x, cy, separatorPaint);

            // 6. AOG GPS
            SKColor aogColor = _s.AogStale ? RedColor : GreenColor;
            string aogText = _s.AogStale ? "LOST" : "OK";
            x = DrawIndicator(canvas, x, cy, "AOG", aogColor, aogText, aogColor, labelPaint, valuePaint, dotPaint);
            x = DrawSeparator(canvas, x, cy, separatorPaint);

            // 7. SensorHub Health
            SKColor shColor = _s.SensorHealth switch { ServiceHealth.Healthy => GreenColor, ServiceHealth.Degraded => YellowColor, _ => RedColor };
            x = DrawIndicator(canvas, x, cy, "SEN", shColor, _s.SensorHealth.ToString(), shColor, labelPaint, valuePaint, dotPaint);
            x = DrawSeparator(canvas, x, cy, separatorPaint);

            // 8. AogUdp Health
            SKColor ahColor = _s.AogHealth switch { ServiceHealth.Healthy => GreenColor, ServiceHealth.Degraded => YellowColor, _ => RedColor };
            DrawIndicator(canvas, x, cy, "UDP", ahColor, _s.AogHealth.ToString(), ahColor, labelPaint, valuePaint, dotPaint);

            canvas.Restore();
        }

        private float DrawIndicator(SKCanvas canvas, float x, float cy, string label, SKColor dotColor, string value, SKColor valueColor, SKPaint labelPaint, SKPaint valuePaint, SKPaint dotPaint)
        {
            dotPaint.Color = dotColor;
            canvas.DrawCircle(x + 6, cy, 6, dotPaint);
            x += 16f;

            labelPaint.GetFontMetrics(out SKFontMetrics lm);
            float labelY = cy - (lm.Ascent + lm.Descent) / 2;
            canvas.DrawText(label, x, labelY, labelPaint);
            float lw = labelPaint.MeasureText(label);
            x += lw + 6f;

            valuePaint.Color = valueColor;
            valuePaint.GetFontMetrics(out SKFontMetrics vm);
            float valueY = cy - (vm.Ascent + vm.Descent) / 2;
            canvas.DrawText(value, x, valueY, valuePaint);
            float vw = valuePaint.MeasureText(value);

            return x + vw + 16f;
        }

        private float DrawSeparator(SKCanvas canvas, float x, float cy, SKPaint separatorPaint)
        {
            canvas.DrawLine(x - 8, cy - 10, x - 8, cy + 10, separatorPaint);
            return x;
        }
    }
}
