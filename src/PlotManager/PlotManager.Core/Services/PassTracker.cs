using PlotManager.Core.Models;

namespace PlotManager.Core.Services;

/// <summary>
/// Tracks passes through the field grid.
///
/// A "pass" is one traverse of the grid column (2.8 m wide, all rows).
/// The tractor drives up (row 0 → N) or down (row N → 0).
///
/// Detection logic:
///   - Pass STARTS when the antenna enters any plot in a new column
///   - Pass ENDS when the antenna leaves the grid bounds
///   - Between start/end: speed locked, rate locked, deviation tracked
///   - Rate/nozzle changes only allowed after OnPassCompleted fires
///
/// Integration: call Update() from the main GPS loop with current position + speed.
/// </summary>
public class PassTracker
{
    // ── Configuration ──

    /// <summary>Speed deviation threshold (%) before warning fires.</summary>
    public double SpeedDeviationWarningPercent { get; set; } = 5.0;

    /// <summary>Speed deviation threshold (%) before error fires.</summary>
    public double SpeedDeviationErrorPercent { get; set; } = 15.0;

    /// <summary>Whether rate/nozzle changes are currently locked (during a pass).</summary>
    public bool IsChangeLocked => CurrentPass?.IsActive == true;

    // ── State ──

    /// <summary>Currently active pass (null if between passes).</summary>
    public PassState? CurrentPass { get; private set; }

    /// <summary>History of completed passes (for reporting).</summary>
    public List<PassState> CompletedPasses { get; } = new();

    /// <summary>Total number of passes (completed + current).</summary>
    public int TotalPassCount => CompletedPasses.Count + (CurrentPass?.IsActive == true ? 1 : 0);

    /// <summary>The grid being traversed.</summary>
    public PlotGrid? Grid { get; private set; }

    /// <summary>Last known column index (-1 = outside grid).</summary>
    public int LastColumnIndex { get; private set; } = -1;

    /// <summary>Last known row index (-1 = outside grid).</summary>
    public int LastRowIndex { get; private set; } = -1;

    // ── Events ──

    /// <summary>
    /// Fires when a new pass starts.
    /// Args: (PassState newPass)
    /// </summary>
    public event Action<PassState>? OnPassStarted;

    /// <summary>
    /// Fires when a pass completes (antenna leaves grid).
    /// Args: (PassState completedPass)
    /// Rate/nozzle changes are now allowed.
    /// </summary>
    public event Action<PassState>? OnPassCompleted;

    /// <summary>
    /// Fires when speed deviation exceeds warning threshold during a pass.
    /// Args: (double actualSpeedKmh, double lockedSpeedKmh, double deviationPercent, bool isError)
    /// isError = true when deviation exceeds SpeedDeviationErrorPercent.
    /// </summary>
    public event Action<double, double, double, bool>? OnSpeedDeviation;

    // ════════════════════════════════════════════════════════════════════
    // Setup
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configures the PassTracker with a plot grid.
    /// Must be called before Update().
    /// </summary>
    public void Configure(PlotGrid grid)
    {
        Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        Reset();
    }

    /// <summary>Resets all pass state (for a new trial run).</summary>
    public void Reset()
    {
        if (CurrentPass?.IsActive == true)
        {
            CurrentPass.Complete();
            CompletedPasses.Add(CurrentPass);
        }
        CurrentPass = null;
        LastColumnIndex = -1;
        LastRowIndex = -1;
    }

    // ════════════════════════════════════════════════════════════════════
    // Main loop — call from GPS update cycle
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Updates the tracker with a new GPS position and speed.
    /// Call every GPS cycle (~100ms at 10 Hz).
    /// </summary>
    /// <param name="position">Current GPS antenna position.</param>
    /// <param name="speedKmh">Current travel speed (km/h).</param>
    /// <param name="targetSpeedKmh">Locked target speed for this pass.</param>
    /// <param name="targetRateLPerHa">Target application rate for this pass.</param>
    public void Update(GeoPoint position, double speedKmh,
        double targetSpeedKmh = 0, double targetRateLPerHa = 0)
    {
        if (Grid == null) return;

        // Find which plot (if any) the position is in
        (int row, int col) = FindPlotIndices(position);

        if (row >= 0 && col >= 0)
        {
            // Inside a plot
            HandleInsideGrid(row, col, speedKmh, targetSpeedKmh, targetRateLPerHa);
        }
        else
        {
            // Outside the grid
            HandleOutsideGrid();
        }

        LastRowIndex = row;
        LastColumnIndex = col;
    }

    // ════════════════════════════════════════════════════════════════════
    // Internal logic
    // ════════════════════════════════════════════════════════════════════

