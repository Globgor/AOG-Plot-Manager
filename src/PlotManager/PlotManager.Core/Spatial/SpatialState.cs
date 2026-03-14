using PlotManager.Core.Models.Trials;

namespace PlotManager.Core.Spatial;

/// <summary>
/// Represents the continuous spatial evaluation result returned by the SpatialEngine.
/// </summary>
public class SpatialState
{
    /// <summary>True if the LookAhead point is strictly inside an active trial plot.</summary>
    public bool IsInsidePlot { get; set; }

    /// <summary>The product assigned to the current active plot, if any.</summary>
    public TrialProduct? ActiveProduct { get; set; }

    /// <summary>The ID or 0-based index of the plot currently being traversed.</summary>
    public int ActivePlotIndex { get; set; } = -1;
}
