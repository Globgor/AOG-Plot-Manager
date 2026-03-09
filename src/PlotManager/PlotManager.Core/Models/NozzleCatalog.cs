using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlotManager.Core.Models;

/// <summary>
/// Nozzle definition with flow characteristics.
/// Stored in the nozzle catalog — operator creates once, selects per trial.
/// 
/// Flow rate scales with pressure by the square-root law:
///   Q₂ = Q₁ × √(P₂ / P₁)
/// </summary>
public class NozzleDefinition
{
    /// <summary>Nozzle model identifier (e.g. "TeeJet XR 110-03").</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Nominal flow rate at reference pressure (liters per minute).
    /// This is the value from the nozzle datasheet.
    /// </summary>
    public double FlowRateLPerMinAtRef { get; set; } = 1.18;

    /// <summary>Reference pressure for the nominal flow rate (bar).</summary>
    public double ReferencePressureBar { get; set; } = 3.0;

    /// <summary>Spray angle in degrees (e.g. 110).</summary>
    public int SprayAngleDegrees { get; set; } = 110;

    /// <summary>Minimum recommended operating pressure (bar).</summary>
    public double MinPressureBar { get; set; } = 1.5;

    /// <summary>Maximum recommended operating pressure (bar).</summary>
    public double MaxPressureBar { get; set; } = 6.0;

    /// <summary>ISO color code (e.g. "Blue", "Green", "Yellow").</summary>
    public string IsoColorCode { get; set; } = string.Empty;

    /// <summary>Optional notes.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Calculates flow rate at a given pressure using the square-root law.
    /// Q₂ = Qref × √(P₂ / Pref)
    /// </summary>
    /// <param name="pressureBar">Actual operating pressure (bar).</param>
    /// <returns>Flow rate in liters per minute.</returns>
    public double GetFlowRateAtPressure(double pressureBar)
    {
        if (pressureBar <= 0 || ReferencePressureBar <= 0) return 0;
        return FlowRateLPerMinAtRef * Math.Sqrt(pressureBar / ReferencePressureBar);
    }

    /// <summary>
    /// Calculates the pressure required to achieve a target flow rate.
    /// P₂ = Pref × (Qtarget / Qref)²
    /// </summary>
    /// <param name="targetFlowRateLPerMin">Desired flow rate (L/min).</param>
    /// <returns>Required pressure in bar.</returns>
    public double GetPressureForFlowRate(double targetFlowRateLPerMin)
    {
        if (targetFlowRateLPerMin <= 0 || FlowRateLPerMinAtRef <= 0) return 0;
        double ratio = targetFlowRateLPerMin / FlowRateLPerMinAtRef;
        return ReferencePressureBar * ratio * ratio;
    }

    /// <summary>Checks if the given pressure is within the nozzle's operating range.</summary>
    public bool IsPressureInRange(double pressureBar) =>
        pressureBar >= MinPressureBar && pressureBar <= MaxPressureBar;

    public override string ToString() => $"{Model} ({IsoColorCode}) @ {FlowRateLPerMinAtRef:F2} L/min";
}

/// <summary>
/// Catalog of nozzle definitions. Persisted as JSON.
/// Operator creates nozzle entries once, then selects per trial.
/// </summary>
public class NozzleCatalog
{
    /// <summary>All nozzle definitions in the catalog.</summary>
    public List<NozzleDefinition> Nozzles { get; set; } = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Finds a nozzle by model name (case-insensitive).</summary>
    public NozzleDefinition? FindByModel(string model) =>
        Nozzles.FirstOrDefault(n =>
            n.Model.Equals(model, StringComparison.OrdinalIgnoreCase));

    /// <summary>Serializes the catalog to JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, _jsonOptions);

    /// <summary>Deserializes a catalog from JSON.</summary>
    public static NozzleCatalog FromJson(string json) =>
        JsonSerializer.Deserialize<NozzleCatalog>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Invalid nozzle catalog JSON.");

    /// <summary>Saves catalog to file.</summary>
    public void SaveToFile(string path) => File.WriteAllText(path, ToJson());

    /// <summary>Loads catalog from file.</summary>
    public static NozzleCatalog LoadFromFile(string path) =>
        FromJson(File.ReadAllText(path));

    /// <summary>Creates a default catalog with common TeeJet nozzles.</summary>
    public static NozzleCatalog CreateDefault()
    {
        return new NozzleCatalog
        {
            Nozzles = new List<NozzleDefinition>
            {
                new() { Model = "TeeJet XR 110-01", FlowRateLPerMinAtRef = 0.39, SprayAngleDegrees = 110, IsoColorCode = "Orange", MinPressureBar = 1.0, MaxPressureBar = 6.0 },
                new() { Model = "TeeJet XR 110-02", FlowRateLPerMinAtRef = 0.79, SprayAngleDegrees = 110, IsoColorCode = "Green", MinPressureBar = 1.0, MaxPressureBar = 6.0 },
                new() { Model = "TeeJet XR 110-03", FlowRateLPerMinAtRef = 1.18, SprayAngleDegrees = 110, IsoColorCode = "Blue", MinPressureBar = 1.0, MaxPressureBar = 6.0 },
                new() { Model = "TeeJet XR 110-04", FlowRateLPerMinAtRef = 1.57, SprayAngleDegrees = 110, IsoColorCode = "Red", MinPressureBar = 1.0, MaxPressureBar = 6.0 },
                new() { Model = "TeeJet XR 110-05", FlowRateLPerMinAtRef = 1.96, SprayAngleDegrees = 110, IsoColorCode = "Brown", MinPressureBar = 1.0, MaxPressureBar = 6.0 },
            },
        };
    }
}
