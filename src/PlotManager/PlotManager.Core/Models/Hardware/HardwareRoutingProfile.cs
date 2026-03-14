using System.Collections.Generic;
using System.Linq;

namespace PlotManager.Core.Models.Hardware;

public class HardwareRoutingProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Default Profile";
    public List<Tank> Tanks { get; set; } = new();

    public int[] GetValvesForProduct(Product product)
    {
        return Tanks.Where(t => t.LoadedProduct?.Id == product.Id)
                    .SelectMany(t => t.ConnectedValveIds)
                    .Distinct()
                    .ToArray();
    }
}
