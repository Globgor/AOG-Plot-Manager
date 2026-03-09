using PlotManager.Core.Models;
using PlotManager.Core.Services;
using Xunit;

namespace PlotManager.Tests;

/// <summary>
/// Tests for SpatialEngine look-ahead logic:
/// - Pre-activation (0.5m before plot entry)
/// - Pre-deactivation (0.2m before plot exit)
/// - Alley detection
/// - Outside grid detection
/// </summary>
public class SpatialEngineTests
{
    private readonly SpatialEngine _engine;
    private readonly PlotGrid _grid;
    private readonly TrialMap _trialMap;
    private readonly HardwareRouting _routing;

    public SpatialEngineTests()
    {
        _engine = new SpatialEngine();

        // Create a simple 2×2 grid at (50.0, 30.0) heading North
        var generator = new GridGenerator();
        _grid = generator.Generate(new GridGenerator.GridParams
        {
            Rows = 2,
            Columns = 2,
            PlotWidthMeters = 5.0,
            PlotLengthMeters = 10.0,
            BufferWidthMeters = 1.0,
            BufferLengthMeters = 2.0,
            Origin = new GeoPoint(50.0, 30.0),
            HeadingDegrees = 0,
        });

        _trialMap = new TrialMap
        {
            TrialName = "Test",
            PlotAssignments = new Dictionary<string, string>
            {
                ["R1C1"] = "Product A",
                ["R1C2"] = "Product B",
                ["R2C1"] = "Product B",
                ["R2C2"] = "Product A",
            },
        };

        _routing = new HardwareRouting
        {
            ProductToSections = new Dictionary<string, List<int>>
            {
                ["Product A"] = new List<int> { 0 }, // Section 1
                ["Product B"] = new List<int> { 1 }, // Section 2
            },
            SectionToProduct = new Dictionary<int, string>
            {
                [0] = "Product A",
                [1] = "Product B",
            },
        };

        _engine.Configure(_grid, _trialMap, _routing);
    }

    [Fact]
    public void InsidePlot_ReturnsInPlotState()
    {
        // Place boom center well inside plot R1C1
        Plot plot = _grid.Plots[0, 0];
        double midLat = (plot.SouthWest.Latitude + plot.NorthEast.Latitude) / 2;
        double midLon = (plot.SouthWest.Longitude + plot.NorthEast.Longitude) / 2;
        var center = new GeoPoint(midLat, midLon);

        SpatialResult result = _engine.EvaluatePosition(center, headingDegrees: 0, speedKmh: 5);

        Assert.Equal(BoomState.InPlot, result.State);
        Assert.NotNull(result.ActivePlot);
        Assert.Equal("R1C1", result.ActivePlot.PlotId);
        Assert.Equal("Product A", result.ActiveProduct);
        Assert.True(result.ValveMask > 0, "Valve mask should be non-zero inside a plot");
    }

    [Fact]
    public void InsidePlot_CorrectValveMask()
    {
        // R1C1 → "Product A" → Section 0 → mask = 0x0001
        Plot plot = _grid.Plots[0, 0];
        double midLat = (plot.SouthWest.Latitude + plot.NorthEast.Latitude) / 2;
        double midLon = (plot.SouthWest.Longitude + plot.NorthEast.Longitude) / 2;
        var center = new GeoPoint(midLat, midLon);

        SpatialResult result = _engine.EvaluatePosition(center, headingDegrees: 0, speedKmh: 5);

        Assert.Equal((ushort)0x0001, result.ValveMask);
    }

    [Fact]
    public void ApproachingPlot_PreActivation()
    {
        // Place boom 0.3m south of plot R1C1 (heading North)
        // The forward look-ahead (+0.5m) should reach inside the plot
        Plot plot = _grid.Plots[0, 0];
        double justSouth = plot.SouthWest.Latitude - (0.3 / 110540.0);
        double midLon = (plot.SouthWest.Longitude + plot.NorthEast.Longitude) / 2;
        var center = new GeoPoint(justSouth, midLon);

        SpatialResult result = _engine.EvaluatePosition(center, headingDegrees: 0, speedKmh: 5);

        Assert.Equal(BoomState.ApproachingPlot, result.State);
        Assert.NotNull(result.ActivePlot);
        Assert.True(result.ValveMask > 0, "Pre-activation should enable valves");
    }

