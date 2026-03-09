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
    private ITransport? _teensyTransport;

    // ── State ──
    private bool _plotModeEnabled;
    private SpatialResult _lastResult;
    private ushort _lastSentMask;
    private AogGpsData? _lastGps;
    private ushort _lastAogMask;

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
        AogUdpClient aogClient)
    {
        _spatialEngine = spatialEngine ?? throw new ArgumentNullException(nameof(spatialEngine));
        _sectionController = sectionController ?? throw new ArgumentNullException(nameof(sectionController));
        _aogClient = aogClient ?? throw new ArgumentNullException(nameof(aogClient));

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
        SpatialResult result = _spatialEngine.EvaluatePosition(boomCenter, headingDegrees, speedKmh);
        ushort finalMask = _sectionController.ApplyInterlocks(result.ValveMask, speedKmh);

        _lastResult = result with { ValveMask = finalMask };
        return _lastResult;
    }

    public void Dispose()
    {
        Stop();
        _aogClient.OnGpsUpdate -= HandleGpsUpdate;
        _aogClient.OnSectionControl -= HandleSectionControl;
        GC.SuppressFinalize(this);
    }

    // ════════════════════════════════════════════════════════════════════
    // AOG Event Handlers
    // ════════════════════════════════════════════════════════════════════

    private void HandleGpsUpdate(AogGpsData gps)
    {
        _lastGps = gps;

        if (!_plotModeEnabled || !_spatialEngine.IsConfigured)
            return;

        try
        {
            var boomCenter = new GeoPoint(gps.Latitude, gps.Longitude);
            SpatialResult result = _spatialEngine.EvaluatePosition(
                boomCenter, gps.HeadingDegrees, gps.SpeedKmh);

            // Apply safety interlocks (speed check, E-STOP)
            ushort finalMask = _sectionController.ApplyInterlocks(result.ValveMask, gps.SpeedKmh);

            _lastResult = result with { ValveMask = finalMask };

            // Only send if mask changed (avoid serial bus flooding)
            if (finalMask != _lastSentMask)
            {
                SendValveMask(finalMask);
            }

            OnSpatialUpdate?.Invoke(_lastResult);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Spatial evaluation error: {ex.Message}");
        }
    }

    private void HandleSectionControl(ushort originalMask, byte[] originalPacket)
    {
        _lastAogMask = originalMask;

        if (!_plotModeEnabled)
        {
            // Pass-through mode: forward AOG's original section control unchanged
            return;
        }

        // In Plot Mode, we override with our computed mask.
        // The override is already sent to Teensy via HandleGpsUpdate.
        // Here we can optionally inject the override back into AOG's UDP stream
        // so the AOG UI reflects the actual section states.
        try
        {
            _aogClient.SendOverriddenPacket(originalPacket, _lastSentMask);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Section override error: {ex.Message}");
        }
    }

    private void SendValveMask(ushort mask)
    {
        _lastSentMask = mask;

        // 1. Send to Teensy via USB Serial (PlotProtocol)
        if (_teensyTransport != null)
        {
            try
            {
                byte[] packet = PlotProtocol.BuildSetValves(mask);
                _teensyTransport.SendAsync(packet).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Teensy send error: {ex.Message}");
            }
        }

        OnValveMaskSent?.Invoke(mask);
    }
}
