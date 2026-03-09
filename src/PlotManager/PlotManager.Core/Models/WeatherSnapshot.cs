namespace PlotManager.Core.Models;

/// <summary>
/// Pre-trial weather metadata snapshot.
/// Recorded at the start of each trial session for scientific reproducibility.
/// Written as a header block in the As-Applied CSV log.
/// </summary>
public record WeatherSnapshot
{
    /// <summary>Air temperature in degrees Celsius.</summary>
    public required double TemperatureC { get; init; }

    /// <summary>Relative air humidity (0–100%).</summary>
    public required double HumidityPercent { get; init; }

    /// <summary>Wind speed in meters per second.</summary>
    public required double WindSpeedMs { get; init; }

    /// <summary>Wind direction as compass text (e.g. "N", "SW", "SSE").</summary>
    public string WindDirection { get; init; } = "N/A";

    /// <summary>Timestamp when snapshot was taken.</summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>Optional operator notes (e.g. "cloudy", "dew present").</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>
    /// Validates that weather conditions are within acceptable trial parameters.
    /// Returns list of warnings (empty = all clear).
    /// </summary>
    public List<string> Validate()
    {
        var warnings = new List<string>();

        if (TemperatureC < 5.0)
            warnings.Add($"Temperature too low ({TemperatureC:F1}°C): spray drift risk increases below 5°C.");
        if (TemperatureC > 35.0)
            warnings.Add($"Temperature too high ({TemperatureC:F1}°C): rapid evaporation risk.");
        if (HumidityPercent < 40.0)
            warnings.Add($"Humidity low ({HumidityPercent:F0}%): evaporation risk.");
        if (WindSpeedMs > 4.0)
            warnings.Add($"Wind speed high ({WindSpeedMs:F1} m/s): spray drift risk above 4 m/s.");

        return warnings;
    }
}
