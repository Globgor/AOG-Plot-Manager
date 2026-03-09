using System.Timers;

namespace PlotManager.Core.Services;

using PlotManager.Core.Models;

/// <summary>
/// High-level trial logging service that wraps AsAppliedLogger with:
///   - 1Hz timed auto-logging during active spray
///   - Weather metadata header
///   - Plot entry/exit event logging
///   - Thread-safe operation
///
/// Integrates with PlotModeController for automatic data capture.
/// </summary>
public class TrialLogger : IDisposable
{
    private readonly AsAppliedLogger _csvLogger = new();
    private System.Timers.Timer? _timer;
    private bool _disposed;

    // ── Last known state (updated externally) ──
    private readonly object _stateLock = new();
    private double _lastLatitude;
    private double _lastLongitude;
    private double _lastHeading;
    private double _lastSpeedKmh;
    private string? _lastPlotId;
    private string? _lastProduct;
    private ushort _lastValveMask;
    private BoomState _lastBoomState;
    private SensorSnapshot? _lastSensorSnapshot;

    /// <summary>Whether a trial session is currently active.</summary>
    public bool IsActive => _timer != null && _timer.Enabled;

    /// <summary>Path to the current log file (null if no session).</summary>
    public string? FilePath => _csvLogger.FilePath;

    /// <summary>Number of data records written in the current session.</summary>
    public long RecordCount => _csvLogger.RecordCount;

    /// <summary>Weather snapshot captured at trial start.</summary>
    public WeatherSnapshot? Weather { get; private set; }

    /// <summary>Logging interval in milliseconds (default: 1000ms = 1Hz).</summary>
    public int LogIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Starts a new trial logging session.
    /// Writes weather metadata header before data rows begin.
    /// </summary>
    /// <param name="logDirectory">Directory for the CSV file.</param>
    /// <param name="trialName">Trial name (used in filename).</param>
    /// <param name="weather">Pre-trial weather snapshot.</param>
    public void StartSession(string logDirectory, string trialName, WeatherSnapshot weather)
    {
        if (IsActive)
            throw new InvalidOperationException("Trial session already active. Call StopSession() first.");

        Weather = weather;

        _csvLogger.StartSession(logDirectory, trialName);

        // Write weather metadata as the first record
        _csvLogger.LogMeteoCheck(
            weather.TemperatureC,
            weather.HumidityPercent,
            weather.WindSpeedMs,
            weather.WindDirection);

        // Log any additional notes
        if (!string.IsNullOrWhiteSpace(weather.Notes))
        {
            _csvLogger.LogRecord(
                DateTime.Now, 0, 0,
                "SYSTEM", "NOTE", 0, 0,
                $"Operator notes: {weather.Notes}");
        }

        // Start 1Hz timer
        _timer = new System.Timers.Timer(LogIntervalMs);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
        _timer.Start();
    }

    /// <summary>
    /// Stops the current trial session. Flushes and closes the CSV file.
    /// </summary>
    public void StopSession()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Elapsed -= OnTimerElapsed;
            _timer.Dispose();
            _timer = null;
        }

        // Write session end marker
        if (_csvLogger.FilePath != null)
        {
            _csvLogger.LogRecord(
                DateTime.Now, _lastLatitude, _lastLongitude,
                "SYSTEM", "SESSION_END", _lastSpeedKmh, 0,
                $"Records: {_csvLogger.RecordCount}");
        }

        _csvLogger.StopSession();
        Weather = null;
    }

    /// <summary>
    /// Updates the current state from PlotModeController/GPS.
    /// Called on every GPS update cycle. Thread-safe.
    /// </summary>
    public void UpdateState(
        double latitude, double longitude,
        double heading, double speedKmh,
        string? plotId, string? product,
        ushort valveMask, BoomState boomState,
        SensorSnapshot? sensorSnapshot = null)
    {
        lock (_stateLock)
        {
            _lastLatitude = latitude;
            _lastLongitude = longitude;
            _lastHeading = heading;
            _lastSpeedKmh = speedKmh;
            _lastPlotId = plotId;
            _lastProduct = product;
            _lastValveMask = valveMask;
            _lastBoomState = boomState;
            _lastSensorSnapshot = sensorSnapshot;
        }
    }

    /// <summary>
    /// Logs a plot entry event immediately (not waiting for 1Hz timer).
    /// </summary>
    public void LogPlotEntry(string plotId, string? product, double latitude, double longitude)
    {
        if (!IsActive) return;

        _csvLogger.LogRecord(
            DateTime.Now, latitude, longitude,
            plotId, product, _lastSpeedKmh, _lastValveMask,
            "PLOT_ENTRY");
    }

    /// <summary>
    /// Logs a plot exit event immediately.
    /// </summary>
    public void LogPlotExit(string plotId, string? product, double latitude, double longitude)
    {
        if (!IsActive) return;

        _csvLogger.LogRecord(
            DateTime.Now, latitude, longitude,
            plotId, product, _lastSpeedKmh, 0,
            "PLOT_EXIT");
    }

    /// <summary>
    /// Logs a speed interlock event.
    /// </summary>
    public void LogSpeedInterlock(double currentSpeed, double targetSpeed, double tolerance)
    {
        if (!IsActive) return;

        _csvLogger.LogRecord(
            DateTime.Now, _lastLatitude, _lastLongitude,
            _lastPlotId, _lastProduct, currentSpeed, 0,
            $"SPEED_INTERLOCK: current={currentSpeed:F2} target={targetSpeed:F2} tol={tolerance:F0}%");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopSession();
        _csvLogger.Dispose();
        GC.SuppressFinalize(this);
    }

    // ════════════════════════════════════════════════════════════════════
    // Private
    // ════════════════════════════════════════════════════════════════════

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_disposed) return;

        double lat, lon, heading, speed;
        string? plotId, product;
        ushort mask;
        BoomState state;
        SensorSnapshot? sensor;

        lock (_stateLock)
        {
            lat = _lastLatitude;
            lon = _lastLongitude;
            heading = _lastHeading;
            speed = _lastSpeedKmh;
            plotId = _lastPlotId;
            product = _lastProduct;
            mask = _lastValveMask;
            state = _lastBoomState;
            sensor = _lastSensorSnapshot;
        }

        // Only log when actively spraying or in a relevant state
        if (state == BoomState.InPlot || state == BoomState.ApproachingPlot ||
            state == BoomState.LeavingPlot)
        {
            // Use sensor-aware logging if we have sensor data
            double? airBar = (sensor != null && !sensor.IsStale) ? sensor.AirPressureBar : null;
            double[]? flows = (sensor != null && !sensor.IsStale) ? sensor.FlowRatesLpm : null;

            _csvLogger.LogRecordWithSensors(
                DateTime.UtcNow, lat, lon, plotId, product, speed, mask,
                airBar, flows);
        }
    }
}
