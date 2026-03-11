using PlotManager.Core.Models;
using PlotManager.Core.Services;
using Xunit;

namespace PlotManager.Tests;

/// <summary>
/// Tests for Sprint 3: Speed Interlock, Prime Controller, Weather validation, and Trial Logger.
/// </summary>
public class SafetyInterlockTests
{
    // ════════════════════════════════════════════════════════════════════
    // Speed Interlock
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void SpeedInterlock_WithinTolerance_PassesMask()
    {
        var controller = new SectionController
        {
            TargetSpeedKmh = 5.0,
            SpeedToleranceFraction = 0.10, // ±10% → 4.5–5.5 km/h
        };

        ushort result = controller.ApplyInterlocks(0x000F, currentSpeedKmh: 5.0);

        Assert.Equal((ushort)0x000F, result);
        Assert.False(controller.SpeedInterlockActive);
    }

    [Fact]
    public void SpeedInterlock_TooSlow_ForcesAllOff()
    {
        var controller = new SectionController
        {
            TargetSpeedKmh = 5.0,
            SpeedToleranceFraction = 0.10,
        };

        ushort result = controller.ApplyInterlocks(0x000F, currentSpeedKmh: 4.0);

        Assert.Equal((ushort)0, result);
        Assert.True(controller.SpeedInterlockActive);
    }

    [Fact]
    public void SpeedInterlock_TooFast_ForcesAllOff()
    {
        var controller = new SectionController
        {
            TargetSpeedKmh = 5.0,
            SpeedToleranceFraction = 0.10,
        };

        ushort result = controller.ApplyInterlocks(0x000F, currentSpeedKmh: 6.0);

        Assert.Equal((ushort)0, result);
        Assert.True(controller.SpeedInterlockActive);
    }

    [Fact]
    public void SpeedInterlock_AtMinBoundary_PassesMask()
    {
        var controller = new SectionController
        {
            TargetSpeedKmh = 5.0,
            SpeedToleranceFraction = 0.10, // Min = 4.5
        };

        ushort result = controller.ApplyInterlocks(0x000F, currentSpeedKmh: 4.5);

        Assert.Equal((ushort)0x000F, result);
        Assert.False(controller.SpeedInterlockActive);
    }

    [Fact]
    public void SpeedInterlock_AtMaxBoundary_PassesMask()
    {
        var controller = new SectionController
        {
            TargetSpeedKmh = 5.0,
            SpeedToleranceFraction = 0.10, // Max = 5.5
        };

        ushort result = controller.ApplyInterlocks(0x000F, currentSpeedKmh: 5.5);

        Assert.Equal((ushort)0x000F, result);
        Assert.False(controller.SpeedInterlockActive);
    }

    [Fact]
    public void SpeedInterlock_FiresEvent_OnStateChange()
    {
        var controller = new SectionController
        {
            TargetSpeedKmh = 5.0,
            SpeedToleranceFraction = 0.10,
        };

        bool eventFired = false;
        bool reportedState = false;
        controller.OnSpeedInterlockChanged += (active, _) =>
        {
            eventFired = true;
            reportedState = active;
        };

        // First call at good speed — no event (was already false)
        controller.ApplyInterlocks(0x000F, 5.0);
        Assert.False(eventFired);

        // Speed drops out of range — event fires
        controller.ApplyInterlocks(0x000F, 3.0);
        Assert.True(eventFired);
        Assert.True(reportedState);
    }

    [Fact]
    public void EmergencyStop_OverridesEverything()
    {
        var controller = new SectionController();
        controller.ActivateEmergencyStop();

        ushort result = controller.ApplyInterlocks(0x3FFF, currentSpeedKmh: 5.0);

        Assert.Equal((ushort)0, result);
        Assert.True(controller.EmergencyStopActive);
    }

    [Fact]
    public void EmergencyStop_ClearRestores()
    {
        var controller = new SectionController
        {
            TargetSpeedKmh = 5.0,
            SpeedToleranceFraction = 0.10,
        };
        controller.ActivateEmergencyStop();
        controller.ClearEmergencyStop();

        ushort result = controller.ApplyInterlocks(0x000F, currentSpeedKmh: 5.0);

        Assert.Equal((ushort)0x000F, result);
        Assert.False(controller.EmergencyStopActive);
    }

    [Fact]
    public void IsSpeedAcceptable_HelperWorks()
    {
        var controller = new SectionController
        {
            TargetSpeedKmh = 5.0,
            SpeedToleranceFraction = 0.10,
        };

        Assert.True(controller.IsSpeedAcceptable(5.0));
        Assert.True(controller.IsSpeedAcceptable(4.5));
        Assert.True(controller.IsSpeedAcceptable(5.5));
        Assert.False(controller.IsSpeedAcceptable(4.4));
        Assert.False(controller.IsSpeedAcceptable(5.6));
    }

    // ════════════════════════════════════════════════════════════════════
    // Prime Controller
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Prime_Stationary_Succeeds()
    {
        var prime = new PrimeController();

        bool result = prime.StartPrime(currentSpeedKmh: 0, plotModeActive: false);

        Assert.True(result);
        Assert.True(prime.IsPriming);
    }

