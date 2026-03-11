using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlotManager.Core.Models;

/// <summary>Nozzle construction type (affects drift and droplet size).</summary>
public enum NozzleType
{
    /// <summary>Standard flat-fan (стандартна щілинна).</summary>
    Slit,

    /// <summary>Air-induction / venturi (інжекторна, крупна крапля).</summary>
    Injector,
}

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
    /// Per ISO 10625, reference pressure is 300 kPa (2.76 bar / 40 PSI).
    /// </summary>
    public double FlowRateLPerMinAtRef { get; set; } = 1.14;

    /// <summary>
    /// Reference pressure for the nominal flow rate (bar).
    /// ISO 10625 standard: 300 kPa = 2.76 bar ≈ 40 PSI.
    /// </summary>
    public double ReferencePressureBar { get; set; } = 2.76;

    /// <summary>Spray angle in degrees (e.g. 110).</summary>
    public int SprayAngleDegrees { get; set; } = 110;

    /// <summary>Minimum recommended operating pressure (bar).</summary>
    public double MinPressureBar { get; set; } = 1.5;

    /// <summary>Maximum recommended operating pressure (bar).</summary>
    public double MaxPressureBar { get; set; } = 6.0;

    /// <summary>ISO color code (e.g. "Blue", "Green", "Yellow").</summary>
    public string IsoColorCode { get; set; } = string.Empty;

    /// <summary>Nozzle construction type — slit (flat-fan) or injector (air-induction).</summary>
    public NozzleType Type { get; set; } = NozzleType.Slit;

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

    /// <summary>Short label for UI display: model, color, type, flow.</summary>
    public override string ToString()
    {
        string typeTag = Type == NozzleType.Injector ? "інж" : "щіл";
        return $"{Model} [{typeTag}] ({IsoColorCode}) @ {FlowRateLPerMinAtRef:F2} л/хв";
    }
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

    /// <summary>
    /// Creates a default catalog with common slit and injector nozzles.
    /// Flow rates are per ISO 10625 at 300 kPa (2.76 bar / 40 PSI).
    /// </summary>
    public static NozzleCatalog CreateDefault()
    {
        return new NozzleCatalog
        {
            Nozzles = new List<NozzleDefinition>
            {
                // ── Щілинні (Slit / flat-fan) — TeeJet XR series ──
                // Ref: TeeJet Catalog 51A-M, ISO 10625 colors
                // Pressure range: 1.0–4.14 bar (15–60 PSI)
                new() { Model = "TeeJet XR 110-01",  FlowRateLPerMinAtRef = 0.38, ReferencePressureBar = 2.76, SprayAngleDegrees = 110, IsoColorCode = "Orange", Type = NozzleType.Slit, MinPressureBar = 1.0, MaxPressureBar = 4.14 },
                new() { Model = "TeeJet XR 110-015", FlowRateLPerMinAtRef = 0.57, ReferencePressureBar = 2.76, SprayAngleDegrees = 110, IsoColorCode = "Green",  Type = NozzleType.Slit, MinPressureBar = 1.0, MaxPressureBar = 4.14 },
                new() { Model = "TeeJet XR 110-02",  FlowRateLPerMinAtRef = 0.76, ReferencePressureBar = 2.76, SprayAngleDegrees = 110, IsoColorCode = "Yellow", Type = NozzleType.Slit, MinPressureBar = 1.0, MaxPressureBar = 4.14 },
                new() { Model = "TeeJet XR 110-03",  FlowRateLPerMinAtRef = 1.14, ReferencePressureBar = 2.76, SprayAngleDegrees = 110, IsoColorCode = "Blue",   Type = NozzleType.Slit, MinPressureBar = 1.0, MaxPressureBar = 4.14 },
                new() { Model = "TeeJet XR 110-04",  FlowRateLPerMinAtRef = 1.51, ReferencePressureBar = 2.76, SprayAngleDegrees = 110, IsoColorCode = "Red",    Type = NozzleType.Slit, MinPressureBar = 1.0, MaxPressureBar = 4.14 },
                new() { Model = "TeeJet XR 110-05",  FlowRateLPerMinAtRef = 1.89, ReferencePressureBar = 2.76, SprayAngleDegrees = 110, IsoColorCode = "Brown",  Type = NozzleType.Slit, MinPressureBar = 1.0, MaxPressureBar = 4.14 },
                new() { Model = "TeeJet XR 110-06",  FlowRateLPerMinAtRef = 2.27, ReferencePressureBar = 2.76, SprayAngleDegrees = 110, IsoColorCode = "Grey",   Type = NozzleType.Slit, MinPressureBar = 1.0, MaxPressureBar = 4.14 },
                new() { Model = "TeeJet XR 110-08",  FlowRateLPerMinAtRef = 3.03, ReferencePressureBar = 2.76, SprayAngleDegrees = 110, IsoColorCode = "White",  Type = NozzleType.Slit, MinPressureBar = 1.0, MaxPressureBar = 4.14 },

                // ── Інжекторні (Air-induction) — TeeJet AI series ──
                // Same ISO flow rates, higher min pressure for air-induction
                // Pressure range: 2.0–8.0 bar
                new() { Model = "TeeJet AI 110-02", FlowRateLPerMinAtRef = 0.76, ReferencePressureBar = 2.76, SprayAngleDegrees = 110, IsoColorCode = "Yellow", Type = NozzleType.Injector, MinPressureBar = 2.0, MaxPressureBar = 8.0, Notes = "Крупна крапля, антизносний" },
                new() { Model = "TeeJet AI 110-03", FlowRateLPerMinAtRef = 1.14, ReferencePressureBar = 2.76, SprayAngleDegrees = 110, IsoColorCode = "Blue",   Type = NozzleType.Injector, MinPressureBar = 2.0, MaxPressureBar = 8.0, Notes = "Крупна крапля, антизносний" },
                new() { Model = "TeeJet AI 110-04", FlowRateLPerMinAtRef = 1.51, ReferencePressureBar = 2.76, SprayAngleDegrees = 110, IsoColorCode = "Red",    Type = NozzleType.Injector, MinPressureBar = 2.0, MaxPressureBar = 8.0, Notes = "Крупна крапля, антизносний" },
                new() { Model = "TeeJet AI 110-05", FlowRateLPerMinAtRef = 1.89, ReferencePressureBar = 2.76, SprayAngleDegrees = 110, IsoColorCode = "Brown",  Type = NozzleType.Injector, MinPressureBar = 2.0, MaxPressureBar = 8.0, Notes = "Крупна крапля, антизносний" },

                // ── Інжекторні — Lechler IDK series ──
                // 120° spray angle, similar flow rates to ISO sizes
                new() { Model = "Lechler IDK 120-02", FlowRateLPerMinAtRef = 0.76, ReferencePressureBar = 2.76, SprayAngleDegrees = 120, IsoColorCode = "Yellow", Type = NozzleType.Injector, MinPressureBar = 2.0, MaxPressureBar = 8.0, Notes = "Інжектор, 120° кут" },
                new() { Model = "Lechler IDK 120-03", FlowRateLPerMinAtRef = 1.14, ReferencePressureBar = 2.76, SprayAngleDegrees = 120, IsoColorCode = "Blue",   Type = NozzleType.Injector, MinPressureBar = 2.0, MaxPressureBar = 8.0, Notes = "Інжектор, 120° кут" },
                new() { Model = "Lechler IDK 120-04", FlowRateLPerMinAtRef = 1.51, ReferencePressureBar = 2.76, SprayAngleDegrees = 120, IsoColorCode = "Red",    Type = NozzleType.Injector, MinPressureBar = 2.0, MaxPressureBar = 8.0, Notes = "Інжектор, 120° кут" },
            },
        };
    }
}
