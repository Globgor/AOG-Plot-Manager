using PlotManager.Core.Models;
using PlotManager.Core.Protocol;
using PlotManager.Core.Services;
using Xunit;

namespace PlotManager.Tests;

/// <summary>
/// Tests for the observability, error recovery, data completeness, security,
/// and stress hardening audit fixes (Passes 1–5).
/// </summary>
public class ObservabilityTests
{
    // ════════════════════════════════════════════════════════════════════
    // L1: PlotLogger (structured logging)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void PlotLogger_WritesToFile_CorrectFormat()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"plotlogger_test_{Guid.NewGuid():N}");

        try
        {
            var logger = new PlotLogger { FlushIntervalMs = 100 };
            logger.StartSession(dir, "test_session");

            logger.Info("TestSrc", "Hello info");
            logger.Warn("TestSrc", "Hello warn");
            logger.Error("TestSrc", "Hello error", new InvalidOperationException("boom"));

            Thread.Sleep(300); // Wait for flush
            logger.StopSession();

            Assert.NotNull(logger.FilePath);
            Assert.True(File.Exists(logger.FilePath));

            string content = ReadFileSafe(logger.FilePath);
            Assert.Contains("[INFO] [TestSrc] Hello info", content);
            Assert.Contains("[WARN] [TestSrc] Hello warn", content);
            Assert.Contains("[ERROR] [TestSrc] Hello error | InvalidOperationException: boom", content);
            Assert.Equal(3, logger.EntryCount);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void PlotLogger_ThrowsIfDoubleStart()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"plotlogger_test_{Guid.NewGuid():N}");

        try
        {
            var logger = new PlotLogger();
            logger.StartSession(dir, "s1");
            Assert.Throws<InvalidOperationException>(() => logger.StartSession(dir, "s2"));
            logger.StopSession();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // D1: CSV header includes new GEP columns
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AsAppliedLogger_CsvHeader_ContainsGepColumns()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"asl_test_{Guid.NewGuid():N}");

        try
        {
            var logger = new AsAppliedLogger();
            logger.StartSession(dir, "test");
            logger.StopSession();

            string content = ReadFileSafe(logger.FilePath!);
            string header = content.Split('\n')[0];

            Assert.Contains("FixQuality", header);
            Assert.Contains("HeadingDeg", header);
            Assert.Contains("OffReason", header);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void AsAppliedLogger_LogRecord_WritesGepColumns()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"asl_test_{Guid.NewGuid():N}");

        try
        {
            var logger = new AsAppliedLogger { FlushIntervalMs = 50 };
            logger.StartSession(dir, "test");

            logger.LogRecord(
                DateTime.UtcNow, 50.0, 30.0, "R1C1", "ProductA", 5.0, 0x000F,
                fixQuality: "RTK_FIX", headingDeg: 180.5, offReason: "SPEED");

            Thread.Sleep(200);
            logger.StopSession();

            string content = ReadFileSafe(logger.FilePath!);
            Assert.Contains("RTK_FIX", content);
            Assert.Contains("180.5", content);
            Assert.Contains("SPEED", content);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // S4: SHA256 hash footer
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AsAppliedLogger_StopSession_WritesSha256Footer()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"asl_test_{Guid.NewGuid():N}");

