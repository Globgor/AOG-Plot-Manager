namespace PlotManager.Core.Services;

/// <summary>
/// "Passive Navigator" (Пассивный Штурман) for air-pressurized sprayer systems.
/// Since flow is constant under a given air pressure, the only way to change
/// the application rate (L/ha) is to change the tractor's speed.
/// This controller calculates the absolute target speed and error margin.
/// </summary>
public class PneumaticRateController
{
    private readonly double _flowRateLPerMin;
    private readonly double _swathWidthMeters;

    /// <summary>
    /// Initializes the controller with the physical constants of the sprayer.
    /// </summary>
    /// <param name="flowRateLPerMin">The sum flow of all open nozzles (L/min) at the set operating pressure.</param>
    /// <param name="swathWidthMeters">The active width of the boom (meters).</param>
    public PneumaticRateController(double flowRateLPerMin, double swathWidthMeters)
    {
        if (flowRateLPerMin <= 0) throw new ArgumentException("Flow rate must be > 0", nameof(flowRateLPerMin));
        if (swathWidthMeters <= 0) throw new ArgumentException("Swath width must be > 0", nameof(swathWidthMeters));

        _flowRateLPerMin = flowRateLPerMin;
        _swathWidthMeters = swathWidthMeters;
    }

    /// <summary>
    /// Calculates the ideal target speed (km/h) required to achieve the target application rate.
    /// Formula: Speed (km/h) = (Flow (L/min) * 600) / (Rate (L/ha) * Width (m))
    /// </summary>
    public double CalculateTargetSpeedKmh(double targetRateLPerHa)
    {
        if (targetRateLPerHa <= 0) return 0.0;
        
        return (_flowRateLPerMin * 600.0) / (targetRateLPerHa * _swathWidthMeters);
    }

    /// <summary>
    /// Reverse calculates the actual applied rate based on current actual speed.
    /// Formula: Rate (L/ha) = (Flow (L/min) * 600) / (Speed (km/h) * Width (m))
    /// </summary>
    public double CalculateActualRateLPerHa(double actualSpeedKmh)
    {
        if (actualSpeedKmh <= 0.1) return 0.0; // Avoid division by zero, tractor is essentially stopped

        return (_flowRateLPerMin * 600.0) / (actualSpeedKmh * _swathWidthMeters);
    }

    /// <summary>
    /// Evaluates speed compliance, returning the error percentage.
    /// positive % = driving too fast (under-applying).
    /// negative % = driving too slow (over-applying).
    /// </summary>
    public double CalculateSpeedErrorPercentage(double targetSpeedKmh, double actualSpeedKmh)
    {
        if (targetSpeedKmh <= 0) return 0.0;
        return ((actualSpeedKmh - targetSpeedKmh) / targetSpeedKmh) * 100.0;
    }
}
