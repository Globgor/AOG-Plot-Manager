namespace PlotManager.Core.Models;

/// <summary>
/// Immutable snapshot of calibrated sensor readings from the Teensy telemetry stream.
/// Raw voltage/Hz values are converted to physical units using MachineProfile calibration constants.
/// </summary>
public record SensorSnapshot
{
    /// <summary>Number of flow meter channels.</summary>
    public const int FlowMeterCount = 10;

    /// <summary>Pneumatic air pressure in Bar (calibrated from voltage).</summary>
    public required double AirPressureBar { get; init; }

    /// <summary>
    /// Flow rates in liters per minute for each boom (calibrated from Hz).
    /// Array always has exactly <see cref="FlowMeterCount"/> elements.
    /// </summary>
    public required double[] FlowRatesLpm { get; init; }

    /// <summary>UTC timestamp when this snapshot was created.</summary>
    public required DateTime TimestampUtc { get; init; }

    /// <summary>
    /// True if no telemetry has been received within the timeout window (default 2s).
    /// When stale, sensor values should be treated as unreliable.
    /// </summary>
    public bool IsStale { get; init; }

    /// <summary>
    /// Creates a "no data" snapshot with zero values and IsStale = true.
    /// </summary>
    public static SensorSnapshot CreateEmpty() => new()
    {
        AirPressureBar = 0,
        FlowRatesLpm = new double[FlowMeterCount],
        TimestampUtc = DateTime.UtcNow,
        IsStale = true,
    };
}

/// <summary>
/// Raw telemetry payload from Teensy JSON packet.
/// Deserialized from: {"AirV": 1.7, "FlowHz": [25.0, 0.0, ...]}
/// </summary>
public class RawTelemetry
{
    /// <summary>Air pressure sensor voltage (0.5V–4.5V typical).</summary>
    public double AirV { get; set; }

    /// <summary>
    /// Flow meter frequencies in Hz (pulse rate).
    /// Array should have 10 elements; missing elements default to 0.
    /// </summary>
    public double[]? FlowHz { get; set; }
}