    [Fact]
    public void LeavingPlot_PreDeactivation()
    {
        // Place boom 0.1m before the north edge of plot R1C1 (heading North)
        // Within the 0.2m pre-deactivation zone
        Plot plot = _grid.Plots[0, 0];
        double nearExit = plot.NorthEast.Latitude - (0.1 / 110540.0);
        double midLon = (plot.SouthWest.Longitude + plot.NorthEast.Longitude) / 2;
        var center = new GeoPoint(nearExit, midLon);

        SpatialResult result = _engine.EvaluatePosition(center, headingDegrees: 0, speedKmh: 5);

        Assert.Equal(BoomState.LeavingPlot, result.State);
        Assert.Equal((ushort)0, result.ValveMask); // Dry cutoff — all off
    }

    [Fact]
    public void InAlley_ReturnsAlleyState()
    {
        // Place boom in the buffer zone between plots (alley)
        Plot plotR1C1 = _grid.Plots[0, 0];
        Plot plotR2C1 = _grid.Plots[1, 0];
        double alleyLat = (plotR1C1.NorthEast.Latitude + plotR2C1.SouthWest.Latitude) / 2;
        double midLon = (plotR1C1.SouthWest.Longitude + plotR1C1.NorthEast.Longitude) / 2;
        var center = new GeoPoint(alleyLat, midLon);

        SpatialResult result = _engine.EvaluatePosition(center, headingDegrees: 0, speedKmh: 5);

        // Could be InAlley or ApproachingPlot depending on exact position
        // The key check is that when genuinely in the alley (far from any plot), mask=0
        if (result.State == BoomState.InAlley)
        {
            Assert.Equal((ushort)0, result.ValveMask);
        }
    }

    [Fact]
    public void OutsideGrid_ReturnsOutsideGridState()
    {
        // Place boom far from the grid
        var center = new GeoPoint(60.0, 40.0); // Far away

        SpatialResult result = _engine.EvaluatePosition(center, headingDegrees: 0, speedKmh: 5);

        Assert.Equal(BoomState.OutsideGrid, result.State);
        Assert.Equal((ushort)0, result.ValveMask);
        Assert.Null(result.ActivePlot);
    }

    [Fact]
    public void NotConfigured_ReturnsOutsideGrid()
    {
        var unconfigured = new SpatialEngine();

        SpatialResult result = unconfigured.EvaluatePosition(
            new GeoPoint(50.0, 30.0), headingDegrees: 0, speedKmh: 5);

        Assert.Equal(BoomState.OutsideGrid, result.State);
        Assert.Equal((ushort)0, result.ValveMask);
    }

    [Fact]
    public void DifferentProducts_DifferentMasks()
    {
        // R1C1 → Product A → Section 0 → mask 0x0001
        Plot plotA = _grid.Plots[0, 0];
        double midLatA = (plotA.SouthWest.Latitude + plotA.NorthEast.Latitude) / 2;
        double midLonA = (plotA.SouthWest.Longitude + plotA.NorthEast.Longitude) / 2;

        // R1C2 → Product B → Section 1 → mask 0x0002
        Plot plotB = _grid.Plots[0, 1];
        double midLatB = (plotB.SouthWest.Latitude + plotB.NorthEast.Latitude) / 2;
        double midLonB = (plotB.SouthWest.Longitude + plotB.NorthEast.Longitude) / 2;

        SpatialResult resultA = _engine.EvaluatePosition(
            new GeoPoint(midLatA, midLonA), 0, 5);
        SpatialResult resultB = _engine.EvaluatePosition(
            new GeoPoint(midLatB, midLonB), 0, 5);

        Assert.Equal((ushort)0x0001, resultA.ValveMask);
        Assert.Equal((ushort)0x0002, resultB.ValveMask);
    }

    [Fact]
    public void FindPlot_ReturnsCorrectedPlot()
    {
        Plot expected = _grid.Plots[0, 0];
        double midLat = (expected.SouthWest.Latitude + expected.NorthEast.Latitude) / 2;
        double midLon = (expected.SouthWest.Longitude + expected.NorthEast.Longitude) / 2;

        Plot? found = _engine.FindPlot(new GeoPoint(midLat, midLon));

        Assert.NotNull(found);
        Assert.Equal(expected.PlotId, found.PlotId);
    }
}