        try
        {
            var logger = new AsAppliedLogger { FlushIntervalMs = 50 };
            logger.StartSession(dir, "test");

            logger.LogRecord(DateTime.UtcNow, 50.0, 30.0, "R1C1", "P", 5.0, 0x01);
            Thread.Sleep(200);
            logger.StopSession();

            string content = ReadFileSafe(logger.FilePath!);
            string lastLine = content.TrimEnd().Split('\n').Last();

            Assert.StartsWith("# SHA256: ", lastLine);
            string hash = lastLine.Replace("# SHA256: ", "");
            Assert.Equal(64, hash.Length); // SHA256 hex = 64 chars
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // R1: AsAppliedLogger DrainQueue fires OnFlushError
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AsAppliedLogger_OnFlushError_EventExists()
    {
        // Verify the event exists and is subscribable
        var logger = new AsAppliedLogger();
        string? errorMessage = null;
        logger.OnFlushError += (msg) => errorMessage = msg;
        // Event exists — no exception thrown
        logger.Dispose();
    }

    // ════════════════════════════════════════════════════════════════════
    // R3: SensorHub NaN/Infinity guard
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void SensorHub_ProcessRawTelemetry_NaN_ReturnsSafeDefaults()
    {
        var hub = new SensorHub
        {
            AirPressureVoltageOffset = 0.5,
            AirPressureVoltageMultiplier = 2.5,
            FlowMeterPulsesPerLiter = 400.0,
        };

        var raw = new RawTelemetry
        {
            AirV = double.NaN,
            FlowHz = new[] { double.PositiveInfinity, double.NegativeInfinity, double.NaN, 100.0 },
        };

        var snap = hub.ProcessRawTelemetry(raw);

        // NaN → 0 → CalibrateAirPressure(0) with offset 0.5 → clamps to 0
        Assert.Equal(0.0, snap.AirPressureBar);
        // Infinity → 0 → CalibrateFlowRate(0) = 0
        Assert.Equal(0.0, snap.FlowRatesLpm[0]);
        Assert.Equal(0.0, snap.FlowRatesLpm[1]);
        Assert.Equal(0.0, snap.FlowRatesLpm[2]);
        // Valid value should calibrate normally: (100 * 60) / 400 = 15.0
        Assert.Equal(15.0, snap.FlowRatesLpm[3], precision: 2);
    }

    // ════════════════════════════════════════════════════════════════════
    // R5: SensorHub rejects FlowMeterPulsesPerLiter <= 0
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void SensorHub_Configure_RejectsZeroPulsesPerLiter()
    {
        var hub = new SensorHub();
        var profile = new MachineProfile
        {
            FlowMeterPulsesPerLiter = 0.0,
            Booms = { new BoomProfile { BoomId = 0, Name = "B1", ValveChannel = 0 } },
        };

        Assert.Throws<ArgumentException>(() => hub.Configure(profile));
    }

    // ════════════════════════════════════════════════════════════════════
    // S1: MachineProfile.Validate()
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void MachineProfile_Validate_RejectsNegativeDelays()
    {
        var profile = MachineProfile.CreateDefault();
        profile.SystemActivationDelayMs = -100;

        Assert.Throws<InvalidOperationException>(() => profile.Validate());
    }

    [Fact]
    public void MachineProfile_Validate_RejectsZeroBooms()
    {
        var profile = new MachineProfile(); // No booms

        Assert.Throws<InvalidOperationException>(() => profile.Validate());
    }

    [Fact]
    public void MachineProfile_Validate_RejectsDuplicateChannels()
    {
        var profile = MachineProfile.CreateDefault();
        // Duplicate channel 0
        profile.Booms[1].ValveChannel = 0;

        Assert.Throws<InvalidOperationException>(() => profile.Validate());
    }

    [Fact]
    public void MachineProfile_Validate_RejectsZeroPulsesPerLiter()
    {
        var profile = MachineProfile.CreateDefault();
        profile.FlowMeterPulsesPerLiter = 0;

        Assert.Throws<InvalidOperationException>(() => profile.Validate());
    }

    [Fact]
    public void MachineProfile_Validate_DefaultProfileIsValid()
    {
        var profile = MachineProfile.CreateDefault();

        // Should not throw
        profile.Validate();
    }

    // ════════════════════════════════════════════════════════════════════
    // T3: PlotModeController blocks SetHardwareSetup while enabled
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void PlotModeController_SetHardwareSetup_ThrowsWhileEnabled()
    {
        var spatialEngine = new SpatialEngine();
        var sectionController = new SectionController();
        var aogClient = new AogUdpClient();
        var controller = new PlotModeController(spatialEngine, sectionController, aogClient);

        var setup = new HardwareSetup();
        setup.Booms.Add(new Boom
        {
            BoomId = 0, Name = "B1", ValveChannel = 0,
            Nozzles = new List<Nozzle> { new Nozzle { NozzleId = 0, XOffsetMeters = 0 } },
        });

        controller.SetHardwareSetup(setup); // OK before enabling

        // Enable — now SetHardwareSetup should throw
        controller.PlotModeEnabled = true;

        Assert.Throws<InvalidOperationException>(() => controller.SetHardwareSetup(setup));
    }

    // ════════════════════════════════════════════════════════════════════
    // T6: GridGenerator rejects polar origin
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void GridGenerator_Rejects_PolarOrigin()
    {
        var gen = new GridGenerator();
        var p = new GridGenerator.GridParams
        {
            Rows = 2,
            Columns = 2,
            PlotWidthMeters = 2,
            PlotLengthMeters = 5,
            Origin = new GeoPoint(89.0, 30.0), // Too close to pole
        };

        Assert.Throws<ArgumentException>(() => gen.Generate(p));
    }

    [Fact]
    public void GridGenerator_Accepts_NormalLatitude()
    {
        var gen = new GridGenerator();
        var p = new GridGenerator.GridParams
        {
            Rows = 2,
            Columns = 2,
            PlotWidthMeters = 2,
            PlotLengthMeters = 5,
            Origin = new GeoPoint(50.0, 30.0),
        };

        // Should not throw
        var grid = gen.Generate(p);
        Assert.Equal(2, grid.Rows);
    }

    // ════════════════════════════════════════════════════════════════════
    // ServiceHealth enum
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void SensorHub_DefaultHealth_IsHealthy()
    {
        var hub = new SensorHub();
        Assert.Equal(ServiceHealth.Healthy, hub.Health);
        Assert.Equal(0, hub.ErrorCount);
    }

    // ════════════════════════════════════════════════════════════════════
    // D2: TrialLogger interlock logging
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void TrialLogger_LogRtkInterlock_WritesToCsv()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"trial_test_{Guid.NewGuid():N}");

