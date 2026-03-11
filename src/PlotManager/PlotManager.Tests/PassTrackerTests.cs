using PlotManager.Core.Models;
using PlotManager.Core.Services;
using Xunit;

namespace PlotManager.Tests;

/// <summary>
/// Tests for PassTracker — pass detection, locking, deviation tracking.
/// Uses a simple 3×2 grid (3 rows, 2 columns) for predictable testing.
/// </summary>
public class PassTrackerTests
{
    private static PlotGrid CreateTestGrid()
    {
        // Simple 3-row × 2-column grid at origin (0, 0)
        // Each plot: 2.8m wide × 10m long, 0.5m buffer between columns
        var gen = new GridGenerator();
        return gen.Generate(new GridGenerator.GridParams
        {
            Rows = 3,
            Columns = 2,
            PlotWidthMeters = 2.8,
            PlotLengthMeters = 10.0,
            BufferWidthMeters = 0.5,
            BufferLengthMeters = 1.0,
            Origin = new GeoPoint(48.0, 30.0),
            HeadingDegrees = 0,
        });
    }

    /// <summary>Get a GPS point inside the center of plot [row, col].</summary>
    private static GeoPoint GetPlotCenter(PlotGrid grid, int row, int col)
    {
        Plot p = grid.Plots[row, col];
        return new GeoPoint(
            (p.SouthWest.Latitude + p.NorthEast.Latitude) / 2,
            (p.SouthWest.Longitude + p.NorthEast.Longitude) / 2);
    }

    /// <summary>Get a GPS point clearly outside the grid.</summary>
    private static GeoPoint GetOutsidePoint()
    {
        return new GeoPoint(47.0, 29.0); // Far from origin
    }

    // ════════════════════════════════════════════════════════════════════
    // Pass detection
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void EnterGrid_StartsPass()
    {
        PlotGrid grid = CreateTestGrid();
        var tracker = new PassTracker();
        tracker.Configure(grid);

        tracker.Update(GetPlotCenter(grid, 0, 0), 5.0, 5.0, 200);

        Assert.NotNull(tracker.CurrentPass);
        Assert.True(tracker.CurrentPass.IsActive);
        Assert.Equal(1, tracker.CurrentPass.PassNumber);
        Assert.Equal(0, tracker.CurrentPass.ColumnIndex);
    }

    [Fact]
    public void LeaveGrid_CompletesPass()
    {
        PlotGrid grid = CreateTestGrid();
        var tracker = new PassTracker();
        tracker.Configure(grid);

        // Enter grid
        tracker.Update(GetPlotCenter(grid, 0, 0), 5.0, 5.0, 200);
        Assert.True(tracker.IsChangeLocked);

        // Leave grid
        tracker.Update(GetOutsidePoint(), 0, 5.0);
        Assert.False(tracker.IsChangeLocked);
        Assert.Single(tracker.CompletedPasses);
        Assert.False(tracker.CompletedPasses[0].IsActive);
    }

    [Fact]
    public void MoveWithinSameColumn_SamePass()
    {
        PlotGrid grid = CreateTestGrid();
        var tracker = new PassTracker();
        tracker.Configure(grid);

        // Enter at row 0, col 0
        tracker.Update(GetPlotCenter(grid, 0, 0), 5.0, 5.0, 200);
        int passNum = tracker.CurrentPass!.PassNumber;

        // Move to row 1, same col 0
        tracker.Update(GetPlotCenter(grid, 1, 0), 5.0, 5.0, 200);
        Assert.Equal(passNum, tracker.CurrentPass!.PassNumber); // Same pass

        // Move to row 2, same col 0
        tracker.Update(GetPlotCenter(grid, 2, 0), 5.0, 5.0, 200);
        Assert.Equal(passNum, tracker.CurrentPass!.PassNumber); // Still same pass
    }

    [Fact]
    public void MoveToDifferentColumn_NewPass()
    {
        PlotGrid grid = CreateTestGrid();
        var tracker = new PassTracker();
        tracker.Configure(grid);

        // Pass 1: column 0
        tracker.Update(GetPlotCenter(grid, 0, 0), 5.0, 5.0, 200);
        Assert.Equal(1, tracker.CurrentPass!.PassNumber);

        // Cross to column 1 → new pass
        tracker.Update(GetPlotCenter(grid, 0, 1), 5.0, 5.0, 200);
        Assert.Equal(2, tracker.CurrentPass!.PassNumber);
        Assert.Equal(1, tracker.CurrentPass.ColumnIndex);
        Assert.Single(tracker.CompletedPasses); // Pass 1 completed
    }

    [Fact]
    public void TwoFullPasses_ExitAndReenter()
    {
        PlotGrid grid = CreateTestGrid();
        var tracker = new PassTracker();
        tracker.Configure(grid);

        // Pass 1
        tracker.Update(GetPlotCenter(grid, 0, 0), 5.0, 5.0, 200);
        tracker.Update(GetPlotCenter(grid, 1, 0), 5.0, 5.0, 200);
        tracker.Update(GetPlotCenter(grid, 2, 0), 5.0, 5.0, 200);
        tracker.Update(GetOutsidePoint(), 0, 5.0); // Exit

        // Pass 2
        tracker.Update(GetPlotCenter(grid, 2, 1), 5.0, 5.0, 200);
        tracker.Update(GetPlotCenter(grid, 1, 1), 5.0, 5.0, 200);
        tracker.Update(GetPlotCenter(grid, 0, 1), 5.0, 5.0, 200);
        tracker.Update(GetOutsidePoint(), 0, 5.0); // Exit

        Assert.Equal(2, tracker.CompletedPasses.Count);
        Assert.Equal(0, tracker.CompletedPasses[0].ColumnIndex);
        Assert.Equal(1, tracker.CompletedPasses[1].ColumnIndex);
    }

