using PlotManager.Core.Models;
using PlotManager.Core.Services;
using Xunit;

namespace PlotManager.Tests;

/// <summary>
/// Tests for Phase 1-2: Product, NozzleCatalog, TrialDefinition, PassState, RateCalculator.
/// </summary>
public class TrialSystemTests
{
    // ════════════════════════════════════════════════════════════════════
    // NozzleDefinition — square-root pressure law
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Nozzle_FlowRateAtRefPressure_EqualsNominal()
    {
        var nozzle = new NozzleDefinition
        {
            FlowRateLPerMinAtRef = 1.18,
            ReferencePressureBar = 3.0,
        };

        double flow = nozzle.GetFlowRateAtPressure(3.0);
        Assert.Equal(1.18, flow, 3);
    }

    [Fact]
    public void Nozzle_FlowRateAtDoublePressure_ScalesBySqrt2()
    {
        var nozzle = new NozzleDefinition
        {
            FlowRateLPerMinAtRef = 1.0,
            ReferencePressureBar = 3.0,
        };

        // At 6 bar: Q = 1.0 × √(6/3) = 1.0 × √2 ≈ 1.414
        double flow = nozzle.GetFlowRateAtPressure(6.0);
        Assert.InRange(flow, 1.41, 1.42);
    }

    [Fact]
    public void Nozzle_GetPressureForFlowRate_Inverse()
    {
        var nozzle = new NozzleDefinition
        {
            FlowRateLPerMinAtRef = 1.18,
            ReferencePressureBar = 3.0,
        };

        // Get flow at 4 bar
        double flowAt4 = nozzle.GetFlowRateAtPressure(4.0);

        // Inverse should return ~4 bar
        double pressure = nozzle.GetPressureForFlowRate(flowAt4);
        Assert.Equal(4.0, pressure, 2);
    }

    [Fact]
    public void Nozzle_IsPressureInRange()
    {
        var nozzle = new NozzleDefinition
        {
            MinPressureBar = 1.5,
            MaxPressureBar = 6.0,
        };

        Assert.True(nozzle.IsPressureInRange(3.0));
        Assert.False(nozzle.IsPressureInRange(1.0));
        Assert.False(nozzle.IsPressureInRange(7.0));
    }

    // ════════════════════════════════════════════════════════════════════
    // NozzleCatalog
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void NozzleCatalog_DefaultHas5Nozzles()
    {
        NozzleCatalog catalog = NozzleCatalog.CreateDefault();
        Assert.Equal(15, catalog.Nozzles.Count);
    }

    [Fact]
    public void NozzleCatalog_FindByModel()
    {
        NozzleCatalog catalog = NozzleCatalog.CreateDefault();

        var found = catalog.FindByModel("TeeJet XR 110-03");
        Assert.NotNull(found);
        Assert.Equal("Blue", found.IsoColorCode);
    }

    [Fact]
    public void NozzleCatalog_JsonRoundTrip()
    {
        NozzleCatalog original = NozzleCatalog.CreateDefault();
        string json = original.ToJson();
        NozzleCatalog restored = NozzleCatalog.FromJson(json);

        Assert.Equal(original.Nozzles.Count, restored.Nozzles.Count);
        Assert.Equal("TeeJet XR 110-03", restored.Nozzles[3].Model);
    }

    // ════════════════════════════════════════════════════════════════════
    // RateCalculator — forward
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Rate_Forward_XR11003_3bar_5kmh_2_8m()
    {
        // TeeJet XR 110-03 @ 3 bar, 5 km/h, 2.8 m swath, 1 nozzle
        // Q = 1.18 L/min, Rate = 1.18 × 600 / (5 × 2.8) = 708 / 14 = 50.57 L/ha
        var nozzle = new NozzleDefinition
        {
            FlowRateLPerMinAtRef = 1.18,
            ReferencePressureBar = 3.0,
        };

        double rate = RateCalculator.CalculateRateLPerHa(nozzle, 3.0, 5.0, 2.8);
        Assert.InRange(rate, 50, 51); // ~50.57
    }

    [Fact]
    public void Rate_Forward_ZeroSpeed_ReturnsZero()
    {
        var nozzle = new NozzleDefinition { FlowRateLPerMinAtRef = 1.18 };
        Assert.Equal(0, RateCalculator.CalculateRateLPerHa(nozzle, 3.0, 0, 2.8));
    }

