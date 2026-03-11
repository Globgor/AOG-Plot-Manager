namespace PlotManager.Core.Services;

using PlotManager.Core.Models;
using PlotManager.Core.Protocol;

/// <summary>
/// Top-level orchestrator for "Plot Mode" — intercepts AOG GPS and section control,
/// applies spatial look-ahead logic, safety interlocks, and sends overridden
/// section commands to the Teensy via USB Serial and/or back to AOG via UDP.
///
/// When PlotModeEnabled is false, AOG's original section control passes through unmodified.
/// </summary>
public class PlotModeController : IDisposable
{
    // ── Dependencies ──
    private readonly SpatialEngine _spatialEngine;
    private readonly SectionController _sectionController;
    private readonly AogUdpClient _aogClient;
    private readonly IPlotLogger? _logger;
    private ITransport? _teensyTransport;

    /// <summary>The currently configured serial transport (for sharing with Prime/Clean controllers).</summary>
    public ITransport? Transport => _teensyTransport;
    private SensorHub? _sensorHub;
    private HardwareSetup? _hardwareSetup;
    private Func<int, (double actMs, double deactMs)>? _boomDelayProvider;

    // ── Thread safety ──
    private readonly object _stateLock = new();

    // ── State ──
    private bool _plotModeEnabled;
    private SpatialResult _lastResult;
    private ushort _lastSentMask;
    private AogGpsData? _lastGps;
    private ushort _lastAogMask;
    private bool _firstFixReceived;

    /// <summary>
    /// Whether Plot Mode is active. When false, AOG's original section control
    /// passes through unchanged to the machine module.
    /// </summary>
    public bool PlotModeEnabled
    {
        get => _plotModeEnabled;
        set
        {
            _plotModeEnabled = value;
            if (!value)
            {
                // When disabling, send all-off to clear any Plot Manager overrides
                SendValveMask(0);
            }
            OnPlotModeChanged?.Invoke(value);
        }
    }

    /// <summary>Current spatial evaluation result.</summary>
    public SpatialResult LastResult => _lastResult;

    /// <summary>Last GPS data received from AOG.</summary>
    public AogGpsData? LastGps => _lastGps;

    /// <summary>Last valve mask sent to Teensy.</summary>
    public ushort LastSentMask => _lastSentMask;

    /// <summary>Last section mask received from AOG (before override).</summary>
    public ushort LastAogMask => _lastAogMask;

    // ── Events (for UI binding) ──

    /// <summary>Fires on every GPS update cycle with the latest spatial result.</summary>
    public event Action<SpatialResult>? OnSpatialUpdate;

    /// <summary>Fires when a valve mask is sent to the hardware.</summary>
    public event Action<ushort>? OnValveMaskSent;

    /// <summary>Fires when Plot Mode is toggled.</summary>
    public event Action<bool>? OnPlotModeChanged;

    /// <summary>Fires on errors (for UI status display).</summary>
    public event Action<string>? OnError;

    public PlotModeController(
        SpatialEngine spatialEngine,
        SectionController sectionController,
        AogUdpClient aogClient,
        IPlotLogger? logger = null)
    {
        _spatialEngine = spatialEngine ?? throw new ArgumentNullException(nameof(spatialEngine));
        _sectionController = sectionController ?? throw new ArgumentNullException(nameof(sectionController));
        _aogClient = aogClient ?? throw new ArgumentNullException(nameof(aogClient));
        _logger = logger;

        _lastResult = new SpatialResult
        {
            State = BoomState.OutsideGrid,
            ValveMask = 0,
            DistanceToBoundaryMeters = double.MaxValue,
        };

        // Wire up AOG events
        _aogClient.OnGpsUpdate += HandleGpsUpdate;
        _aogClient.OnSectionControl += HandleSectionControl;
    }

    /// <summary>
    /// Sets the Teensy transport for direct valve control.
    /// </summary>
    public void SetTransport(ITransport transport)
    {
        _teensyTransport = transport;
    }

    /// <summary>
    /// Wires SensorHub telemetry to SectionController air pressure interlock.
    /// Call after construction to enable air pressure safety checks.
    /// </summary>
    public void WireInterlocks(SensorHub sensorHub)
    {
        _sensorHub = sensorHub ?? throw new ArgumentNullException(nameof(sensorHub));
        _sensorHub.OnTelemetryUpdated += HandleTelemetryForInterlocks;
    }

    /// <summary>
    /// Sets the hardware setup for per-boom evaluation (COG correction,
    /// individual overlap thresholds, per-boom hydraulic delays).
    /// When set, HandleGpsUpdate uses EvaluatePerBoom instead of EvaluatePosition.
    /// </summary>
    public void SetHardwareSetup(
        HardwareSetup setup,
        Func<int, (double actMs, double deactMs)>? boomDelayProvider = null)
    {
        // T3 FIX: Block mid-session hardware changes to prevent race with HandleGpsUpdate
        if (_plotModeEnabled)
            throw new InvalidOperationException(
                "Cannot change HardwareSetup while Plot Mode is enabled. Disable Plot Mode first.");

        _hardwareSetup = setup ?? throw new ArgumentNullException(nameof(setup));
        _boomDelayProvider = boomDelayProvider;
        _logger?.Info("PlotMode", $"HardwareSetup configured: {setup.Booms.Count} booms");
    }

    private void HandleTelemetryForInterlocks(PlotManager.Core.Models.SensorSnapshot snapshot)
    {
        _sectionController.CheckAirPressure(snapshot.AirPressureBar);
    }

    /// <summary>
    /// Starts the controller: begins listening for AOG UDP broadcasts.
    /// </summary>
    public void Start()
    {
        _aogClient.Start();
    }

