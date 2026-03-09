using PlotManager.Core.Models;

namespace PlotManager.Core.Services;

/// <summary>
/// Suggestion for a better nozzle when the current one can't achieve the target.
/// </summary>
public class NozzleSuggestion
{
    /// <summary>Suggested nozzle definition.</summary>
    public required NozzleDefinition Nozzle { get; init; }

    /// <summary>Required pressure for this nozzle at the target speed/rate.</summary>
    public double RequiredPressureBar { get; init; }

    /// <summary>Achievable speed with this nozzle at its reference pressure.</summary>
    public double SpeedAtRefPressureKmh { get; init; }

    /// <summary>Human-readable recommendation text.</summary>
    public string Recommendation { get; init; } = string.Empty;
}

/// <summary>
/// Result of a rate validation check.
/// </summary>
public class RateValidationResult
{
    /// <summary>Whether the configuration is valid (all within limits).</summary>
    public bool IsValid { get; init; }

    /// <summary>List of warnings (non-blocking issues).</summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>List of errors (blocking issues).</summary>
    public List<string> Errors { get; init; } = new();

    /// <summary>Calculated rate at the given parameters (L/ha).</summary>
    public double CalculatedRateLPerHa { get; init; }

    /// <summary>Recommended speed (km/h) — computed if pressure is provided.</summary>
    public double RecommendedSpeedKmh { get; init; }

    /// <summary>Required pressure (bar) — computed if speed is provided.</summary>
    public double RequiredPressureBar { get; init; }

    /// <summary>
    /// If the current nozzle can't achieve the target at the given speed constraints,
    /// this contains suggestions for better nozzles from the catalog.
    /// </summary>
    public List<NozzleSuggestion> NozzleSuggestions { get; init; } = new();

    /// <summary>True if speed was clamped to min/max constraints.</summary>
    public bool SpeedWasClamped { get; init; }
}

/// <summary>
/// Application rate calculator using the standard spray formula:
///
///   Rate (L/ha) = Q × 600 × n / (V × B)
///
/// Where:
///   Q = nozzle flow rate (L/min) at operating pressure
///   n = number of nozzles per boom
///   V = speed (km/h)
///   B = swath width (m)
///
/// Pressure correction via square-root law:
///   Q₂ = Q₁ × √(P₂ / P₁)
/// </summary>
public static class RateCalculator
{
    private const double Factor = 600.0; // Conversion constant: 10000 m²/ha ÷ 1000 L/m³ × 60 min/h

    /// <summary>
    /// Forward: calculates application rate (L/ha) from speed, nozzle, and pressure.
    /// </summary>
    /// <param name="nozzle">Nozzle definition with flow data.</param>
    /// <param name="pressureBar">Operating pressure (bar).</param>
    /// <param name="speedKmh">Travel speed (km/h).</param>
    /// <param name="swathWidthM">Boom swath width (m).</param>
    /// <param name="nozzlesPerBoom">Number of nozzles on the boom.</param>
    /// <returns>Application rate in liters per hectare.</returns>
    public static double CalculateRateLPerHa(
        NozzleDefinition nozzle,
        double pressureBar,
        double speedKmh,
        double swathWidthM,
        int nozzlesPerBoom = 1)
    {
        if (speedKmh <= 0 || swathWidthM <= 0 || nozzlesPerBoom <= 0) return 0;

        double flowRate = nozzle.GetFlowRateAtPressure(pressureBar);
        return flowRate * Factor * nozzlesPerBoom / (speedKmh * swathWidthM);
    }

    /// <summary>
    /// Inverse: calculates required speed (km/h) for a target rate.
    /// Given nozzle, pressure, and desired L/ha → speed.
    /// </summary>
    public static double CalculateSpeedKmh(
        NozzleDefinition nozzle,
        double pressureBar,
        double targetRateLPerHa,
        double swathWidthM,
        int nozzlesPerBoom = 1)
    {
        if (targetRateLPerHa <= 0 || swathWidthM <= 0 || nozzlesPerBoom <= 0) return 0;

        double flowRate = nozzle.GetFlowRateAtPressure(pressureBar);
        return flowRate * Factor * nozzlesPerBoom / (targetRateLPerHa * swathWidthM);
    }

