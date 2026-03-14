using System;

namespace PlotManager.Core.Models;

public class ActiveIngredient
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// E.g. Herbicide, Fungicide, Insecticide, Fertilizer
    /// </summary>
    public string Category { get; set; } = string.Empty;
}