    // ════════════════════════════════════════════════════════════════════
    // RateCalculator — inverse (speed)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Rate_InverseSpeed_200LPerHa()
    {
        // Want 200 L/ha with XR 110-03 @ 3 bar, 2.8 m swath
        // V = Q × 600 / (R × B) = 1.18 × 600 / (200 × 2.8) = 708 / 560 ≈ 1.264 km/h
        var nozzle = new NozzleDefinition
        {
            FlowRateLPerMinAtRef = 1.18,
            ReferencePressureBar = 3.0,
        };

        double speed = RateCalculator.CalculateSpeedKmh(nozzle, 3.0, 200, 2.8);
        Assert.InRange(speed, 1.2, 1.3);
    }

    [Fact]
    public void Rate_ForwardAndInverse_AreConsistent()
    {
        var nozzle = new NozzleDefinition
        {
            FlowRateLPerMinAtRef = 1.18,
            ReferencePressureBar = 3.0,
        };

        double speed = RateCalculator.CalculateSpeedKmh(nozzle, 3.0, 200, 2.8);
        double rateBack = RateCalculator.CalculateRateLPerHa(nozzle, 3.0, speed, 2.8);

        Assert.Equal(200, rateBack, 1);
    }

    // ════════════════════════════════════════════════════════════════════
    // RateCalculator — inverse (pressure)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Rate_InversePressure_Consistent()
    {
        var nozzle = new NozzleDefinition
        {
            FlowRateLPerMinAtRef = 1.18,
            ReferencePressureBar = 3.0,
        };

        // Want 100 L/ha at 3 km/h, 2.8 m → what pressure?
        double pressure = RateCalculator.CalculatePressureBar(nozzle, 100, 3.0, 2.8);

        // Verify: at that pressure and speed, rate should be ~100
        double rateBack = RateCalculator.CalculateRateLPerHa(nozzle, pressure, 3.0, 2.8);
        Assert.Equal(100, rateBack, 1);
    }