    // ════════════════════════════════════════════════════════════════════
    // Speed locking and deviation
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void SpeedDeviation_Tracked()
    {
        PlotGrid grid = CreateTestGrid();
        var tracker = new PassTracker();
        tracker.Configure(grid);

        tracker.Update(GetPlotCenter(grid, 0, 0), 5.0, 5.0, 200);
        tracker.Update(GetPlotCenter(grid, 1, 0), 5.5, 5.0, 200); // +10%
        tracker.Update(GetPlotCenter(grid, 2, 0), 4.5, 5.0, 200); // -10%

        Assert.Equal(3, tracker.CurrentPass!.SpeedSampleCount);
        Assert.Equal(10.0, tracker.CurrentPass.MaxSpeedDeviationPercent, 1);
    }

    [Fact]
    public void SpeedDeviation_FiresEvent()
    {
        PlotGrid grid = CreateTestGrid();
        var tracker = new PassTracker();
        tracker.Configure(grid);
        tracker.SpeedDeviationWarningPercent = 5.0;

        double? firedDeviation = null;
        tracker.OnSpeedDeviation += (actual, locked, dev, isError) => firedDeviation = dev;

        tracker.Update(GetPlotCenter(grid, 0, 0), 5.0, 5.0, 200);
        Assert.Null(firedDeviation); // 0% → no event

        tracker.Update(GetPlotCenter(grid, 1, 0), 5.5, 5.0, 200); // 10% → fires
        Assert.NotNull(firedDeviation);
        Assert.Equal(10.0, firedDeviation!.Value, 1);
    }

    // ════════════════════════════════════════════════════════════════════
    // Events
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void OnPassStarted_Fires()
    {
        PlotGrid grid = CreateTestGrid();
        var tracker = new PassTracker();
        tracker.Configure(grid);

        PassState? started = null;
        tracker.OnPassStarted += p => started = p;

        tracker.Update(GetPlotCenter(grid, 0, 0), 5.0, 5.0, 200);

        Assert.NotNull(started);
        Assert.Equal(1, started!.PassNumber);
    }

    [Fact]
    public void OnPassCompleted_Fires()
    {
        PlotGrid grid = CreateTestGrid();
        var tracker = new PassTracker();
        tracker.Configure(grid);

        PassState? completed = null;
        tracker.OnPassCompleted += p => completed = p;

        tracker.Update(GetPlotCenter(grid, 0, 0), 5.0, 5.0, 200);
        Assert.Null(completed); // Not yet

        tracker.Update(GetOutsidePoint(), 0, 5.0);
        Assert.NotNull(completed);
        Assert.False(completed!.IsActive);
    }

    // ════════════════════════════════════════════════════════════════════
    // Change locking
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void CanChangeRate_OnlyBetweenPasses()
    {
        PlotGrid grid = CreateTestGrid();
        var tracker = new PassTracker();
        tracker.Configure(grid);

        Assert.True(tracker.CanChangeRate()); // Before any pass

        tracker.Update(GetPlotCenter(grid, 0, 0), 5.0, 5.0, 200);
        Assert.False(tracker.CanChangeRate()); // During pass

        tracker.Update(GetOutsidePoint(), 0, 5.0);
        Assert.True(tracker.CanChangeRate()); // After pass
    }

    // ════════════════════════════════════════════════════════════════════
    // Direction detection
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Direction_BottomEntry_IsUp()
    {
        PlotGrid grid = CreateTestGrid();
        var tracker = new PassTracker();
        tracker.Configure(grid);

        tracker.Update(GetPlotCenter(grid, 0, 0), 5.0, 5.0, 200); // Row 0 = bottom
        Assert.Equal(PassDirection.Up, tracker.CurrentPass!.Direction);
    }

    [Fact]
    public void Direction_TopEntry_IsDown()
    {
        PlotGrid grid = CreateTestGrid();
        var tracker = new PassTracker();
        tracker.Configure(grid);

        tracker.Update(GetPlotCenter(grid, 2, 0), 5.0, 5.0, 200); // Row 2 = top (3 rows, so 2 >= 3/2)
        Assert.Equal(PassDirection.Down, tracker.CurrentPass!.Direction);
    }

    // ════════════════════════════════════════════════════════════════════
    // Status text
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void StatusText_OutsideGrid()
    {
        var tracker = new PassTracker();
        Assert.Contains("Очікування", tracker.GetStatusText());
    }

    [Fact]
    public void StatusText_DuringPass()
    {
        PlotGrid grid = CreateTestGrid();
        var tracker = new PassTracker();
        tracker.Configure(grid);

        tracker.Update(GetPlotCenter(grid, 0, 0), 5.0, 5.0, 200);
        string status = tracker.GetStatusText();

        Assert.Contains("Прохід 1", status);
        Assert.Contains("5.0", status);
    }
}
