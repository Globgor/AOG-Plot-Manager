using PlotManager.Core.Models;
using PlotManager.Core.Services;
using Xunit;

namespace PlotManager.Tests;

/// <summary>
/// Tests for MachineProfile: JSON round-trip with all new fields,
/// per-boom delay overrides, apply to engine/controller, conversion.
/// </summary>
public class MachineProfileTests
{
    [Fact]
    public void CreateDefault_Has10Booms()
    {
        MachineProfile profile = MachineProfile.CreateDefault();

        Assert.Equal(10, profile.Booms.Count);
        Assert.Equal("Стандартний профіль", profile.ProfileName);
        Assert.Equal(FluidType.Water, profile.FluidType);
    }

    [Fact]
    public void JsonRoundTrip_PreservesAllFields()
    {
        var profile = new MachineProfile
        {
            ProfileName = "Adjuvant Profile",
            Notes = "Calibrated with oil adjuvant",
            FluidType = FluidType.OilAdjuvant,
            AntennaHeightMeters = 2.8,
            SystemActivationDelayMs = 400,
            SystemDeactivationDelayMs = 200,
            PreActivationMeters = 0.6,
            PreDeactivationMeters = 0.3,
            OperatingPressureBar = 4.0,
            CogHeadingThresholdDegrees = 5.0,
            RtkLossTimeoutSeconds = 3.0,
            GpsUpdateRateHz = 5,
            TargetSpeedKmh = 6.0,
            SpeedToleranceKmh = 1.5,
            TargetRateLPerHa = 250,
            Nozzle = new NozzleSpec
            {
                Model = "TeeJet XR 110-03",
                SprayAngleDegrees = 110,
                FlowRateLPerMin = 1.18,
                ColorCode = "Blue",
            },
            Connection = new ConnectionSettings
            {
                TeensyComPort = "COM5",
                TeensyBaudRate = 230400,
                AogHost = "192.168.1.100",
                AogUdpListenPort = 8888,
                AogUdpSendPort = 8889,
                WeatherComPort = "COM7",
            },
            Booms = new List<BoomProfile>
            {
                new BoomProfile
                {
                    BoomId = 0, Name = "Front", ValveChannel = 0,
                    YOffsetMeters = -0.25, SprayWidthMeters = 0.30,
                    ActivationOverlapPercent = 80, DeactivationOverlapPercent = 20,
                    ActivationDelayOverrideMs = 350, DeactivationDelayOverrideMs = 180,
                    HoseLengthMeters = 1.2, Enabled = true,
                },
                new BoomProfile
                {
                    BoomId = 1, Name = "Rear", ValveChannel = 1,
                    YOffsetMeters = -0.50,
                    ActivationDelayOverrideMs = -1, // Use global
                    Enabled = false,
                },
            },
        };

        string json = profile.ToJson();
        MachineProfile r = MachineProfile.FromJson(json);

        // Identity
        Assert.Equal("Adjuvant Profile", r.ProfileName);
        Assert.Equal(FluidType.OilAdjuvant, r.FluidType);
        Assert.Equal(2.8, r.AntennaHeightMeters);

        // Hydraulics
        Assert.Equal(400, r.SystemActivationDelayMs);
        Assert.Equal(200, r.SystemDeactivationDelayMs);
        Assert.Equal(4.0, r.OperatingPressureBar);

        // Spatial
        Assert.Equal(0.6, r.PreActivationMeters);
        Assert.Equal(5.0, r.CogHeadingThresholdDegrees);
        Assert.Equal(3.0, r.RtkLossTimeoutSeconds);
        Assert.Equal(5, r.GpsUpdateRateHz);

        // Speed
        Assert.Equal(6.0, r.TargetSpeedKmh);
        Assert.Equal(250, r.TargetRateLPerHa);

        // Nozzle
        Assert.Equal("TeeJet XR 110-03", r.Nozzle.Model);
        Assert.Equal(110, r.Nozzle.SprayAngleDegrees);
        Assert.Equal("Blue", r.Nozzle.ColorCode);

        // Connections
        Assert.Equal("COM5", r.Connection.TeensyComPort);
        Assert.Equal(230400, r.Connection.TeensyBaudRate);
        Assert.Equal("192.168.1.100", r.Connection.AogHost);
        Assert.Equal("COM7", r.Connection.WeatherComPort);

        // Booms
        Assert.Equal(2, r.Booms.Count);
        Assert.Equal(350, r.Booms[0].ActivationDelayOverrideMs);
        Assert.Equal(1.2, r.Booms[0].HoseLengthMeters);
        Assert.False(r.Booms[1].Enabled);
        Assert.Equal(-1, r.Booms[1].ActivationDelayOverrideMs);
    }

    [Fact]
    public void FluidType_SerializedAsString()
    {
        var profile = new MachineProfile { FluidType = FluidType.OilAdjuvant };
        string json = profile.ToJson();

        Assert.Contains("oilAdjuvant", json);
    }