    [Fact]
    public void Prime_WhileMoving_Fails()
    {
        var prime = new PrimeController();

        string? errorMessage = null;
        prime.OnPrimeError += msg => errorMessage = msg;

        bool result = prime.StartPrime(currentSpeedKmh: 2.0, plotModeActive: false);

        Assert.False(result);
        Assert.False(prime.IsPriming);
        Assert.NotNull(errorMessage);
        Assert.Contains("Stop the tractor", errorMessage);
    }

    [Fact]
    public void Prime_WhilePlotModeActive_Fails()
    {
        var prime = new PrimeController();

        string? errorMessage = null;
        prime.OnPrimeError += msg => errorMessage = msg;

        bool result = prime.StartPrime(currentSpeedKmh: 0, plotModeActive: true);

        Assert.False(result);
        Assert.NotNull(errorMessage);
        Assert.Contains("Plot Mode", errorMessage);
    }

    [Fact]
    public void Prime_StopReleases()
    {
        var prime = new PrimeController();
        prime.StartPrime(0, false);
        Assert.True(prime.IsPriming);

        prime.StopPrime();
        Assert.False(prime.IsPriming);
    }

    [Fact]
    public void Prime_ForceStop_AlwaysWorks()
    {
        var prime = new PrimeController();
        prime.StartPrime(0, false);

        prime.ForceStop();
        Assert.False(prime.IsPriming);
    }

    // ════════════════════════════════════════════════════════════════════
    // Weather Snapshot Validation
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Weather_NormalConditions_NoWarnings()
    {
        var weather = new WeatherSnapshot
        {
            TemperatureC = 20,
            HumidityPercent = 60,
            WindSpeedMs = 2.0,
        };

        Assert.Empty(weather.Validate());
    }

    [Fact]
    public void Weather_LowTemp_WarnsAboutDriftRisk()
    {
        var weather = new WeatherSnapshot
        {
            TemperatureC = 3,
            HumidityPercent = 60,
            WindSpeedMs = 2.0,
        };

        List<string> warnings = weather.Validate();
        Assert.Single(warnings);
        Assert.Contains("Temperature too low", warnings[0]);
    }

    [Fact]
    public void Weather_HighWind_WarnsAboutDrift()
    {
        var weather = new WeatherSnapshot
        {
            TemperatureC = 20,
            HumidityPercent = 60,
            WindSpeedMs = 5.0,
        };

        List<string> warnings = weather.Validate();
        Assert.Single(warnings);
        Assert.Contains("Wind speed high", warnings[0]);
    }

    [Fact]
    public void Weather_MultipleIssues_MultipleWarnings()
    {
        var weather = new WeatherSnapshot
        {
            TemperatureC = 40,
            HumidityPercent = 30,
            WindSpeedMs = 6.0,
        };

        List<string> warnings = weather.Validate();
        Assert.Equal(3, warnings.Count);
    }

    // ════════════════════════════════════════════════════════════════════
    // Trial Logger (basic lifecycle tests)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void TrialLogger_StartsAndStopsCleanly()
    {
        using var logger = new TrialLogger();
        string tempDir = Path.Combine(Path.GetTempPath(), $"plotmanager_test_{Guid.NewGuid():N}");

        try
        {
            var weather = new WeatherSnapshot
            {
                TemperatureC = 20, HumidityPercent = 60, WindSpeedMs = 1.0,
            };

            logger.StartSession(tempDir, "TestTrial", weather);
            Assert.True(logger.IsActive);
            Assert.NotNull(logger.FilePath);
            Assert.True(File.Exists(logger.FilePath));

            logger.StopSession();
            Assert.False(logger.IsActive);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void TrialLogger_WritesWeatherHeader()
    {
        using var logger = new TrialLogger();
        string tempDir = Path.Combine(Path.GetTempPath(), $"plotmanager_test_{Guid.NewGuid():N}");

        try
        {
            var weather = new WeatherSnapshot
            {
                TemperatureC = 22.5, HumidityPercent = 55, WindSpeedMs = 1.5,
                WindDirection = "NW",
            };

            logger.StartSession(tempDir, "WeatherTest", weather);
            logger.StopSession();

            string content = ReadFileSafe(logger.FilePath!);
            Assert.Contains("METEO", content);
            Assert.Contains("22.5", content);
            Assert.Contains("NW", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void TrialLogger_DoubleStart_Throws()
    {
        using var logger = new TrialLogger();
        string tempDir = Path.Combine(Path.GetTempPath(), $"plotmanager_test_{Guid.NewGuid():N}");

        try
        {
            var weather = new WeatherSnapshot
            {
                TemperatureC = 20, HumidityPercent = 60, WindSpeedMs = 1.0,
            };

            logger.StartSession(tempDir, "Test1", weather);
            Assert.Throws<InvalidOperationException>(() =>
                logger.StartSession(tempDir, "Test2", weather));

            logger.StopSession();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    /// <summary>Reads file while writer may still hold it open.</summary>
    private static string ReadFileSafe(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }
}
