using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PlotManager.Core.Services;

using PlotManager.Core.Models;

/// <summary>
/// Background service that receives JSON sensor telemetry from Teensy via UDP,
/// applies calibration math from MachineProfile, and exposes calibrated snapshots.
///
/// Teensy JSON format: {"AirV": 1.7, "FlowHz": [25.0, 0.0, 0.0, ...]}
///
/// Calibration formulas:
///   AirPressureBar = (AirV - VoltageOffset) * VoltageMultiplier
///   FlowRatesLpm[i] = (FlowHz[i] * 60) / PulsesPerLiter
/// </summary>
public class SensorHub : IDisposable
{
    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private bool _disposed;
    private readonly IPlotLogger? _logger;
    private int _errorCount;
    private ServiceHealth _health = ServiceHealth.Healthy;
    private int _handlerRunning; // T4: ThreadPool dispatch throttle

    /// <summary>Maximum number of UDP bind retries before giving up.</summary>
    public int MaxBindRetries { get; set; } = 3;

    /// <summary>Initial delay between bind retries in milliseconds.</summary>
    public int BindRetryDelayMs { get; set; } = 500;

    // ── Calibration constants (set from MachineProfile) ──

    /// <summary>Voltage at 0 Bar (sensor offset).</summary>
    public double AirPressureVoltageOffset { get; set; } = 0.5;

    /// <summary>Multiplier: Bar = (V - Offset) * Multiplier.</summary>
    public double AirPressureVoltageMultiplier { get; set; } = 2.5;

    /// <summary>Flow meter pulses per liter (all channels).</summary>
    public double FlowMeterPulsesPerLiter { get; set; } = 400.0;

    /// <summary>Timeout in seconds before telemetry is considered stale.</summary>
    public double StaleTimeoutSeconds { get; set; } = 2.0;

    /// <summary>UDP port to listen on.</summary>
    public int ListenPort { get; set; } = 9999;

    /// <summary>Number of communication/parse errors since Start().</summary>
    public int ErrorCount => _errorCount;

    /// <summary>Current health status of the SensorHub service.</summary>
    public ServiceHealth Health => _health;

    /// <summary>
    /// Creates a SensorHub with optional structured logging.
    /// </summary>
    public SensorHub(IPlotLogger? logger = null)
    {
        _logger = logger;
    }

    // ── State ──

    private readonly object _snapshotLock = new();
    private SensorSnapshot _latestSnapshot = SensorSnapshot.CreateEmpty();
    private DateTime _lastReceivedUtc = DateTime.MinValue;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The most recent calibrated sensor snapshot. Thread-safe.</summary>
    public SensorSnapshot LatestSnapshot
    {
        get
        {
            lock (_snapshotLock)
            {
                // Check staleness dynamically
                if (!_latestSnapshot.IsStale && IsStale(_latestSnapshot.TimestampUtc))
                {
                    _latestSnapshot = _latestSnapshot with { IsStale = true };
                }
                return _latestSnapshot;
            }
        }
    }

    /// <summary>Fires when a new telemetry snapshot is processed.</summary>
    public event Action<SensorSnapshot>? OnTelemetryUpdated;

    /// <summary>Fires when telemetry transitions to stale (no data for StaleTimeoutSeconds).</summary>
    public event Action? OnTelemetryLost
    {
        add { _telemetryLostHandlers += value; }
        remove { _telemetryLostHandlers -= value; }
    }
    private Action? _telemetryLostHandlers;

    /// <summary>Whether the hub is currently listening for UDP data.</summary>
    public bool IsListening => _receiveTask != null && !_receiveTask.IsCompleted;

    // ════════════════════════════════════════════════════════════════════
    // Lifecycle
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configures calibration constants from a MachineProfile.
    /// Call before Start().
    /// </summary>
    public void Configure(MachineProfile profile)
    {
        AirPressureVoltageOffset = profile.AirPressureVoltageOffset;
        AirPressureVoltageMultiplier = profile.AirPressureVoltageMultiplier;

        // R5 FIX: Reject invalid calibration that would cause divide-by-zero
        if (profile.FlowMeterPulsesPerLiter <= 0)
            throw new ArgumentException(
                $"FlowMeterPulsesPerLiter must be positive, got {profile.FlowMeterPulsesPerLiter}",
                nameof(profile));
        FlowMeterPulsesPerLiter = profile.FlowMeterPulsesPerLiter;

        ListenPort = profile.SensorUdpPort;
        _logger?.Info("SensorHub", $"Configured: port={ListenPort}, pulsesPerL={FlowMeterPulsesPerLiter}");
    }

    /// <summary>Starts listening for UDP telemetry.</summary>
    public void Start()
    {
        if (IsListening) return;

        _cts = new CancellationTokenSource();
        _errorCount = 0;
        _handlerRunning = 0;
        _health = ServiceHealth.Healthy;

        // R2 FIX: Retry UDP bind with exponential backoff
        int delay = BindRetryDelayMs;
        for (int attempt = 1; attempt <= MaxBindRetries; attempt++)
        {
            try
            {
                _listener = new UdpClient(ListenPort);
                _logger?.Info("SensorHub", $"Bound to UDP port {ListenPort} (attempt {attempt})");
                break;
            }
            catch (SocketException ex)
            {
                _logger?.Warn("SensorHub",
                    $"Bind attempt {attempt}/{MaxBindRetries} failed on port {ListenPort}: {ex.SocketErrorCode}");

                if (attempt == MaxBindRetries)
                {
                    _health = ServiceHealth.Failed;
                    _logger?.Error("SensorHub",
                        $"All {MaxBindRetries} bind attempts exhausted for port {ListenPort}", ex);
                    throw new InvalidOperationException(
                        $"Failed to bind SensorHub to UDP port {ListenPort} after {MaxBindRetries} retries.", ex);
                }

                Thread.Sleep(delay);
                delay = Math.Min(delay * 2, 4000); // Exponential backoff, cap at 4s
            }
        }

        _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
    }