    [Fact]
    public void SaveAndLoad_FileRoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"test_profile_{Guid.NewGuid()}.json");
        try
        {
            MachineProfile profile = MachineProfile.CreateDefault();
            profile.ProfileName = "File Test";
            profile.SystemActivationDelayMs = 400;
            profile.CogHeadingThresholdDegrees = 4.5;

            profile.SaveToFile(path);
            MachineProfile loaded = MachineProfile.LoadFromFile(path);

            Assert.Equal("File Test", loaded.ProfileName);
            Assert.Equal(400, loaded.SystemActivationDelayMs);
            Assert.Equal(4.5, loaded.CogHeadingThresholdDegrees);
            Assert.Equal(10, loaded.Booms.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PerBoomDelay_OverrideUsedWhenSet()
    {
        var bp = new BoomProfile
        {
            ActivationDelayOverrideMs = 450,
            DeactivationDelayOverrideMs = 220,
        };

        Assert.Equal(450, bp.GetEffectiveActivationDelay(300));
        Assert.Equal(220, bp.GetEffectiveDeactivationDelay(150));
    }

    [Fact]
    public void PerBoomDelay_FallsBackToGlobalWhenMinus1()
    {
        var bp = new BoomProfile
        {
            ActivationDelayOverrideMs = -1,
            DeactivationDelayOverrideMs = -1,
        };

        Assert.Equal(300, bp.GetEffectiveActivationDelay(300));
        Assert.Equal(150, bp.GetEffectiveDeactivationDelay(150));
    }

    [Fact]
    public void ToHardwareSetup_ConvertsAllBooms()
    {
        MachineProfile profile = MachineProfile.CreateDefault();
        profile.Booms[2].SprayWidthMeters = 0.40;
        profile.Booms[2].ActivationOverlapPercent = 80;
        profile.Booms[5].Enabled = false;

        HardwareSetup setup = profile.ToHardwareSetup();

        Assert.Equal(10, setup.Booms.Count);
        Assert.Equal(0.40, setup.Booms[2].SprayWidthMeters);
        Assert.Equal(80, setup.Booms[2].ActivationOverlapPercent);
        Assert.False(setup.Booms[5].Enabled);
        Assert.Equal(9, setup.EnabledBoomCount);
    }

    [Fact]
    public void ApplyToSpatialEngine_SetsAllParams()
    {
        var engine = new SpatialEngine();
        var profile = new MachineProfile
        {
            PreActivationMeters = 0.75,
            PreDeactivationMeters = 0.35,
            SystemActivationDelayMs = 400,
            SystemDeactivationDelayMs = 200,
        };

        profile.ApplyToSpatialEngine(engine);

        Assert.Equal(0.75, engine.PreActivationMeters);
        Assert.Equal(0.35, engine.PreDeactivationMeters);
        Assert.Equal(400, engine.SystemActivationDelayMs);
        Assert.Equal(200, engine.SystemDeactivationDelayMs);
    }

    [Fact]
    public void ApplyToSectionController_SetsSpeedLimits()
    {
        var controller = new SectionController();
        var profile = new MachineProfile
        {
            TargetSpeedKmh = 6.0,
            SpeedToleranceKmh = 1.2,
        };

        profile.ApplyToSectionController(controller);

        Assert.Equal(6.0, controller.TargetSpeedKmh);
        Assert.Equal(0.2, controller.SpeedToleranceFraction, 3);
        Assert.Equal(4.8, controller.MinSpeedKmh, 1);
        Assert.Equal(7.2, controller.MaxSpeedKmh, 1);
    }

    [Fact]
    public void DefaultBoomOffsets_AreSequential()
    {
        MachineProfile profile = MachineProfile.CreateDefault();

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(i, profile.Booms[i].BoomId);
            Assert.Equal(i, profile.Booms[i].ValveChannel);
            double expected = -0.30 - (i * 0.05);
            Assert.Equal(expected, profile.Booms[i].YOffsetMeters, 3);
            Assert.Equal(-1, profile.Booms[i].ActivationDelayOverrideMs);
        }
    }

    [Fact]
    public void DefaultProfile_HasReasonableDefaults()
    {
        var profile = new MachineProfile();

        Assert.Equal(3.0, profile.CogHeadingThresholdDegrees);
        Assert.Equal(2.0, profile.RtkLossTimeoutSeconds);
        Assert.Equal(10, profile.GpsUpdateRateHz);
        Assert.Equal(3.0, profile.OperatingPressureBar);
        Assert.Equal(2.5, profile.AntennaHeightMeters);
        Assert.Equal(200, profile.TargetRateLPerHa);
        Assert.Equal(string.Empty, profile.Connection.TeensyComPort); // F1: no hardcoded default
        Assert.Equal(115200, profile.Connection.TeensyBaudRate);
    }
}