    private void HandleInsideGrid(int row, int col, double speedKmh,
        double targetSpeedKmh, double targetRateLPerHa)
    {
        if (CurrentPass == null || !CurrentPass.IsActive)
        {
            StartNewPass(row, col, speedKmh, targetSpeedKmh, targetRateLPerHa);
        }
        else if (col != CurrentPass.ColumnIndex)
        {
            CompleteCurrentPass();
            StartNewPass(row, col, speedKmh, targetSpeedKmh, targetRateLPerHa);
        }

        // Track speed deviation within pass
        if (CurrentPass != null && CurrentPass.IsActive && CurrentPass.LockedSpeedKmh > 0)
        {
            CurrentPass.RecordSpeedSample(speedKmh);

            double deviation = Math.Abs(speedKmh - CurrentPass.LockedSpeedKmh)
                / CurrentPass.LockedSpeedKmh * 100.0;

            if (deviation >= SpeedDeviationWarningPercent)
            {
                bool isError = deviation >= SpeedDeviationErrorPercent;
                OnSpeedDeviation?.Invoke(speedKmh, CurrentPass.LockedSpeedKmh, deviation, isError);
            }
        }
    }

    private void StartNewPass(int row, int col, double speedKmh,
        double targetSpeedKmh, double targetRateLPerHa)
    {
        PassDirection dir = DetermineDirection(row);
        CurrentPass = new PassState
        {
            PassNumber = CompletedPasses.Count + 1,
            ColumnIndex = col,
            Direction = dir,
            LockedSpeedKmh = targetSpeedKmh > 0 ? targetSpeedKmh : speedKmh,
            LockedRateLPerHa = targetRateLPerHa,
            StartTimeUtc = DateTime.UtcNow,
        };
        OnPassStarted?.Invoke(CurrentPass);
    }

    private void HandleOutsideGrid()
    {
        if (CurrentPass?.IsActive == true)
        {
            CompleteCurrentPass();
        }
    }

    private void CompleteCurrentPass()
    {
        if (CurrentPass == null) return;

        CurrentPass.Complete();
        CompletedPasses.Add(CurrentPass);
        OnPassCompleted?.Invoke(CurrentPass);
    }

    /// <summary>
    /// Determines pass direction based on entry row.
    /// If entering from row 0 → going Up.
    /// If entering from last row → going Down.
    /// </summary>
    private PassDirection DetermineDirection(int entryRow)
    {
        if (Grid == null) return PassDirection.Up;

        // Bottom half (inclusive of midpoint for odd row counts) → going up
        // Top half → going down
        // For 3 rows: 0,1 = Up (< 2), 2 = Down (>= 2)
        // For 4 rows: 0,1 = Up (< 2), 2,3 = Down (>= 2)
        int midpoint = (Grid.Rows + 1) / 2; // Ceiling division: favors Up
        return entryRow < midpoint ? PassDirection.Up : PassDirection.Down;
    }

    /// <summary>
    /// Finds the (row, col) indices of the plot containing the position.
    /// Returns (-1, -1) if outside the grid.
    /// </summary>
    internal (int row, int col) FindPlotIndices(GeoPoint position)
    {
        if (Grid == null) return (-1, -1);

        // O(1) lookup: compute candidate indices from grid geometry
        // then verify with bounds check on the candidate plot.
        // Falls back to linear scan only if the geometric shortcut fails
        // (e.g., rotated grids where lat/lon don't align with rows/cols).

        if (Grid.HeadingDegrees == 0 && Grid.Rows > 0 && Grid.Columns > 0)
        {
            Plot origin = Grid.Plots[0, 0];
            double rowStep = Grid.PlotLengthMeters + Grid.BufferLengthMeters;
            double colStep = Grid.PlotWidthMeters + Grid.BufferWidthMeters;

            // Convert lat/lon to meters offset from origin
            double dLatMeters = (position.Latitude - origin.SouthWest.Latitude) * 110540.0;
            double cosLat = Math.Cos(origin.SouthWest.Latitude * Math.PI / 180.0);
            double dLonMeters = (position.Longitude - origin.SouthWest.Longitude) * 111320.0 * cosLat;

            int candidateRow = (int)(dLatMeters / rowStep);
            int candidateCol = (int)(dLonMeters / colStep);

            if (candidateRow >= 0 && candidateRow < Grid.Rows &&
                candidateCol >= 0 && candidateCol < Grid.Columns &&
                Grid.Plots[candidateRow, candidateCol].Contains(position))
            {
                return (candidateRow, candidateCol);
            }
        }

        // Fallback: linear scan (for rotated grids or edge cases)
        for (int row = 0; row < Grid.Rows; row++)
        {
            for (int col = 0; col < Grid.Columns; col++)
            {
                if (Grid.Plots[row, col].Contains(position))
                {
                    return (row, col);
                }
            }
        }

        return (-1, -1);
    }

    // ════════════════════════════════════════════════════════════════════
    // Queries
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets a summary of the current pass for UI display.
    /// </summary>
    public string GetStatusText()
    {
        if (CurrentPass == null || !CurrentPass.IsActive)
            return "Ожидание прохода...";

        return $"Проход {CurrentPass.PassNumber} " +
            $"(Кол. {CurrentPass.ColumnIndex + 1}, {(CurrentPass.Direction == PassDirection.Up ? "↑" : "↓")}) " +
            $"| Скорость: {CurrentPass.LockedSpeedKmh:F1} км/ч " +
            $"| Макс. откл.: {CurrentPass.MaxSpeedDeviationPercent:F1}%";
    }

    /// <summary>
    /// Returns true if rate/nozzle changes are currently allowed
    /// (i.e., not in the middle of a pass).
    /// </summary>
    public bool CanChangeRate() => !IsChangeLocked;
}
