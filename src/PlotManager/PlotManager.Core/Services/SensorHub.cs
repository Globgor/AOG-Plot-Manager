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
        FlowMeterPulsesPerLiter = profile.FlowMeterPulsesPerLiter;
        ListenPort = profile.SensorUdpPort;
    }

    /// <summary>Starts listening for UDP telemetry.</summary>
    public void Start()
    {
        if (IsListening) return;

        _cts = new CancellationTokenSource();
        _listener = new UdpClient(ListenPort);

        _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
    }

    /// <summary>Stops listening and releases resources.</summary>
    public void Stop()
    {
        _cts?.Cancel();
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
        double airBar = CalibrateAirPressure(raw.AirV);

        double[] flowLpm = new double[SensorSnapshot.FlowMeterCount];
        if (raw.FlowHz != null)
        {
            int count = Math.Min(raw.FlowHz.Length, SensorSnapshot.FlowMeterCount);
            for (int i = 0; i < count; i++)
            {
                flowLpm[i] = CalibrateFlowRate(raw.FlowHz[i]);
            }
        }

        return new SensorSnapshot
        {
            AirPressureBar = airBar,
            FlowRatesLpm = flowLpm,
            TimestampUtc = DateTime.UtcNow,
            IsStale = false,
        };
    }

    // ════════════════════════════════════════════════════════════════════
    // Private
    // ════════════════════════════════════════════════════════════════════

    private bool IsStale(DateTime snapshotTime)
    {
        return (DateTime.UtcNow - snapshotTime).TotalSeconds > StaleTimeoutSeconds;
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_listener == null) break;

                UdpReceiveResult result = await _listener.ReceiveAsync(ct);
                string json = Encoding.UTF8.GetString(result.Buffer);

                SensorSnapshot? snapshot = ProcessRawJson(json);
                if (snapshot == null) continue;

                lock (_snapshotLock)
                {
                    _latestSnapshot = snapshot;
                    _lastReceivedUtc = snapshot.TimestampUtc;
                }


                OnTelemetryUpdated?.Invoke(snapshot);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { /* Network error — retry */ }
            catch (JsonException) { /* Bad packet — skip */ }
        }
    }
}
