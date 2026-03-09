using System.Text.Json.Serialization;

namespace PlotManager.Core.Models;

/// <summary>
/// Product definition for a trial — a specific chemical/treatment
/// with a target application rate.
/// </summary>
public class Product
{
    /// <summary>Product name (e.g. "Базагран 480 г/л").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Target application rate in liters per hectare.</summary>
    public double RateLPerHa { get; set; } = 200;

    /// <summary>
    /// Active ingredient concentration (%).
    /// 0 = ready-to-use solution. Typically 0 for tank mix.
    /// </summary>
    public double ConcentrationPercent { get; set; }

    /// <summary>Fluid type — affects hydraulic delays and viscosity.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FluidType FluidType { get; set; } = FluidType.WaterSolution;

    /// <summary>UI color for plot visualization (hex, e.g. "#FF6600").</summary>
    public string ColorHex { get; set; } = "#2196F3";

    /// <summary>True if this is a control treatment (water only).</summary>
    public bool IsControl { get; set; }

    /// <summary>Optional notes (e.g. "Tank mix with surfactant").</summary>
    public string Notes { get; set; } = string.Empty;

    public override string ToString() => IsControl ? $"{Name} (контроль)" : $"{Name} @ {RateLPerHa} л/га";
}