    /// <summary>
    /// Inverse: calculates required pressure (bar) for a target rate at given speed.
    /// Given nozzle, speed, and desired L/ha → pressure.
    /// </summary>
    public static double CalculatePressureBar(
        NozzleDefinition nozzle,
        double targetRateLPerHa,
        double speedKmh,
        double swathWidthM,
        int nozzlesPerBoom = 1)
    {
        if (targetRateLPerHa <= 0 || speedKmh <= 0 || swathWidthM <= 0 || nozzlesPerBoom <= 0) return 0;

        // Required flow rate: Q = R × V × B / (600 × n)
        double requiredFlowRate = targetRateLPerHa * speedKmh * swathWidthM
            / (Factor * nozzlesPerBoom);

        // Pressure from flow rate: P₂ = Pref × (Q₂ / Qref)²
        return nozzle.GetPressureForFlowRate(requiredFlowRate);
    }

    /// <summary>
    /// Full validation: checks that the computed parameters are within
    /// nozzle operating range and reasonable speed limits.
    /// </summary>
    public static RateValidationResult Validate(
        NozzleDefinition nozzle,
        double pressureBar,
        double speedKmh,
        double targetRateLPerHa,
        double swathWidthM,
        int nozzlesPerBoom = 1,
        double minSpeedKmh = 2.0,
        double maxSpeedKmh = 10.0)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        // Calculate actual rate at given parameters
        double actualRate = CalculateRateLPerHa(nozzle, pressureBar, speedKmh, swathWidthM, nozzlesPerBoom);

        // Rate deviation
        double rateDeviation = targetRateLPerHa > 0
            ? Math.Abs(actualRate - targetRateLPerHa) / targetRateLPerHa * 100
            : 0;

        if (rateDeviation > 10)
            errors.Add($"Rate deviation {rateDeviation:F1}% exceeds 10% tolerance (actual: {actualRate:F1} L/ha vs target: {targetRateLPerHa:F1} L/ha).");
        else if (rateDeviation > 5)
            warnings.Add($"Rate deviation {rateDeviation:F1}% (actual: {actualRate:F1} L/ha vs target: {targetRateLPerHa:F1} L/ha).");

        // Pressure limits
        if (!nozzle.IsPressureInRange(pressureBar))
        {
            errors.Add($"Pressure {pressureBar:F1} bar is outside nozzle range ({nozzle.MinPressureBar:F1}–{nozzle.MaxPressureBar:F1} bar).");
        }

        // Speed limits
        if (speedKmh < minSpeedKmh)
            warnings.Add($"Speed {speedKmh:F1} km/h is below minimum {minSpeedKmh:F1} km/h.");
        if (speedKmh > maxSpeedKmh)
            warnings.Add($"Speed {speedKmh:F1} km/h exceeds maximum {maxSpeedKmh:F1} km/h.");

        // Compute recommendations
        double recommendedSpeed = CalculateSpeedKmh(nozzle, pressureBar, targetRateLPerHa, swathWidthM, nozzlesPerBoom);
        double requiredPressure = CalculatePressureBar(nozzle, targetRateLPerHa, speedKmh, swathWidthM, nozzlesPerBoom);

