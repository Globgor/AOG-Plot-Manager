namespace PlotManager.Core.Models;

/// <summary>
/// Direction of a single pass through the field grid.
/// </summary>
public enum PassDirection
{
    /// <summary>Driving from row 0 toward the last row.</summary>
    Up,

    /// <summary>Driving from the last row toward row 0.</summary>
    Down,
}

/// <summary>
/// Represents the state of a single pass (one traverse of the field).
/// A pass covers one column of the grid (2.8 m = 4 corn rows).
/// Speed and rate are locked for the entire pass duration.
/// </summary>
public class PassState
{
    /// <summary>Pass number (1-based, sequential).</summary>
    public int PassNumber { get; set; }

    /// <summary>Grid column index being traversed (0-based).</summary>
    public int ColumnIndex { get; set; }

    /// <summary>Driving direction for this pass.</summary>
    public PassDirection Direction { get; set; }

    /// <summary>Speed locked for this entire pass (km/h).</summary>
    public double LockedSpeedKmh { get; set; }

    /// <summary>Target application rate locked for this pass (L/ha).</summary>
    public double LockedRateLPerHa { get; set; }

    /// <summary>Nozzle model locked for this pass (cannot change mid-pass).</summary>
    public string LockedNozzleModel { get; set; } = string.Empty;

    /// <summary>Pass start timestamp (UTC).</summary>
    public DateTime StartTimeUtc { get; set; }

    /// <summary>Pass end timestamp (null if still in progress).</summary>
    public DateTime? EndTimeUtc { get; set; }

    /// <summary>Whether this pass is currently active.</summary>
    public bool IsActive => EndTimeUtc == null;

    /// <summary>Duration of this pass (or elapsed if still active).</summary>
    public TimeSpan Duration => (EndTimeUtc ?? DateTime.UtcNow) - StartTimeUtc;

    // ── Deviation tracking (updated in real-time during pass) ──

    /// <summary>Maximum speed deviation observed during this pass (%).</summary>
    public double MaxSpeedDeviationPercent { get; set; }

    /// <summary>Average speed deviation over the pass duration (%).</summary>
    public double AvgSpeedDeviationPercent { get; set; }

    /// <summary>Number of speed samples collected.</summary>
    public int SpeedSampleCount { get; set; }

    /// <summary>Running sum of absolute speed deviations (for averaging).</summary>
    internal double SpeedDeviationSum { get; set; }

    /// <summary>
    /// Records a speed sample and updates deviation statistics.
    /// </summary>
    /// <param name="actualSpeedKmh">Current measured speed.</param>
    public void RecordSpeedSample(double actualSpeedKmh)
    {
        if (LockedSpeedKmh <= 0) return;

        double deviationPercent = Math.Abs(actualSpeedKmh - LockedSpeedKmh) / LockedSpeedKmh * 100.0;

        if (deviationPercent > MaxSpeedDeviationPercent)
            MaxSpeedDeviationPercent = deviationPercent;

        SpeedDeviationSum += deviationPercent;
        SpeedSampleCount++;
        AvgSpeedDeviationPercent = SpeedDeviationSum / SpeedSampleCount;
    }

    /// <summary>Completes this pass.</summary>
    public void Complete()
    {
        EndTimeUtc = DateTime.UtcNow;
    }

    public override string ToString() =>
        $"Pass {PassNumber} (Col {ColumnIndex}, {Direction}) " +
        $"@ {LockedSpeedKmh:F1} km/h, {LockedRateLPerHa:F0} L/ha" +
        (IsActive ? " [ACTIVE]" : $" [{Duration.TotalSeconds:F0}s]");
}
