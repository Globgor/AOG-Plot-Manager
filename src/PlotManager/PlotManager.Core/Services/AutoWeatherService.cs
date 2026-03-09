using System.Diagnostics;

namespace PlotManager.Core.Services;

using PlotManager.Core.Models;

/// <summary>
/// Monitors GPS speed and triggers automatic weather data capture
/// when the tractor is stationary for a configurable duration.
///
/// State machine:
///   IDLE → (speed == 0 for StationaryThresholdMs) → TRIGGERED → IDLE
///
/// When triggered, fires OnWeatherFetchRequired so the UI can
/// open the weather dialog or query a serial weather station.
/// </summary>
public class AutoWeatherService : IDisposable
{
    private readonly Stopwatch _stationaryTimer = new();
    private bool _triggered;
    private bool _disposed;

    /// <summary>Duration in milliseconds of zero speed before triggering (default: 10,000 = 10s).</summary>
    public int StationaryThresholdMs { get; set; } = 10_000;

    /// <summary>Speed threshold below which we consider the tractor "stopped" (km/h).</summary>
    public double StoppedSpeedKmh { get; set; } = 0.1;

    /// <summary>Whether the tractor is currently considered stationary.</summary>
    public bool IsStationary { get; private set; }

    /// <summary>How long the tractor has been stationary (ms).</summary>
    public long StationaryDurationMs => _stationaryTimer.IsRunning ? _stationaryTimer.ElapsedMilliseconds : 0;

    /// <summary>Whether a weather fetch has been triggered and not yet consumed.</summary>
    public bool IsTriggered => _triggered;

    /// <summary>Fires when stationary threshold is met — UI should fetch weather data.</summary>
    public event Action? OnWeatherFetchRequired;

    /// <summary>Fires with stationary progress (for UI progress bar). Value: 0.0–1.0.</summary>
    public event Action<double>? OnStationaryProgress;

    /// <summary>
    /// COM port name for the weather station (e.g. "COM5"). Null = manual mode.
    /// </summary>
    public string? WeatherStationPort { get; set; }

    /// <summary>
    /// Called on every GPS update. Monitors speed and manages the stationary timer.
    /// </summary>
    public void UpdateSpeed(double speedKmh)
    {
        if (_disposed) return;

        if (speedKmh <= StoppedSpeedKmh)
        {
            if (!IsStationary)
            {
                IsStationary = true;
                _stationaryTimer.Restart();
                _triggered = false;
            }

            // Report progress
            double progress = Math.Min(1.0, (double)_stationaryTimer.ElapsedMilliseconds / StationaryThresholdMs);
            OnStationaryProgress?.Invoke(progress);

            // Check threshold
            if (!_triggered && _stationaryTimer.ElapsedMilliseconds >= StationaryThresholdMs)
            {
                _triggered = true;
                OnWeatherFetchRequired?.Invoke();
            }
        }
        else
        {
            if (IsStationary)
            {
                IsStationary = false;
                _stationaryTimer.Stop();
                _triggered = false;
                OnStationaryProgress?.Invoke(0);
            }
        }
    }

    /// <summary>
    /// Resets the trigger so it can fire again on the next stationary period.
    /// Call this after weather data has been consumed.
    /// </summary>
    public void ResetTrigger()
    {
        _triggered = false;
        _stationaryTimer.Reset();
    }

    /// <summary>
    /// Parses an NMEA WIMWV sentence (wind speed and direction).
    /// Format: $WIMWV,angle,R/T,speed,unit,status*checksum
    /// </summary>
    /// <returns>Tuple of (windAngleDeg, windSpeedMs) or null if invalid.</returns>
    public static (double AngleDeg, double SpeedMs)? ParseWimwv(string sentence)
    {
        if (!sentence.StartsWith("$WIMWV", StringComparison.OrdinalIgnoreCase))
            return null;

        // Strip checksum
        int starIdx = sentence.IndexOf('*');
        string body = starIdx > 0 ? sentence[..starIdx] : sentence;
        string[] parts = body.Split(',');

        if (parts.Length < 5) return null;

        if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double angle))
            return null;

        if (!double.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double speed))
            return null;

        // Convert to m/s if needed
        string unit = parts[4].Trim().ToUpperInvariant();
        double speedMs = unit switch
        {
            "N" => speed * 0.514444, // Knots → m/s
            "K" => speed / 3.6,      // km/h → m/s
            "M" => speed,             // m/s
            _ => speed,
        };

        return (angle, speedMs);
    }

    /// <summary>
    /// Parses an NMEA WIMDA sentence (meteorological composite).
    /// Extracts temperature and humidity.
    /// Format: $WIMDA,...,temperatureC,...,humidityPercent,...
    /// </summary>
    public static (double TempC, double Humidity)? ParseWimda(string sentence)
    {
        if (!sentence.StartsWith("$WIMDA", StringComparison.OrdinalIgnoreCase))
            return null;

        int starIdx = sentence.IndexOf('*');
        string body = starIdx > 0 ? sentence[..starIdx] : sentence;
        string[] parts = body.Split(',');

        // WIMDA has many fields. Temperature (°C) is typically at index 5, humidity at index 9
        if (parts.Length < 10) return null;

        double tempC = 0, humidity = 0;

        if (parts.Length > 5 && double.TryParse(parts[5], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double t))
            tempC = t;

        if (parts.Length > 9 && double.TryParse(parts[9], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double h))
            humidity = h;

        return (tempC, humidity);
    }

    public void Dispose()
    {
        _disposed = true;
        _stationaryTimer.Stop();
        GC.SuppressFinalize(this);
    }
}