        return new RateValidationResult
        {
            IsValid = errors.Count == 0,
            Warnings = warnings,
            Errors = errors,
            CalculatedRateLPerHa = actualRate,
            RecommendedSpeedKmh = recommendedSpeed,
            RequiredPressureBar = requiredPressure,
        };
    }

    /// <summary>
    /// Smart auto-calculation with speed constraints and nozzle suggestion.
    ///
    /// Algorithm:
    /// 1. Compute ideal speed from formula (may be too slow).
    /// 2. Clamp speed to [minSpeedKmh, maxSpeedKmh].
    /// 3. If clamped → recalculate pressure needed at the clamped speed.
    /// 4. If pressure out of nozzle range → search catalog for a better nozzle.
    /// </summary>
    /// <param name="trial">Trial definition to populate.</param>
    /// <param name="targetPressureBar">Desired pressure (0 = auto).</param>
    /// <param name="minSpeedKmh">Minimum acceptable speed (km/h).</param>
    /// <param name="maxSpeedKmh">Maximum acceptable speed (km/h).</param>
    /// <param name="catalog">Optional nozzle catalog for auto-suggestion.</param>
    public static RateValidationResult AutoCalculate(
        TrialDefinition trial,
        double targetPressureBar = 0,
        double minSpeedKmh = 3.0,
        double maxSpeedKmh = 8.0,
        NozzleCatalog? catalog = null)
    {
        double maxRate = trial.GetMaxRateLPerHa();
        NozzleDefinition nozzle = trial.ActiveNozzle;

        // Step 1: Compute ideal speed at target or reference pressure
        double pressure = targetPressureBar > 0
            ? targetPressureBar
            : nozzle.ReferencePressureBar;
        double idealSpeed = CalculateSpeedKmh(
            nozzle, pressure, maxRate,
            trial.SwathWidthMeters, trial.NozzlesPerBoom);

        // Step 2: Clamp speed
        bool speedClamped = false;
        double speed = idealSpeed;
        if (speed < minSpeedKmh)
        {
            speed = minSpeedKmh;
            speedClamped = true;
        }
        else if (speed > maxSpeedKmh)
        {
            speed = maxSpeedKmh;
            speedClamped = true;
        }

        // Step 3: If speed was clamped, recalculate pressure
        if (speedClamped)
        {
            pressure = CalculatePressureBar(
                nozzle, maxRate, speed,
                trial.SwathWidthMeters, trial.NozzlesPerBoom);
        }

        // Verify actual rate at final speed + pressure
        double actualRate = CalculateRateLPerHa(
            nozzle, pressure, speed,
            trial.SwathWidthMeters, trial.NozzlesPerBoom);

        // Fill trial fields
        trial.RecommendedSpeedKmh = Math.Round(speed, 2);
        trial.RecommendedPressureBar = Math.Round(pressure, 2);
        trial.CalculatedRateLPerHa = Math.Round(actualRate, 1);

        // Step 4: Build nozzle suggestions if pressure is out of range
        var suggestions = new List<NozzleSuggestion>();
        bool pressureOk = nozzle.IsPressureInRange(pressure);

        if (!pressureOk && catalog != null)
        {
            foreach (NozzleDefinition candidate in catalog.Nozzles)
            {
                if (candidate.Model == nozzle.Model) continue;

                double candidatePressure = CalculatePressureBar(
                    candidate, maxRate, speed,
                    trial.SwathWidthMeters, trial.NozzlesPerBoom);

                if (candidate.IsPressureInRange(candidatePressure))
                {
                    double speedAtRef = CalculateSpeedKmh(
                        candidate, candidate.ReferencePressureBar, maxRate,
                        trial.SwathWidthMeters, trial.NozzlesPerBoom);

                    suggestions.Add(new NozzleSuggestion
                    {
                        Nozzle = candidate,
                        RequiredPressureBar = Math.Round(candidatePressure, 2),
                        SpeedAtRefPressureKmh = Math.Round(speedAtRef, 2),
                        Recommendation =
                            $"✅ {candidate.Model} ({candidate.IsoColorCode}): " +
                            $"давление {candidatePressure:F1} бар при {speed:F1} км/ч " +
                            $"(скорость при {candidate.ReferencePressureBar:F0} бар: {speedAtRef:F1} км/ч)",
                    });
                }
            }

            // Sort by pressure closest to nozzle reference (most comfortable operating point)
            suggestions.Sort((a, b) =>
                Math.Abs(a.RequiredPressureBar - a.Nozzle.ReferencePressureBar)
                    .CompareTo(Math.Abs(b.RequiredPressureBar - b.Nozzle.ReferencePressureBar)));
        }

        // Validate
        var result = Validate(
            nozzle, pressure, speed, maxRate,
            trial.SwathWidthMeters, trial.NozzlesPerBoom,
            minSpeedKmh, maxSpeedKmh);

        return new RateValidationResult
        {
            IsValid = result.IsValid,
            Warnings = result.Warnings,
            Errors = result.Errors,
            CalculatedRateLPerHa = result.CalculatedRateLPerHa,
            RecommendedSpeedKmh = speed,
            RequiredPressureBar = pressure,
            SpeedWasClamped = speedClamped,
            NozzleSuggestions = suggestions,
        };
    }
}
