using System.Collections.Generic;

namespace PlotManager.Core.Models.Hardware;

public class Tank
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// The physical relays/valves connected to this tank's output
    /// </summary>
    public List<int> ConnectedValveIds { get; set; } = new();

    /// <summary>
    /// The product currently loaded in this tank
    /// </summary>
    public Product? LoadedProduct { get; set; }
}