    /// <summary>Stops listening and releases resources.</summary>
    public void Stop()
    {
        _cts?.Cancel();

        // Wait for the receive loop to exit before disposing the socket
        try { _receiveTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { /* expected from cancellation */ }
        _receiveTask = null;

        _listener?.Close();
        _listener?.Dispose();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    // ════════════════════════════════════════════════════════════════════
    // Calibration Math (public for testability)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Converts raw sensor voltage to air pressure in Bar.
    /// Formula: (voltage - offset) * multiplier, clamped to ≥ 0.
    /// </summary>
    public double CalibrateAirPressure(double voltage)
    {
        double bar = (voltage - AirPressureVoltageOffset) * AirPressureVoltageMultiplier;
        return Math.Max(0, bar);
    }

    /// <summary>
    /// Converts flow meter frequency (Hz) to liters per minute.
    /// Formula: (hz * 60) / pulsesPerLiter.
    /// </summary>
    public double CalibrateFlowRate(double hz)
    {
        if (FlowMeterPulsesPerLiter <= 0 || hz < 0) return 0;
        return (hz * 60.0) / FlowMeterPulsesPerLiter;
    }

    /// <summary>
    /// Processes a raw JSON telemetry string into a calibrated SensorSnapshot.
    /// Returns null if the JSON is invalid.
    /// </summary>
    public SensorSnapshot? ProcessRawJson(string json)
    {
        RawTelemetry? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawTelemetry>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (raw == null) return null;

        return ProcessRawTelemetry(raw);
    }

    /// <summary>
    /// Processes a deserialized RawTelemetry into a calibrated SensorSnapshot.
    /// </summary>
    public SensorSnapshot ProcessRawTelemetry(RawTelemetry raw)
    {
        // R3 FIX: Guard NaN/Infinity from truncated JSON
        double airV = double.IsFinite(raw.AirV) ? raw.AirV : 0;
        double airBar = CalibrateAirPressure(airV);

        double[] flowLpm = new double[SensorSnapshot.FlowMeterCount];
        if (raw.FlowHz != null)
        {
            int count = Math.Min(raw.FlowHz.Length, SensorSnapshot.FlowMeterCount);
            for (int i = 0; i < count; i++)
            {
                double hz = double.IsFinite(raw.FlowHz[i]) ? raw.FlowHz[i] : 0;
                flowLpm[i] = CalibrateFlowRate(hz);
            }
        }

        return new SensorSnapshot
        {
            AirPressureBar = airBar,
            FlowRatesLpm = flowLpm,
            TimestampUtc = DateTime.UtcNow,
            IsStale = false,
            IsEstop = raw.Estop,
        };
    }

    // ════════════════════════════════════════════════════════════════════
    // Private
    // ════════════════════════════════════════════════════════════════════

    private bool IsStale(DateTime snapshotTime)
    {
        return (DateTime.UtcNow - snapshotTime).TotalSeconds > StaleTimeoutSeconds;
    }

    /// <summary>Source IP of the last received telemetry packet (for diagnostics).</summary>
    public string? LastSourceIP { get; private set; }

    /// <summary>
    /// If set, only accept UDP packets from this IP address.
    /// Null = accept from any source (default, backward-compatible).
    /// Set to the Teensy's IP (e.g., "192.168.5.100") for production hardening.
    /// </summary>
    public string? AllowedSourceIP { get; set; }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_listener == null) break;

                UdpReceiveResult result = await _listener.ReceiveAsync(ct);
                string sourceIp = result.RemoteEndPoint.Address.ToString();
                LastSourceIP = sourceIp;

                // Sec2 FIX: Filter by source IP if configured
                if (AllowedSourceIP != null && sourceIp != AllowedSourceIP)
                    continue;

                string json = Encoding.UTF8.GetString(result.Buffer);

                SensorSnapshot? snapshot = ProcessRawJson(json);
                if (snapshot == null) continue;

                lock (_snapshotLock)
                {
                    _latestSnapshot = snapshot;
                    _lastReceivedUtc = snapshot.TimestampUtc;
                }

                // T4 FIX: Throttle ThreadPool dispatch — skip if previous handler still running.
                // Prevents ThreadPool saturation under burst conditions (>10 packets/sec).
                SensorSnapshot captured = snapshot;
                if (OnTelemetryUpdated != null && Interlocked.CompareExchange(ref _handlerRunning, 1, 0) == 0)
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            OnTelemetryUpdated?.Invoke(captured);
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _handlerRunning, 0);
                        }
                    });
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex)
            {
                Interlocked.Increment(ref _errorCount);
                _health = ServiceHealth.Degraded;
                _logger?.Warn("SensorHub", $"Socket error #{_errorCount}: {ex.SocketErrorCode}");
            }
            catch (JsonException ex)
            {
                Interlocked.Increment(ref _errorCount);
                _logger?.Warn("SensorHub", $"JSON parse error #{_errorCount}: {ex.Message}");
            }
        }
    }
}
