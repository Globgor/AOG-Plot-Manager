namespace PlotManager.Core.Models.Trials;

/// <summary>
/// Represents a targeted application product with a specific rate constraint.
/// </summary>
public class TrialProduct
{
    public string Name { get; set; } = string.Empty;
    public double TargetRateLPerHa { get; set; }
    public bool IsControl { get; set; }
    public string ColorHex { get; set; } = "#FFCCCCCC";
}

/// <summary>
/// A universal contract for trial designs.
/// Hides the complexity of whether it's a simple quick-start or a complex randomized matrix.
/// </summary>
public interface ITrialDesign
{
    /// <summary>Trial name (e.g. "Quick Start 1" or "Excel Matrix 2").</summary>
    string Name { get; set; }

    /// <summary>Total number of plots in this trial.</summary>
    int TotalPlots { get; }

    /// <summary>Returns the length of a specific plot in meters.</summary>
    double GetPlotLengthMeters(int plotIndex);

    /// <summary>Returns the width of the trial swaths in meters.</summary>
    double GetPlotWidthMeters();

    /// <summary>Returns the product assigned to a specific plot index (0-based).</summary>
    TrialProduct? GetProductForPlot(int plotIndex);
}
