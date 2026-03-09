namespace PlotManager.Core.Models;

/// <summary>
/// Represents the trial map — mapping each plot to its assigned product/treatment.
/// Loaded from a CSV file.
/// </summary>
public class TrialMap
{
    /// <summary>
    /// Trial name/description.
    /// </summary>
    public string TrialName { get; init; } = string.Empty;

    /// <summary>
    /// Mapping of PlotId (e.g. "R1C1") to product name.
    /// </summary>
    public Dictionary<string, string> PlotAssignments { get; init; } = new();

    /// <summary>
    /// All unique product names in the trial.
    /// </summary>
    public IReadOnlySet<string> Products =>
        PlotAssignments.Values.ToHashSet();

    /// <summary>
    /// Gets the product assigned to a specific plot.
    /// Returns null if the plot is not in the trial map.
    /// </summary>
    public string? GetProduct(string plotId)
    {
        return PlotAssignments.TryGetValue(plotId, out string? product)
            ? product
            : null;
    }

    /// <summary>
    /// Gets the product assigned to a specific plot by row/column.
    /// </summary>
    public string? GetProduct(int row, int column)
    {
        return GetProduct($"R{row + 1}C{column + 1}");
    }
}

/// <summary>
/// Maps products to physical boom sections (1-14).
/// Defines which canister/section delivers which product.
/// </summary>
public class HardwareRouting
{
    /// <summary>Total number of boom sections (fixed at 14).</summary>
    public const int TotalSections = 14;

    /// <summary>
    /// Mapping of product name to section index (0-based, 0-13).
    /// A product can be assigned to multiple sections.
    /// </summary>
    public Dictionary<string, List<int>> ProductToSections { get; init; } = new();

    /// <summary>
    /// Reverse mapping: section index to product name.
    /// </summary>
    public Dictionary<int, string> SectionToProduct { get; init; } = new();

    /// <summary>
    /// Gets the section indices assigned to a product.
    /// </summary>
    public IReadOnlyList<int> GetSections(string product)
    {
        return ProductToSections.TryGetValue(product, out List<int>? sections)
            ? sections.AsReadOnly()
            : Array.Empty<int>();
    }

    /// <summary>
    /// Validates that all products in the trial map have assigned sections.
    /// </summary>
    public List<string> Validate(TrialMap trialMap)
    {
        var errors = new List<string>();
        foreach (string product in trialMap.Products)
        {
            if (!ProductToSections.ContainsKey(product))
            {
                errors.Add($"Product '{product}' has no assigned boom section.");
            }
        }
        return errors;
    }
}
