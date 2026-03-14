using PlotManager.Core.Models;
using System.Collections.Generic;

namespace PlotManager.Core.Services;

/// <summary>
/// Handles agronomic calculations for pneumatic sprayers where nozzle flow and air pressure are fixed.
/// In multi-boom scenarios (or even single boom), the tractor's speed dictates the Volume Rate (L/ha),
/// so the concentration of products in the tanks must be perfectly mixed to hit the required Active Ingredient dose.
/// </summary>
public class MixCalculator
{
    public class MixResult
    {
        public Product Product { get; set; }
        
        /// <summary>
        /// What the actual Volume Rate (L/ha) of the machine will be based on fixed speed.
        /// </summary>
        public double ActualVolumeRateLha { get; set; }

        /// <summary>
        /// Amount of commercial product required per 1 Liter of water (or carrier) in the tank.
        /// </summary>
        public double ProductPerLiterMix { get; set; }

        /// <summary>
        /// Total amount of product needed for a specific total tank mix volume.
        /// </summary>
        public double GetTotalProductNeeded(double tankVolumeLiters) => ProductPerLiterMix * tankVolumeLiters;
    }

    /// <summary>
    /// Calculates the required tank mix concentration to achieve a required Target Active Ingredient Rate (g/ha)
    /// given the physical limitations of a pneumatic sprayer.
    /// </summary>
    /// <param name="product">The commercial product to spray</param>
    /// <param name="targetAiRateGha">The required dose of active ingredient in g/ha (e.g., 1000 g/ha)</param>
    /// <param name="activeIngredientId">Which active ingredient we are targeting (since products can have multiple)</param>
    /// <param name="machineSpeedKmh">The unified speed the tractor will travel at (km/h)</param>
    /// <param name="nozzleFlowLmin">The physical fixed flow rate from the pneumatic cans (L/min)</param>
    /// <param name="boomWidthMeters">The width of the boom section applying this product (m)</param>
    public MixResult CalculatePneumaticMix(
        Product product, 
        double targetAiRateGha, 
        string activeIngredientId,
        double machineSpeedKmh, 
        double nozzleFlowLmin, 
        double boomWidthMeters)
    {
        if (machineSpeedKmh <= 0) throw new ArgumentOutOfRangeException(nameof(machineSpeedKmh), "Speed must be strictly positive to calculate a rate.");
        if (boomWidthMeters <= 0) throw new ArgumentOutOfRangeException(nameof(boomWidthMeters), "Boom width must be strictly positive.");
        if (nozzleFlowLmin < 0) throw new ArgumentOutOfRangeException(nameof(nozzleFlowLmin), "Nozzle flow cannot be negative.");

        // 1. Calculate actual volume rate (L/ha) forced by physics
        // Rate = (Flow * 600) / (Speed * Width)
        double actualVolumeRateLha = (nozzleFlowLmin * 600) / (machineSpeedKmh * boomWidthMeters);

        // 2. We need 'targetAiRateGha' of AI sprayed over 1 hectare.
        // Since 1 hectare will receive 'actualVolumeRateLha' of liquid from the tank,
        // the tank must contain this much AI per Liter:
        double requiredAiPerLiterMix = targetAiRateGha / actualVolumeRateLha;

        // 3. Find the concentration of this AI in the commercial product
        var aiListing = product.ActiveIngredients.Find(ai => ai.Ingredient?.Id == activeIngredientId);
        double productAiConcentrationGL = aiListing?.Concentration ?? 1.0; // Default to 1:1 if not found to avoid div/0

        // 4. Calculate how much commercial product is needed per liter of tank mix
        double productPerLiterMix = requiredAiPerLiterMix / productAiConcentrationGL;

        return new MixResult
        {
            Product = product,
            ActualVolumeRateLha = actualVolumeRateLha,
            ProductPerLiterMix = productPerLiterMix
        };
    }
}
