using PlotManager.Core.Models;
using PlotManager.Core.Services;
using Xunit;

namespace PlotManager.Tests;

/// <summary>
/// Tests for SensorHub calibration math, JSON processing, and
/// SectionController air pressure interlock integration.
/// </summary>
public class SensorHubTests
{
    // ════════════════════════════════════════════════════════════════════
    // Air Pressure Calibration: Bar = (Voltage - Offset) * Multiplier
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0.5, 0.0)]    // 0.5V = 0 Bar (minimum voltage = 0 pressure)
    [InlineData(1.0, 1.25)]   // 1.0V = 1.25 Bar
    [InlineData(2.5, 5.0)]    // 2.5V = 5.0 Bar (mid-range)
    [InlineData(3.0, 6.25)]   // 3.0V = 6.25 Bar
    [InlineData(4.5, 10.0)]   // 4.5V = 10.0 Bar (maximum)
    public void CalibrateAirPressure_CorrectMath(double voltage, double expectedBar)
    {
        var hub = new SensorHub
        {
            AirPressureVoltageOffset = 0.5,
            AirPressureVoltageMultiplier = 2.5,
        };

        double result = hub.CalibrateAirPressure(voltage);

        Assert.Equal(expectedBar, result, precision: 4);
    }

    [Fact]
    public void CalibrateAirPressure_BelowOffset_ClampsToZero()
    {
        var hub = new SensorHub
        {
            AirPressureVoltageOffset = 0.5,
            AirPressureVoltageMultiplier = 2.5,
        };

        // 0.3V is below 0.5V offset → should clamp to 0, not go negative
        double result = hub.CalibrateAirPressure(0.3);

        Assert.Equal(0.0, result);
    }

    // ════════════════════════════════════════════════════════════════════
    // Flow Rate Calibration: Lpm = (Hz * 60) / PulsesPerLiter
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0.0, 0.0)]       // No pulses = no flow
    [InlineData(400.0, 60.0)]    // 400 Hz @400 PPL = 60 Lpm
    [InlineData(200.0, 30.0)]    // 200 Hz @400 PPL = 30 Lpm
    [InlineData(100.0, 15.0)]    // 100 Hz @400 PPL = 15 Lpm
    [InlineData(6.667, 1.0)]     // ~6.667 Hz @400 PPL ≈ 1.0 Lpm
    public void CalibrateFlowRate_CorrectMath(double hz, double expectedLpm)
    {
        var hub = new SensorHub
        {
            FlowMeterPulsesPerLiter = 400.0,
        };

        double result = hub.CalibrateFlowRate(hz);

        Assert.Equal(expectedLpm, result, precision: 2);
    }

    [Fact]
    public void CalibrateFlowRate_NegativeHz_ReturnsZero()
    {
        var hub = new SensorHub { FlowMeterPulsesPerLiter = 400.0 };

        Assert.Equal(0.0, hub.CalibrateFlowRate(-10.0));
    }

    // ════════════════════════════════════════════════════════════════════
    // JSON Deserialization
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessRawJson_ValidPayload_ReturnsSnapshot()
    {
        var hub = new SensorHub
        {
            AirPressureVoltageOffset = 0.5,
            AirPressureVoltageMultiplier = 2.5,
            FlowMeterPulsesPerLiter = 400.0,
        };

        string json = """{"AirV": 3.0, "FlowHz": [400.0, 200.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0]}""";

        SensorSnapshot? snapshot = hub.ProcessRawJson(json);

        Assert.NotNull(snapshot);
        Assert.Equal(6.25, snapshot.AirPressureBar, precision: 4);
        Assert.Equal(60.0, snapshot.FlowRatesLpm[0], precision: 2);
        Assert.Equal(30.0, snapshot.FlowRatesLpm[1], precision: 2);
        Assert.Equal(0.0, snapshot.FlowRatesLpm[2], precision: 2);
        Assert.False(snapshot.IsStale);
    }

    [Fact]
    public void ProcessRawJson_InvalidJson_ReturnsNull()
    {
        var hub = new SensorHub();

        Assert.Null(hub.ProcessRawJson("not json at all"));
        Assert.Null(hub.ProcessRawJson("{broken:}"));
        Assert.Null(hub.ProcessRawJson(""));
    }

    [Fact]
    public void ProcessRawJson_PartialFlowHz_PadsWithZeros()
    {
        var hub = new SensorHub { FlowMeterPulsesPerLiter = 400.0 };

        // Only 3 flow values instead of 10
        string json = """{"AirV": 1.0, "FlowHz": [100.0, 200.0, 300.0]}""";

        SensorSnapshot? snapshot = hub.ProcessRawJson(json);

        Assert.NotNull(snapshot);
        Assert.Equal(10, snapshot.FlowRatesLpm.Length);
        Assert.Equal(15.0, snapshot.FlowRatesLpm[0], precision: 2);  // 100*(60/400)
        Assert.Equal(30.0, snapshot.FlowRatesLpm[1], precision: 2);  // 200*(60/400)
        Assert.Equal(45.0, snapshot.FlowRatesLpm[2], precision: 2);  // 300*(60/400)
        Assert.Equal(0.0, snapshot.FlowRatesLpm[3]);  // Padded with zero
        Assert.Equal(0.0, snapshot.FlowRatesLpm[9]);  // Padded with zero
    }

    [Fact]
    public void ProcessRawJson_NullFlowHz_AllZeros()
    {
        var hub = new SensorHub();

        string json = """{"AirV": 2.0}""";

        SensorSnapshot? snapshot = hub.ProcessRawJson(json);

        Assert.NotNull(snapshot);
        Assert.All(snapshot.FlowRatesLpm, v => Assert.Equal(0.0, v));
    }

    [Fact]
    public void ProcessRawJson_CaseInsensitive()
    {
        var hub = new SensorHub
        {
            AirPressureVoltageOffset = 0.5,
            AirPressureVoltageMultiplier = 2.5,
        };

        // Lowercase keys
        string json = """{"airv": 2.5, "flowhz": [100.0]}""";

        SensorSnapshot? snapshot = hub.ProcessRawJson(json);

        Assert.NotNull(snapshot);
        Assert.Equal(5.0, snapshot.AirPressureBar, precision: 4);
    }

    // ════════════════════════════════════════════════════════════════════
    // SensorSnapshot Model
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateEmpty_IsStaleWithZeroValues()
    {
        var empty = SensorSnapshot.CreateEmpty();

        Assert.True(empty.IsStale);
        Assert.Equal(0.0, empty.AirPressureBar);
        Assert.Equal(10, empty.FlowRatesLpm.Length);
        Assert.All(empty.FlowRatesLpm, v => Assert.Equal(0.0, v));
    }

    // ════════════════════════════════════════════════════════════════════
    // Air Pressure E-STOP Interlock (via SectionController)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AirPressure_Above_Threshold_NoInterlock()
    {
        var controller = new SectionController
        {
            MinSafeAirPressureBar = 2.0,
            AirPressureTimeoutSeconds = 2.0,
        };

        bool result = controller.CheckAirPressure(3.0, DateTime.UtcNow);

        Assert.True(result);
        Assert.False(controller.AirPressureLostActive);
        Assert.False(controller.AirPressureDegraded);
    }

    [Fact]
    public void AirPressure_Below_Threshold_DegradedImmediately_LostAfterTimeout()
    {
        var controller = new SectionController
        {
            MinSafeAirPressureBar = 2.0,
            AirPressureTimeoutSeconds = 2.0,
        };

        var t0 = new DateTime(2026, 3, 9, 12, 0, 0, DateTimeKind.Utc);

        // First low reading — degraded but not lost
        bool r1 = controller.CheckAirPressure(1.5, t0);
        Assert.True(r1);  // Still OK (within timeout)
        Assert.True(controller.AirPressureDegraded);
        Assert.False(controller.AirPressureLostActive);

        // 1 second later — still within timeout
        bool r2 = controller.CheckAirPressure(1.5, t0.AddSeconds(1));
        Assert.True(r2);
        Assert.True(controller.AirPressureDegraded);
        Assert.False(controller.AirPressureLostActive);

        // 2 seconds later — timeout expired → E-STOP
        bool r3 = controller.CheckAirPressure(1.5, t0.AddSeconds(2));
        Assert.False(r3);
        Assert.True(controller.AirPressureLostActive);
    }

    [Fact]
    public void AirPressure_Recovery_ClearsInterlock()
    {
        var controller = new SectionController
        {
            MinSafeAirPressureBar = 2.0,
            AirPressureTimeoutSeconds = 2.0,
        };

        var t0 = new DateTime(2026, 3, 9, 12, 0, 0, DateTimeKind.Utc);

        // Trigger loss
        controller.CheckAirPressure(1.5, t0);
        controller.CheckAirPressure(1.5, t0.AddSeconds(3));
        Assert.True(controller.AirPressureLostActive);

        // Pressure restored
        controller.CheckAirPressure(3.0, t0.AddSeconds(4));
        Assert.False(controller.AirPressureLostActive);
        Assert.False(controller.AirPressureDegraded);
    }

    [Fact]
    public void AirPressure_FiresEvents()
    {
        var controller = new SectionController
        {
            MinSafeAirPressureBar = 2.0,
            AirPressureTimeoutSeconds = 2.0,
        };

        double? lostPressure = null;
        double? restoredPressure = null;
        controller.OnAirPressureLost += (p) => lostPressure = p;
        controller.OnAirPressureRestored += (p) => restoredPressure = p;

        var t0 = new DateTime(2026, 3, 9, 12, 0, 0, DateTimeKind.Utc);

        // Trigger
        controller.CheckAirPressure(1.0, t0);
        controller.CheckAirPressure(1.0, t0.AddSeconds(3));
        Assert.NotNull(lostPressure);
        Assert.Equal(1.0, lostPressure.Value);

        // Restore
        controller.CheckAirPressure(4.0, t0.AddSeconds(4));
        Assert.NotNull(restoredPressure);
        Assert.Equal(4.0, restoredPressure.Value);
    }

    [Fact]
    public void AirPressure_Interlock_BlocksValveMask()
    {
        var controller = new SectionController
        {
            MinSafeAirPressureBar = 2.0,
            AirPressureTimeoutSeconds = 0.0, // Instant for simplicity
            TargetSpeedKmh = 5.0,
        };

        // Trigger immediately (timeout=0)
        controller.CheckAirPressure(1.0);

        // ApplyInterlocks should return 0 even with valid mask
        ushort result = controller.ApplyInterlocks(0x3FFF, 5.0);
        Assert.Equal(0, result);
    }

    [Fact]
    public void AirPressure_Disabled_WhenMinIsZero()
    {
        var controller = new SectionController
        {
            MinSafeAirPressureBar = 0.0, // Disabled
        };

        bool result = controller.CheckAirPressure(0.0);

        Assert.True(result);
        Assert.False(controller.AirPressureLostActive);
    }

    // ════════════════════════════════════════════════════════════════════
    // MachineProfile Calibration Defaults
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void MachineProfile_DefaultCalibrationValues()
    {
        var profile = new MachineProfile();

        Assert.Equal(0.5, profile.AirPressureVoltageOffset);
        Assert.Equal(2.5, profile.AirPressureVoltageMultiplier);
        Assert.Equal(400.0, profile.FlowMeterPulsesPerLiter);
        Assert.Equal(2.0, profile.MinSafeAirPressureBar);
        Assert.Equal(9999, profile.SensorUdpPort);
    }

    [Fact]
    public void MachineProfile_CalibrationSurvivesJsonRoundTrip()
    {
        var profile = new MachineProfile
        {
            AirPressureVoltageOffset = 0.6,
            AirPressureVoltageMultiplier = 3.0,
            FlowMeterPulsesPerLiter = 500.0,
            MinSafeAirPressureBar = 1.5,
            SensorUdpPort = 8888,
        };

        string json = profile.ToJson();
        var restored = MachineProfile.FromJson(json);

        Assert.Equal(0.6, restored.AirPressureVoltageOffset);
        Assert.Equal(3.0, restored.AirPressureVoltageMultiplier);
        Assert.Equal(500.0, restored.FlowMeterPulsesPerLiter);
        Assert.Equal(1.5, restored.MinSafeAirPressureBar);
        Assert.Equal(8888, restored.SensorUdpPort);
    }

    [Fact]
    public void SensorHub_Configure_SetsFromProfile()
    {
        var profile = new MachineProfile
        {
            AirPressureVoltageOffset = 0.7,
            AirPressureVoltageMultiplier = 3.5,
            FlowMeterPulsesPerLiter = 600.0,
            SensorUdpPort = 7777,
        };

        var hub = new SensorHub();
        hub.Configure(profile);

        Assert.Equal(0.7, hub.AirPressureVoltageOffset);
        Assert.Equal(3.5, hub.AirPressureVoltageMultiplier);
        Assert.Equal(600.0, hub.FlowMeterPulsesPerLiter);
        Assert.Equal(7777, hub.ListenPort);
    }
}
