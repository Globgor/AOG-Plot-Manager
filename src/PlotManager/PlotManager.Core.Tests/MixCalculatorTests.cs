using PlotManager.Core.Models;
using PlotManager.Core.Services;
using Xunit;
using System;

namespace PlotManager.Core.Tests.Services;

public class MixCalculatorTests
{
    private readonly MixCalculator _calculator = new();

    [Fact]
    public void CalculatePneumaticMix_WithZeroSpeed_ShouldThrowArgumentException()
    {
        // Arrange
        var product = new Product { Name = "Test Chem" };
        // We will just use the default logic which falls back to 1.0 if not found for simplicity here.

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _calculator.CalculatePneumaticMix(
                product: product,
                targetAiRateGha: 1000,
                activeIngredientId: "AI-1",
                machineSpeedKmh: 0.0, // <-- The Edge Case
                nozzleFlowLmin: 1.5,
                boomWidthMeters: 2.0);
        });
    }

    [Fact]
    public void CalculatePneumaticMix_WithZeroWidth_ShouldThrowArgumentException()
    {
        // Arrange
        var product = new Product { Name = "Test Chem" };

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _calculator.CalculatePneumaticMix(
                product: product,
                targetAiRateGha: 1000,
                activeIngredientId: "AI-1",
                machineSpeedKmh: 10.0,
                nozzleFlowLmin: 1.5,
                boomWidthMeters: 0.0); // <-- The Edge Case
        });
    }
}
