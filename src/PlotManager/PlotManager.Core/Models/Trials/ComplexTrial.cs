namespace PlotManager.Core.Models.Trials;

/// <summary>
/// Implements a complex, randomized trial design.
/// Typically loaded from an Excel file, consisting of a full 2D grid matrix
/// where plots are explicitly assigned varying products and target rates.
/// </summary>
public class ComplexTrial : ITrialDesign
{
    public string Name { get; set; } = "Imported Excel Trial";

    // Geometry
    public double DefaultPlotLengthMeters { get; set; } = 10.0;
    public double DefaultPlotWidthMeters { get; set; } = 2.8;

    // A dictionary mapping Plot ID (e.g. "R1C1" or index) to a product/rate
    public Dictionary<int, TrialProduct> PlotAssignments { get; set; } = new();

    public int TotalPlots => PlotAssignments.Count;

    public double GetPlotLengthMeters(int plotIndex) => DefaultPlotLengthMeters;
    public double GetPlotWidthMeters() => DefaultPlotWidthMeters;

    public TrialProduct? GetProductForPlot(int plotIndex)
    {
        return PlotAssignments.TryGetValue(plotIndex, out var product) ? product : null;
    }
}