    /// <summary>
    /// Stops the controller and releases AOG UDP resources.
    /// </summary>
    public void Stop()
    {
        SendValveMask(0); // Safety: all valves off on shutdown
        _aogClient.Stop();
    }

    /// <summary>
    /// Manual evaluation for testing or simulation without UDP.
    /// </summary>
    public SpatialResult Evaluate(GeoPoint boomCenter, double headingDegrees, double speedKmh)
    {
        _spatialEngine.UpdateAcceleration(speedKmh);
        SpatialResult result = _spatialEngine.EvaluatePosition(boomCenter, headingDegrees, speedKmh);
        ushort finalMask = _sectionController.ApplyInterlocks(result.ValveMask, speedKmh);

        lock (_stateLock)
        {
            _lastResult = result with { ValveMask = finalMask };
        }
        return _lastResult;
    }

    public void Dispose()
    {
        Stop();
        _aogClient.OnGpsUpdate -= HandleGpsUpdate;
        _aogClient.OnSectionControl -= HandleSectionControl;
        if (_sensorHub != null)
            _sensorHub.OnTelemetryUpdated -= HandleTelemetryForInterlocks;
        GC.SuppressFinalize(this);
    }

    // ════════════════════════════════════════════════════════════════════
    // AOG Event Handlers
    // ════════════════════════════════════════════════════════════════════

    private void HandleGpsUpdate(AogGpsData gps)
    {
        lock (_stateLock)
        {
            _lastGps = gps;
        }

        // ── P1-A2 FIX: Check RTK quality interlock on every GPS update ──
        // Skip interlock until first valid fix to avoid false alarm at startup
        if (!_firstFixReceived)
        {
            if ((int)gps.FixQuality >= (int)_sectionController.MinFixQuality)
                _firstFixReceived = true;
        }
        else
        {
            _sectionController.CheckRtkQuality(gps.FixQuality);
        }

        if (!_plotModeEnabled || !_spatialEngine.IsConfigured)
            return;

        try
        {
            // ── P2-A4 FIX: Update acceleration estimate for look-ahead ──
            _spatialEngine.UpdateAcceleration(gps.SpeedKmh);

            var boomCenter = new GeoPoint(gps.Latitude, gps.Longitude);

            // L5 FIX: Use per-boom path when hardware setup is available.
            // This activates COG crab-walk correction, per-boom overlap hysteresis,
            // and per-boom hydraulic delay compensation.
            SpatialResult result = _hardwareSetup != null
                ? _spatialEngine.EvaluatePerBoom(
                    boomCenter, gps.HeadingDegrees, gps.CourseOverGroundDegrees,
                    gps.SpeedKmh, _hardwareSetup, _boomDelayProvider)
                : _spatialEngine.EvaluatePosition(
                    boomCenter, gps.HeadingDegrees, gps.SpeedKmh);

            // Apply safety interlocks (speed, E-STOP, RTK, air pressure)
            ushort finalMask = _sectionController.ApplyInterlocks(result.ValveMask, gps.SpeedKmh);

            lock (_stateLock)
            {
                _lastResult = result with { ValveMask = finalMask };
            }

            // Only send if mask changed (avoid serial bus flooding)
            if (finalMask != _lastSentMask)
            {
                _logger?.Info("PlotMode",
                    $"Valve mask 0x{_lastSentMask:X4}→0x{finalMask:X4} | State={result.State} " +
                    $"Plot={result.ActivePlot?.Row},{result.ActivePlot?.Column} " +
                    $"Product={result.ActiveProduct ?? "—"} Dist={result.DistanceToBoundaryMeters:F2}m");
                SendValveMask(finalMask);
            }

            OnSpatialUpdate?.Invoke(_lastResult);
        }
        catch (Exception ex)
        {
            _logger?.Error("PlotMode", "Spatial evaluation error", ex);
            OnError?.Invoke($"Spatial evaluation error: {ex.Message}");
        }
    }

    private void HandleSectionControl(ushort originalMask, byte[] originalPacket)
    {
        ushort currentMask;
        lock (_stateLock)
        {
            _lastAogMask = originalMask;
            currentMask = _lastSentMask;
        }

        if (!_plotModeEnabled)
        {
            // Pass-through mode: forward AOG's original section control unchanged
            return;
        }

        // In Plot Mode, we override with our computed mask.
        // The override is already sent to Teensy via HandleGpsUpdate.
        // Here we inject the override back into AOG's UDP stream
        // so the AOG UI reflects the actual section states.
        try
        {
            _aogClient.SendOverriddenPacket(originalPacket, currentMask);
        }
        catch (Exception ex)
        {
            _logger?.Error("PlotMode", "Section override error", ex);
            OnError?.Invoke($"Section override error: {ex.Message}");
        }
    }

    private void SendValveMask(ushort mask)
    {
        lock (_stateLock)
        {
            _lastSentMask = mask;
        }

        // 1. Send to Teensy via USB Serial (PlotProtocol)
        if (_teensyTransport != null)
        {
            try
            {
                byte[] packet = PlotProtocol.BuildSetValves(mask);
                // P1-S1 FIX: Observe async exceptions instead of fire-and-forget
                _ = _teensyTransport.SendAsync(packet).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                {
                    _logger?.Error("PlotMode", "Teensy send error", t.Exception?.GetBaseException());
                    OnError?.Invoke($"Teensy send error: {t.Exception?.GetBaseException().Message}");
                }
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                _logger?.Error("PlotMode", "Teensy send error", ex);
                OnError?.Invoke($"Teensy send error: {ex.Message}");
            }
        }

        OnValveMaskSent?.Invoke(mask);
    }
}
