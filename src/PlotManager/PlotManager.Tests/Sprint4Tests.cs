using PlotManager.Core.Models;
using PlotManager.Core.Services;
using Xunit;

namespace PlotManager.Tests;

/// <summary>
/// Tests for Sprint 4: Hardware Hierarchy, Per-Boom Y-Offsets,
/// RTK Watchdog, Auto-Weather, Grid Nudge, Clean Mode.
/// </summary>
public class Sprint4Tests
{
    // ════════════════════════════════════════════════════════════════════
    // HardwareSetup model
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateDefault10Boom_Creates10Booms()
    {
        HardwareSetup setup = HardwareSetup.CreateDefault10Boom();

        Assert.Equal(10, setup.Booms.Count);
        Assert.Equal(10, setup.EnabledBoomCount);
    }

    [Fact]
    public void CreateDefault10Boom_SequentialChannels()
    {
        HardwareSetup setup = HardwareSetup.CreateDefault10Boom();

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(i, setup.Booms[i].ValveChannel);
        }
    }

    [Fact]
    public void CreateDefault10Boom_IncreasingBackwardOffset()
    {
        HardwareSetup setup = HardwareSetup.CreateDefault10Boom(-0.30, 0.05);

        // Boom 0 = -0.30, Boom 1 = -0.35, ..., Boom 9 = -0.75
        Assert.Equal(-0.30, setup.Booms[0].YOffsetMeters, 3);
        Assert.Equal(-0.35, setup.Booms[1].YOffsetMeters, 3);
        Assert.Equal(-0.75, setup.Booms[9].YOffsetMeters, 3);
    }

    [Fact]
    public void BuildValveMask_CorrectBits()
    {
        HardwareSetup setup = HardwareSetup.CreateDefault10Boom();

        // Activate booms 0 and 2 (channels 0 and 2)
        ushort mask = setup.BuildValveMask(new[] { 0, 2 });

        Assert.Equal((ushort)0b0000_0000_0101, mask); // bits 0 and 2
    }

    [Fact]
    public void BuildValveMask_DisabledBoomIgnored()
    {
        HardwareSetup setup = HardwareSetup.CreateDefault10Boom();
        setup.Booms[2].Enabled = false;

        ushort mask = setup.BuildValveMask(new[] { 0, 2 });

        Assert.Equal((ushort)0b0000_0000_0001, mask); // Only bit 0
    }

    [Fact]
    public void GetBoomsForProduct_MatchesByChannel()
    {
        HardwareSetup setup = HardwareSetup.CreateDefault10Boom();
        var routing = new HardwareRouting
        {
            ProductToSections = new Dictionary<string, List<int>>
            {
                ["Herbicide A"] = new List<int> { 0, 3 },
            },
        };

        var booms = setup.GetBoomsForProduct("Herbicide A", routing);

        Assert.Equal(2, booms.Count);
        Assert.Equal(0, booms[0].BoomId);
        Assert.Equal(3, booms[1].BoomId);
    }

    // ════════════════════════════════════════════════════════════════════
    // Per-Boom Spatial Evaluation
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void EvaluatePerBoom_BoomsGetIndependentMasks()
    {
        var engine = new SpatialEngine();
        var generator = new GridGenerator();
        PlotGrid grid = generator.Generate(new GridGenerator.GridParams
        {
            Rows = 1, Columns = 1,
            PlotWidthMeters = 5.0, PlotLengthMeters = 10.0,
            BufferWidthMeters = 1.0, BufferLengthMeters = 2.0,
            Origin = new GeoPoint(50.0, 30.0), HeadingDegrees = 0,
        });

        var trialMap = new TrialMap
        {
            PlotAssignments = new Dictionary<string, string> { ["R1C1"] = "Product A" },
        };
        var routing = new HardwareRouting
        {
            ProductToSections = new Dictionary<string, List<int>>
            {
                ["Product A"] = new List<int> { 0, 1 },
            },
        };

        engine.Configure(grid, trialMap, routing);

        // Two booms: both mapped to Product A via channels 0 and 1
        var setup = new HardwareSetup
        {
            Booms = new List<Boom>
            {
                new Boom { BoomId = 0, ValveChannel = 0, YOffsetMeters = 0 },
                new Boom { BoomId = 1, ValveChannel = 1, YOffsetMeters = 0 },
            },
        };

        // Position inside the plot
        Plot plot = grid.Plots[0, 0];
        double midLat = (plot.SouthWest.Latitude + plot.NorthEast.Latitude) / 2;
        double midLon = (plot.SouthWest.Longitude + plot.NorthEast.Longitude) / 2;

        SpatialResult result = engine.EvaluatePerBoom(
            new GeoPoint(midLat, midLon), 0, 5, setup);

        // Both booms should be active
        Assert.True((result.ValveMask & 0x01) != 0, "Boom 0 (channel 0) should be active");
        Assert.True((result.ValveMask & 0x02) != 0, "Boom 1 (channel 1) should be active");
    }

    [Fact]
    public void EvaluatePerBoom_YOffset_DelaysRearBoom()
    {
        var engine = new SpatialEngine();
        var generator = new GridGenerator();
        PlotGrid grid = generator.Generate(new GridGenerator.GridParams
        {
            Rows = 1, Columns = 1,
            PlotWidthMeters = 5.0, PlotLengthMeters = 10.0,
            BufferWidthMeters = 1.0, BufferLengthMeters = 2.0,
            Origin = new GeoPoint(50.0, 30.0), HeadingDegrees = 0,
        });

        var trialMap = new TrialMap
        {
            PlotAssignments = new Dictionary<string, string> { ["R1C1"] = "Product A" },
        };
        var routing = new HardwareRouting
        {
            ProductToSections = new Dictionary<string, List<int>>
            {
                ["Product A"] = new List<int> { 0, 1 },
            },
        };

        engine.Configure(grid, trialMap, routing);

        // Boom 0 at antenna position (Y=0), Boom 1 is 2m behind (Y=-2.0)
        var setup = new HardwareSetup
        {
            Booms = new List<Boom>
            {
                new Boom { BoomId = 0, ValveChannel = 0, YOffsetMeters = 0 },
                new Boom { BoomId = 1, ValveChannel = 1, YOffsetMeters = -2.0 },
            },
        };

        // Position the antenna 1m inside the south edge of the plot (heading North)
        // Boom 0 (at antenna) is inside → active
        // Boom 1 (2m behind antenna) is outside the plot → inactive
        Plot plot = grid.Plots[0, 0];
        double insideLat = plot.SouthWest.Latitude + (1.0 / 110540.0);
        double midLon = (plot.SouthWest.Longitude + plot.NorthEast.Longitude) / 2;

        SpatialResult result = engine.EvaluatePerBoom(
            new GeoPoint(insideLat, midLon), 0, 5, setup);

        Assert.True((result.ValveMask & 0x01) != 0, "Front boom (Y=0) should be active");
        Assert.True((result.ValveMask & 0x02) == 0, "Rear boom (Y=-2.0) should still be outside the plot");
    }

    // ════════════════════════════════════════════════════════════════════
    // RTK Watchdog
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void RtkWatchdog_RtkFix_Passes()
    {
        var controller = new SectionController();

        bool result = controller.CheckRtkQuality(GpsFixQuality.RtkFix);

        Assert.True(result);
        Assert.False(controller.RtkLostActive);
    }

    [Fact]
    public void RtkWatchdog_Float_TriggersInterlock()
    {
        var controller = new SectionController { RtkLossTimeoutSeconds = 0 };

        bool result = controller.CheckRtkQuality(GpsFixQuality.RtkFloat);

        Assert.False(result);
        Assert.True(controller.RtkLostActive);
    }

    [Fact]
    public void RtkWatchdog_Autonomous_TriggersInterlock()
    {
        var controller = new SectionController { RtkLossTimeoutSeconds = 0 };

        bool result = controller.CheckRtkQuality(GpsFixQuality.Autonomous);

        Assert.False(result);
        Assert.True(controller.RtkLostActive);
    }

    [Fact]
    public void RtkWatchdog_FiresEvents()
    {
        var controller = new SectionController { RtkLossTimeoutSeconds = 0 };
        GpsFixQuality? lostQuality = null;
        GpsFixQuality? restoredQuality = null;
        controller.OnRtkLost += q => lostQuality = q;
        controller.OnRtkRestored += q => restoredQuality = q;

        controller.CheckRtkQuality(GpsFixQuality.RtkFloat);
        Assert.NotNull(lostQuality);
        Assert.Equal(GpsFixQuality.RtkFloat, lostQuality);

        controller.CheckRtkQuality(GpsFixQuality.RtkFix);
        Assert.NotNull(restoredQuality);
        Assert.Equal(GpsFixQuality.RtkFix, restoredQuality);
    }

    // ════════════════════════════════════════════════════════════════════
    // Grid Nudge
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void GridNudge_ShiftsCoordinates()
    {
        var generator = new GridGenerator();
        PlotGrid grid = generator.Generate(new GridGenerator.GridParams
        {
            Rows = 1, Columns = 1,
            PlotWidthMeters = 5.0, PlotLengthMeters = 10.0,
            BufferWidthMeters = 0, BufferLengthMeters = 0,
            Origin = new GeoPoint(50.0, 30.0), HeadingDegrees = 0,
        });

        double origLat = grid.Plots[0, 0].SouthWest.Latitude;
        double origLon = grid.Plots[0, 0].SouthWest.Longitude;

        grid.NudgeMeters(1.0, 0); // 1 meter north

        double newLat = grid.Plots[0, 0].SouthWest.Latitude;

        // 1 meter ≈ 1/110540 degrees latitude
        double expectedShift = 1.0 / 110540.0;
        Assert.InRange(newLat - origLat, expectedShift * 0.99, expectedShift * 1.01);
    }

    [Fact]
    public void GridNudge_1cm_Precision()
    {
        var generator = new GridGenerator();
        PlotGrid grid = generator.Generate(new GridGenerator.GridParams
        {
            Rows = 2, Columns = 2,
            PlotWidthMeters = 5.0, PlotLengthMeters = 10.0,
            BufferWidthMeters = 1.0, BufferLengthMeters = 2.0,
            Origin = new GeoPoint(50.0, 30.0), HeadingDegrees = 0,
        });

        // Nudge 1 cm east
        grid.NudgeMeters(0, 0.01);

        // All plots should have shifted
        // Just verify the operation doesn't throw and coordinates changed
        Assert.NotEqual(30.0, grid.Plots[0, 0].SouthWest.Longitude);
    }

    // ════════════════════════════════════════════════════════════════════
    // Auto-Weather State Machine
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AutoWeather_MovingDoesNotTrigger()
    {
        using var service = new AutoWeatherService();
        bool triggered = false;
        service.OnWeatherFetchRequired += () => triggered = true;

        service.UpdateSpeed(5.0);
        service.UpdateSpeed(5.0);

        Assert.False(triggered);
        Assert.False(service.IsStationary);
    }

    [Fact]
    public void AutoWeather_StoppedStartsTimer()
    {
        using var service = new AutoWeatherService();

        service.UpdateSpeed(0);

        Assert.True(service.IsStationary);
        Assert.True(service.StationaryDurationMs >= 0);
    }

    [Fact]
    public void AutoWeather_MovingResetsTimer()
    {
        using var service = new AutoWeatherService();

        service.UpdateSpeed(0);
        Assert.True(service.IsStationary);

        service.UpdateSpeed(5.0);
        Assert.False(service.IsStationary);
    }

    // ════════════════════════════════════════════════════════════════════
    // NMEA Parsing
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseWimwv_ValidSentence()
    {
        string sentence = "$WIMWV,270.0,R,8.5,M,A*20";

        var result = AutoWeatherService.ParseWimwv(sentence);

        Assert.NotNull(result);
        Assert.Equal(270.0, result.Value.AngleDeg);
        Assert.Equal(8.5, result.Value.SpeedMs);
    }

    [Fact]
    public void ParseWimwv_KnotsConversion()
    {
        string sentence = "$WIMWV,180.0,R,10.0,N,A*XX";

        var result = AutoWeatherService.ParseWimwv(sentence);

        Assert.NotNull(result);
        Assert.Equal(180.0, result.Value.AngleDeg);
        Assert.InRange(result.Value.SpeedMs, 5.14, 5.15); // 10 knots ≈ 5.144 m/s
    }

    [Fact]
    public void ParseWimda_ValidSentence()
    {
        // $WIMDA,b1,b2,b3,b4,temp,C,...,humidity,...
        string sentence = "$WIMDA,29.9737,I,1.0148,B,22.5,C,,,55.0,,,,,,,,,,,*XX";

        var result = AutoWeatherService.ParseWimda(sentence);

        Assert.NotNull(result);
        Assert.Equal(22.5, result.Value.TempC);
        Assert.Equal(55.0, result.Value.Humidity);
    }

    [Fact]
    public void ParseWimwv_InvalidSentence_ReturnsNull()
    {
        Assert.Null(AutoWeatherService.ParseWimwv("$GPGGA,something"));
        Assert.Null(AutoWeatherService.ParseWimwv(""));
    }

    // ════════════════════════════════════════════════════════════════════
    // Clean Controller
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Clean_Stationary_Starts()
    {
        using var clean = new CleanController();

        bool result = clean.StartClean(new[] { 0, 1 }, currentSpeedKmh: 0, plotModeActive: false);

        Assert.True(result);
        Assert.True(clean.IsCleaning);
    }

    [Fact]
    public void Clean_WhileMoving_Fails()
    {
        using var clean = new CleanController();
        string? error = null;
        clean.OnCleanError += msg => error = msg;

        bool result = clean.StartClean(new[] { 0 }, currentSpeedKmh: 2.0, plotModeActive: false);

        Assert.False(result);
        Assert.False(clean.IsCleaning);
        Assert.NotNull(error);
    }

    [Fact]
    public void Clean_WhilePlotMode_Fails()
    {
        using var clean = new CleanController();

        bool result = clean.StartClean(new[] { 0 }, currentSpeedKmh: 0, plotModeActive: true);

        Assert.False(result);
    }

    [Fact]
    public void Clean_EmptyChannels_Fails()
    {
        using var clean = new CleanController();

        bool result = clean.StartClean(Array.Empty<int>(), currentSpeedKmh: 0, plotModeActive: false);

        Assert.False(result);
    }

    [Fact]
    public void Clean_StopWorks()
    {
        using var clean = new CleanController();
        clean.StartClean(new[] { 0 }, 0, false);
        Assert.True(clean.IsCleaning);

        clean.StopClean();
        Assert.False(clean.IsCleaning);
    }
}
