namespace PlotManager.Core.Models.Trials;

/// <summary>
/// Implements the 'Quick Start' (Ad-hoc) linear trial.
/// Perfect for field operations: operator just drives straight, and the system
/// switches tanks based on a predetermined sequence.
/// </summary>
public class SequentialTrial : ITrialDesign
{
    public string Name { get; set; } = "Quick Start Trial";

    // Geometry
    public double PlotLengthMeters { get; set; } = 10.0;
    public double PlotWidthMeters { get; set; } = 2.8;
    public double GapMeters { get; set; } = 1.5;

    public int PlotsCount { get; set; } = 10;

    public int TotalPlots => PlotsCount;

    // Pattern (Tank / Product Sequence)
    // For example: [Product A, Product B, Water, Product A, Product B, Water...]
    public List<TrialProduct> ProductSequence { get; set; } = new();

    public double GetPlotLengthMeters(int plotIndex) => PlotLengthMeters;
    public double GetPlotWidthMeters() => PlotWidthMeters;

    public TrialProduct? GetProductForPlot(int plotIndex)
    {
        if (plotIndex < 0 || plotIndex >= PlotsCount) return null;
        if (ProductSequence.Count == 0) return null;

        // Loop the sequence if there are more plots than sequence items
        int sequenceIndex = plotIndex % ProductSequence.Count;
        return ProductSequence[sequenceIndex];
    }
}