        try
        {
            var logger = new TrialLogger();
            logger.StartSession(dir, "test", new WeatherSnapshot { TemperatureC = 20, HumidityPercent = 50, WindSpeedMs = 2 });

            logger.LogRtkInterlock("FLOAT", isLost: false);
            logger.LogRtkInterlock("NO_FIX", isLost: true);

            Thread.Sleep(200);
            logger.StopSession();

            // Read the CSV file
            string csvPath = Directory.GetFiles(dir, "*.csv").First();
            string content = ReadFileSafe(csvPath);

            Assert.Contains("RTK_DEGRADED", content);
            Assert.Contains("RTK_LOST", content);
            Assert.Contains("FLOAT", content);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TrialLogger_LogAirPressureInterlock_WritesToCsv()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"trial_test_{Guid.NewGuid():N}");

        try
        {
            var logger = new TrialLogger();
            logger.StartSession(dir, "test", new WeatherSnapshot { TemperatureC = 20, HumidityPercent = 50, WindSpeedMs = 2 });

            logger.LogAirPressureInterlock(1.5, 2.0, isLost: true);

            Thread.Sleep(200);
            logger.StopSession();

            string csvPath = Directory.GetFiles(dir, "*.csv").First();
            string content = ReadFileSafe(csvPath);

            Assert.Contains("AIR_PRESSURE_LOST", content);
            Assert.Contains("1.50", content);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // D3: Session summary
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void TrialLogger_StopSession_WritesSessionSummary()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"trial_test_{Guid.NewGuid():N}");

        try
        {
            var logger = new TrialLogger();
            logger.StartSession(dir, "test", new WeatherSnapshot { TemperatureC = 20, HumidityPercent = 50, WindSpeedMs = 2 });

            // Log one interlock to increment counter
            logger.LogSpeedInterlock(2.0, 5.0, 20);

            Thread.Sleep(200);
            logger.StopSession();

            string csvPath = Directory.GetFiles(dir, "*.csv").First();
            string content = ReadFileSafe(csvPath);

            Assert.Contains("SESSION_SUMMARY", content);
            Assert.Contains("InterlockEvents=1", content);
            Assert.Contains("SESSION_END", content);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // R2: SensorHub bind retry
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void SensorHub_HasBindRetryProperties()
    {
        var hub = new SensorHub();

        Assert.Equal(3, hub.MaxBindRetries);
        Assert.Equal(500, hub.BindRetryDelayMs);
    }

    // ════════════════════════════════════════════════════════════════════
    // R6: AogUdpClient bind retry
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AogUdpClient_HasBindRetryProperties()
    {
        var client = new AogUdpClient();

        Assert.Equal(3, client.MaxBindRetries);
        Assert.Equal(500, client.BindRetryDelayMs);
    }

    [Fact]
    public void AogUdpClient_DefaultHealth_IsHealthy()
    {
        var client = new AogUdpClient();
        Assert.Equal(ServiceHealth.Healthy, client.Health);
        Assert.Equal(0, client.ErrorCount);
    }

    // ════════════════════════════════════════════════════════════════════
    // D6: TrialLogger LogAllStates
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void TrialLogger_LogAllStates_DefaultIsFalse()
    {
        var logger = new TrialLogger();
        Assert.False(logger.LogAllStates);
    }

    [Fact]
    public void TrialLogger_LogAllStates_CanBeEnabled()
    {
        var logger = new TrialLogger { LogAllStates = true };
        Assert.True(logger.LogAllStates);
    }

    /// <summary>Reads file while writer may still hold it open.</summary>
    private static string ReadFileSafe(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }
}