    // ════════════════════════════════════════════════════════════════════
    // RateCalculator — validation
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_PressureOutOfRange_Error()
    {
        var nozzle = new NozzleDefinition
        {
            FlowRateLPerMinAtRef = 1.18,
            ReferencePressureBar = 3.0,
            MinPressureBar = 1.5,
            MaxPressureBar = 6.0,
        };

        var result = RateCalculator.Validate(nozzle, 8.0, 5.0, 200, 2.8);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("outside nozzle range"));
    }

    // ════════════════════════════════════════════════════════════════════
    // RateCalculator — AutoCalculate
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AutoCalculate_FillsTrialFields()
    {
        var trial = new TrialDefinition
        {
            SwathWidthMeters = 2.8,
            NozzlesPerBoom = 1,
            ActiveNozzle = new NozzleDefinition
            {
                FlowRateLPerMinAtRef = 1.18,
                ReferencePressureBar = 3.0,
                MinPressureBar = 1.0,
                MaxPressureBar = 20.0, // Wide range so clamping doesn't cause errors
            },
            Products = new()
            {
                new Product { Name = "Herbicide A", RateLPerHa = 200 },
                new Product { Name = "Control", RateLPerHa = 200, IsControl = true },
            },
        };

        // minSpeed=3 (default) → ideal speed ~1.26 is clamped to 3
        // → pressure recalculated to achieve 200 L/ha at 3 km/h
        var result = RateCalculator.AutoCalculate(trial);

        Assert.True(trial.RecommendedSpeedKmh >= 3.0);
        Assert.True(trial.RecommendedPressureBar > 0);
        Assert.True(trial.CalculatedRateLPerHa > 0);
        Assert.Equal(200.0, trial.CalculatedRateLPerHa, 0);
        Assert.True(result.SpeedWasClamped);
    }

    [Fact]
    public void AutoCalculate_SpeedClamped_PressureRecalculated()
    {
        // XR 110-03 @ 200 L/ha, 2.8 m: ideal speed = 1.26 km/h
        // Clamp to 3 km/h → need higher pressure
        var trial = new TrialDefinition
        {
            SwathWidthMeters = 2.8,
            ActiveNozzle = new NozzleDefinition
            {
                Model = "XR 110-03",
                FlowRateLPerMinAtRef = 1.18,
                ReferencePressureBar = 3.0,
                MinPressureBar = 1.0,
                MaxPressureBar = 6.0,
            },
            Products = new() { new Product { Name = "H", RateLPerHa = 200 } },
        };

        var result = RateCalculator.AutoCalculate(trial, minSpeedKmh: 3.0);

        Assert.True(result.SpeedWasClamped);
        Assert.Equal(3.0, trial.RecommendedSpeedKmh);
        // Pressure at 3 km/h for 200 L/ha: ~16.9 bar → way over 6 bar max
        Assert.True(trial.RecommendedPressureBar > 6.0);
        Assert.False(result.IsValid); // Pressure out of range
    }

    [Fact]
    public void AutoCalculate_NozzleSuggestion_BiggerNozzleWorks()
    {
        // XR 110-01 (0.39 L/min) can't do 100 L/ha at 3 km/h:
        // Q_needed = 100×3×2.8/600 = 1.4 L/min → P = 3×(1.4/0.39)² = 38.6 bar → way out
        // But bigger nozzles from catalog CAN handle it.
        var trial = new TrialDefinition
        {
            SwathWidthMeters = 2.8,
            ActiveNozzle = new NozzleDefinition
            {
                Model = "TeeJet XR 110-01",
                FlowRateLPerMinAtRef = 0.39,
                ReferencePressureBar = 3.0,
                MinPressureBar = 1.0,
                MaxPressureBar = 6.0,
            },
            Products = new() { new Product { Name = "H", RateLPerHa = 100 } },
        };

        NozzleCatalog catalog = NozzleCatalog.CreateDefault();
        var result = RateCalculator.AutoCalculate(trial, minSpeedKmh: 3.0, catalog: catalog);

        Assert.True(result.SpeedWasClamped);
        Assert.True(result.NozzleSuggestions.Count > 0);
        // At least one suggestion should fit
        var best = result.NozzleSuggestions[0];
        Assert.True(best.Nozzle.IsPressureInRange(best.RequiredPressureBar));
        Assert.Contains("✅", best.Recommendation);
    }

    [Fact]
    public void AutoCalculate_NoClampNeeded_WhenSpeedInRange()
    {
        // Low rate → high speed → no clamping needed
        var trial = new TrialDefinition
        {
            SwathWidthMeters = 2.8,
            ActiveNozzle = new NozzleDefinition
            {
                FlowRateLPerMinAtRef = 1.96,
                ReferencePressureBar = 3.0,
                MinPressureBar = 1.0,
                MaxPressureBar = 6.0,
            },
            Products = new() { new Product { Name = "H", RateLPerHa = 50 } },
        };

        // 50 L/ha with XR-05 @ 3 bar: speed = 1.96×600/(50×2.8) = 8.4 km/h
        var result = RateCalculator.AutoCalculate(trial, minSpeedKmh: 3.0, maxSpeedKmh: 10.0);

        Assert.False(result.SpeedWasClamped);
        Assert.InRange(trial.RecommendedSpeedKmh, 8.0, 9.0);
    }

    [Fact]
    public void AutoCalculate_LowMinSpeed_BypassesClamp()
    {
        // If operator says min speed is 1 km/h, no clamping at all
        var trial = new TrialDefinition
        {
            SwathWidthMeters = 2.8,
            ActiveNozzle = new NozzleDefinition
            {
                FlowRateLPerMinAtRef = 1.18,
                ReferencePressureBar = 3.0,
            },
            Products = new() { new Product { Name = "H", RateLPerHa = 200 } },
        };

        var result = RateCalculator.AutoCalculate(trial, minSpeedKmh: 1.0);

        Assert.False(result.SpeedWasClamped);
        Assert.InRange(trial.RecommendedSpeedKmh, 1.2, 1.3);
        Assert.Equal(3.0, trial.RecommendedPressureBar); // Reference pressure used
    }

    // ════════════════════════════════════════════════════════════════════
    // TrialDefinition — converters
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Trial_ToTrialMap_PreservesAssignments()
    {
        var trial = new TrialDefinition
        {
            TrialName = "Test",
            PlotAssignments = new() { ["R1C1"] = "Product A", ["R1C2"] = "Control" },
        };

        TrialMap map = trial.ToTrialMap();

        Assert.Equal("Test", map.TrialName);
        Assert.Equal("Product A", map.GetProduct("R1C1"));
        Assert.Equal("Control", map.GetProduct("R1C2"));
    }

    [Fact]
    public void Trial_ToHardwareRouting_MapsChannels()
    {
        var trial = new TrialDefinition
        {
            ProductToChannels = new()
            {
                ["Product A"] = new() { 0, 1, 2 },
                ["Control"] = new() { 3, 4 },
            },
        };

        HardwareRouting routing = trial.ToHardwareRouting();

        Assert.Equal(3, routing.GetSections("Product A").Count);
        Assert.Equal(2, routing.GetSections("Control").Count);
        Assert.Equal("Product A", routing.SectionToProduct[0]);
        Assert.Equal("Control", routing.SectionToProduct[3]);
    }

    [Fact]
    public void Trial_ToMachineProfile_UsesCalculatedValues()
    {
        var trial = new TrialDefinition
        {
            TrialName = "Herbicide Trial",
            RecommendedSpeedKmh = 3.5,
            RecommendedPressureBar = 3.0,
            CalculatedRateLPerHa = 200,
            ProductToChannels = new()
            {
                ["Product A"] = new() { 0, 1 },
            },
        };

        MachineProfile profile = trial.ToMachineProfile();

        Assert.Equal(3.5, profile.TargetSpeedKmh);
        Assert.Equal(3.0, profile.OperatingPressureBar);
        Assert.Equal(200, profile.TargetRateLPerHa);
        Assert.Equal(2, profile.Booms.Count);
        Assert.StartsWith("Auto:", profile.ProfileName);
    }

    // ════════════════════════════════════════════════════════════════════
    // TrialDefinition — validation
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Trial_Validate_MissingProduct_Error()
    {
        var trial = new TrialDefinition
        {
            TrialName = "Test",
            Products = new()
            {
                new Product { Name = "Product A", RateLPerHa = 200 },
            },
            PlotAssignments = new() { ["R1C1"] = "Product A", ["R1C2"] = "Product B" },
            ProductToChannels = new() { ["Product A"] = new() { 0 } },
        };

        var errors = trial.Validate();

        Assert.Contains(errors, e => e.Contains("Product B"));
    }

    [Fact]
    public void Trial_Validate_ChannelConflict_Error()
    {
        var trial = new TrialDefinition
        {
            TrialName = "Test",
            Products = new()
            {
                new Product { Name = "A" }, new Product { Name = "B" },
            },
            PlotAssignments = new() { ["R1C1"] = "A" },
            ProductToChannels = new()
            {
                ["A"] = new() { 0, 1 },
                ["B"] = new() { 1, 2 }, // Channel 1 conflict!
            },
        };

        var errors = trial.Validate();
        Assert.Contains(errors, e => e.Contains("Channel 1"));
    }

    // ════════════════════════════════════════════════════════════════════
    // TrialDefinition — JSON round-trip
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Trial_JsonRoundTrip()
    {
        var original = new TrialDefinition
        {
            TrialName = "JSON Test",
            SwathWidthMeters = 2.8,
            Products = new()
            {
                new Product { Name = "Herbicide", RateLPerHa = 300, FluidType = FluidType.WaterSolution },
            },
            ProductToChannels = new() { ["Herbicide"] = new() { 0, 1 } },
            PlotAssignments = new() { ["R1C1"] = "Herbicide" },
            RecommendedSpeedKmh = 4.2,
            RecommendedPressureBar = 3.5,
        };

        string json = original.ToJson();
        TrialDefinition restored = TrialDefinition.FromJson(json);

        Assert.Equal("JSON Test", restored.TrialName);
        Assert.Equal(2.8, restored.SwathWidthMeters);
        Assert.Single(restored.Products);
        Assert.Equal(300, restored.Products[0].RateLPerHa);
        Assert.Equal(FluidType.WaterSolution, restored.Products[0].FluidType);
        Assert.Equal(4.2, restored.RecommendedSpeedKmh);
    }

    // ════════════════════════════════════════════════════════════════════
    // PassState — deviation tracking
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void PassState_RecordSpeedSample_TracksDeviation()
    {
        var pass = new PassState
        {
            PassNumber = 1,
            LockedSpeedKmh = 5.0,
        };

        pass.RecordSpeedSample(5.0);  // 0% deviation
        pass.RecordSpeedSample(5.5);  // 10% deviation
        pass.RecordSpeedSample(4.5);  // 10% deviation

        Assert.Equal(10, pass.MaxSpeedDeviationPercent, 1);
        Assert.Equal(3, pass.SpeedSampleCount);
        // Avg = (0 + 10 + 10) / 3 ≈ 6.67%
        Assert.InRange(pass.AvgSpeedDeviationPercent, 6.5, 6.8);
    }

    [Fact]
    public void PassState_Complete_SetsEndTime()
    {
        var pass = new PassState
        {
            PassNumber = 1,
            StartTimeUtc = DateTime.UtcNow,
        };

        Assert.True(pass.IsActive);

        pass.Complete();

        Assert.False(pass.IsActive);
        Assert.NotNull(pass.EndTimeUtc);
    }

    // ════════════════════════════════════════════════════════════════════
    // Product
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Product_ToString_IncludesRate()
    {
        var product = new Product { Name = "Базагран", RateLPerHa = 300 };
        Assert.Contains("300", product.ToString());
    }

    [Fact]
    public void Product_Control_MarkedInToString()
    {
        var product = new Product { Name = "Вода", IsControl = true };
        Assert.Contains("контроль", product.ToString());
    }
}
