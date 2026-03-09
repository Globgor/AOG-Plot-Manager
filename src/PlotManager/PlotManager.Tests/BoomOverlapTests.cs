using PlotManager.Core.Models;
using PlotManager.Core.Services;
using Xunit;

namespace PlotManager.Tests;

/// <summary>
/// Tests for boom overlap percentage thresholds:
/// - CalculateOverlap geometry
/// - Activation threshold (70% default)
/// - Deactivation threshold (30% default) with hysteresis
/// </summary>
public class BoomOverlapTests
{
    private static Plot MakePlot(double southLat, double westLon, double northLat, double eastLon)
    {
        return new Plot
        {
            Row = 0, Column = 0,
            WidthMeters = 5, LengthMeters = 10,
            SouthWest = new GeoPoint(southLat, westLon),
            NorthEast = new GeoPoint(northLat, eastLon),
        };
    }

    // ════════════════════════════════════════════════════════════════════
    // CalculateOverlap
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Overlap_FullyInside_Returns100()
    {
        var boom = new Boom { BoomId = 0, ValveChannel = 0, SprayWidthMeters = 0.25 };
        var plot = MakePlot(50.0, 30.0, 50.001, 30.001);

        // Boom center well inside the plot, heading North
        double overlap = boom.CalculateOverlap(new GeoPoint(50.0005, 30.0005), plot, 0);

        Assert.Equal(100, overlap, 1);
    }

    [Fact]
    public void Overlap_FullyOutside_Returns0()
    {
        var boom = new Boom { BoomId = 0, ValveChannel = 0, SprayWidthMeters = 0.25 };
        var plot = MakePlot(50.0, 30.0, 50.001, 30.001);

        // Boom center far outside the plot
        double overlap = boom.CalculateOverlap(new GeoPoint(51.0, 31.0), plot, 0);

        Assert.Equal(0, overlap, 1);
    }

    [Fact]
    public void Overlap_HalfInside_ReturnsAbout50()
    {
        var boom = new Boom { BoomId = 0, ValveChannel = 0, SprayWidthMeters = 0.50 };

        // Create a plot. Boom center at the south edge — half the spray is inside
        double plotSouthLat = 50.0;
        double plotNorthLat = 50.001;
        var plot = MakePlot(plotSouthLat, 30.0, plotNorthLat, 30.001);

        // Position boom right at the south boundary, heading North
        double overlap = boom.CalculateOverlap(new GeoPoint(plotSouthLat, 30.0005), plot, 0);

        // Should be approximately 50% (half the spray is inside)
        Assert.InRange(overlap, 40, 60);
    }

    [Fact]
    public void Overlap_ZeroSprayWidth_BinaryResult()
    {
        var boom = new Boom { BoomId = 0, ValveChannel = 0, SprayWidthMeters = 0 };
        var plot = MakePlot(50.0, 30.0, 50.001, 30.001);

        // Inside: 100
        Assert.Equal(100, boom.CalculateOverlap(new GeoPoint(50.0005, 30.0005), plot, 0));

        // Outside: 0
        Assert.Equal(0, boom.CalculateOverlap(new GeoPoint(51.0, 31.0), plot, 0));
    }

    // ════════════════════════════════════════════════════════════════════
    // Activation threshold
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void OverlapThreshold_DefaultIs70_30()
    {
        var boom = new Boom { BoomId = 0, ValveChannel = 0 };

        Assert.Equal(70, boom.ActivationOverlapPercent);
        Assert.Equal(30, boom.DeactivationOverlapPercent);
    }

    [Fact]
    public void BoomToString_IncludesOverlapInfo()
    {
        var boom = new Boom
        {
            BoomId = 0, ValveChannel = 2, YOffsetMeters = -0.5,
            ActivationOverlapPercent = 75, DeactivationOverlapPercent = 25,
        };

        string str = boom.ToString();
        Assert.Contains("75%", str);
        Assert.Contains("25%", str);
    }

    [Fact]
    public void SprayWidth_DefaultIs025m()
    {
        var boom = new Boom { BoomId = 0, ValveChannel = 0 };
        Assert.Equal(0.25, boom.SprayWidthMeters);
    }
}
