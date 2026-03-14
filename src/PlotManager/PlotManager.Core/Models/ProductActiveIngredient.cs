namespace PlotManager.Core.Models;

public class ProductActiveIngredient
{
    public ActiveIngredient? Ingredient { get; set; }
    
    /// <summary>
    /// Concentration of the active ingredient within the formulated product (e.g. 480)
    /// </summary>
    public double Concentration { get; set; }
    
    /// <summary>
    /// Measurement unit (e.g. g/L, g/kg)
    /// </summary>
    public string Unit { get; set; } = "g/L";
}
