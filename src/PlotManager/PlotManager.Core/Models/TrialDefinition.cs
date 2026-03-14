using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlotManager.Core.Models;

/// <summary>
/// Central trial definition — the single source of truth for an experiment.
/// Replaces the scattered TrialMap + HardwareRouting with a unified model.
/// Contains products, nozzle choice, boom routing, plot assignments, and
/// can auto-generate MachineProfile / HardwareSetup / HardwareRouting.
/// </summary>
public class TrialDefinition
{
    // ── Identity ──

    /// <summary>Trial name (e.g. "Гербицидный опыт 2026-03-10").</summary>
    public string TrialName { get; set; } = "Новое испытание";

    /// <summary>Operator notes.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Creation timestamp.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Last modification timestamp.</summary>
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;

    // ── Geometry ──

    /// <summary>Boom swath width (meters). Default: 2.8 m (4 corn rows × 0.7 m).</summary>
    public double SwathWidthMeters { get; set; } = 2.8;

    /// <summary>Number of nozzles per boom (currently 1).</summary>
    public int NozzlesPerBoom { get; set; } = 1;

    // ── Nozzle ──

    /// <summary>Selected nozzle for this trial.</summary>
    public NozzleDefinition ActiveNozzle { get; set; } = new()
    {
        Model = "TeeJet XR 110-03",
        FlowRateLPerMinAtRef = 1.18,
        ReferencePressureBar = 3.0,
        SprayAngleDegrees = 110,
        IsoColorCode = "Blue",
    };

    // ── Products ──

    /// <summary>All products used in this trial.</summary>
    public List<Product> Products { get; set; } = new();

    // ── Routing: Product → Boom channels ──

    /// <summary>
    /// Maps product name to boom channels (valve indices 0-13).
    /// Multiple channels can be assigned to one product.
    /// Example: { "Herbicide A": [0, 1, 2], "Control": [3, 4] }
    /// </summary>
    public Dictionary<string, List<int>> ProductToChannels { get; set; } = new();

    // ── Plot assignments ──

    /// <summary>
    /// Maps plot ID (e.g. "R1C1") to product name.
    /// This is the "what goes where" map from the trial design.
    /// </summary>
    public Dictionary<string, string> PlotAssignments { get; set; } = new();

    // ── Calculated fields (filled by RateCalculator) ──

    /// <summary>Recommended operating speed (km/h) based on max product rate.</summary>
    public double RecommendedSpeedKmh { get; set; }

    /// <summary>Recommended operating pressure (bar) for the active nozzle.</summary>
    public double RecommendedPressureBar { get; set; }

    /// <summary>Calculated actual rate at recommended speed/pressure (L/ha).</summary>
    public double CalculatedRateLPerHa { get; set; }

    // ════════════════════════════════════════════════════════════════════
    // Serialization
    // ════════════════════════════════════════════════════════════════════

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Serializes to JSON.</summary>
    public string ToJson()
    {
        LastModifiedUtc = DateTime.UtcNow;
        return JsonSerializer.Serialize(this, _jsonOptions);
    }

    /// <summary>Deserializes from JSON.</summary>
    public static TrialDefinition FromJson(string json) =>
        JsonSerializer.Deserialize<TrialDefinition>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Invalid trial definition JSON.");

    /// <summary>Saves to file.</summary>
    public void SaveToFile(string path) => File.WriteAllText(path, ToJson());

    /// <summary>Loads from file.</summary>
    public static TrialDefinition LoadFromFile(string path) =>
        FromJson(File.ReadAllText(path));

    // ════════════════════════════════════════════════════════════════════
    // Converters — generate working objects from this definition
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Converts to the legacy TrialMap format (backward compatibility).
    /// </summary>
    public TrialMap ToTrialMap()
    {
        return new TrialMap
        {
            TrialName = TrialName,
            PlotAssignments = new Dictionary<string, string>(PlotAssignments),
        };
    }

    /// <summary>
    /// Converts to HardwareRouting (product → channel mapping).
    /// </summary>
    public HardwareRouting ToHardwareRouting()
    {
        var routing = new HardwareRouting
        {
            ProductToSections = new Dictionary<string, List<int>>(),
            SectionToProduct = new Dictionary<int, string>(),
        };

        foreach (var (productName, channels) in ProductToChannels)
        {
            routing.ProductToSections[productName] = new List<int>(channels);
            foreach (int ch in channels)
            {
                routing.SectionToProduct[ch] = productName;
            }
        }

        return routing;
    }

    /// <summary>
    /// Builds a HardwareSetup from the routing and boom count.
    /// </summary>
    public HardwareSetup ToHardwareSetup()
    {
        // Find all unique channels
        var allChannels = new HashSet<int>();
        foreach (var channels in ProductToChannels.Values)
        {
            foreach (int ch in channels) allChannels.Add(ch);
        }

        var setup = new HardwareSetup();
        foreach (int ch in allChannels.OrderBy(c => c))
        {
            setup.Booms.Add(new Boom
            {
                BoomId = ch,
                Name = $"Boom {ch + 1}",
                ValveChannel = ch,
                YOffsetMeters = -0.30 - (ch * 0.05),
                SprayWidthMeters = 0.25,
                Enabled = true,
                Nozzles = new List<Nozzle>
                {
                    new() { NozzleId = 0, XOffsetMeters = 0 },
                },
            });
        }

        return setup;
    }

    /// <summary>
    /// Generates a MachineProfile with calculated speed/pressure.
    /// Must call RateCalculator first to populate RecommendedSpeedKmh/PressureBar.
    /// </summary>
    public MachineProfile ToMachineProfile()
    {
        var profile = new MachineProfile
        {
            ProfileName = $"Auto: {TrialName}",
            Notes = $"Auto-generated from trial '{TrialName}' at {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            Booms = new List<BoomProfile>(),
        };

        // Create boom profiles from routing
        var allChannels = new HashSet<int>();
        foreach (var channels in ProductToChannels.Values)
        {
            foreach (int ch in channels) allChannels.Add(ch);
        }

        foreach (int ch in allChannels.OrderBy(c => c))
        {
            profile.Booms.Add(new BoomProfile
            {
                BoomId = ch,
                Name = $"Boom {ch + 1}",
                ValveChannel = ch,
                YOffsetMeters = -0.30 - (ch * 0.05),
            });
        }

        return profile;
    }

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    /// <summary>All unique product names in this trial (cached).</summary>
    public IReadOnlySet<string> UniqueProducts
    {
        get
        {
            // Rebuild cache when assignments change
            if (_uniqueProductsCache == null || _uniqueProductsCacheCount != PlotAssignments.Count)
            {
                _uniqueProductsCache = PlotAssignments.Values.ToHashSet();
                _uniqueProductsCacheCount = PlotAssignments.Count;
            }
            return _uniqueProductsCache;
        }
    }
    private HashSet<string>? _uniqueProductsCache;
    private int _uniqueProductsCacheCount = -1;

    /// <summary>
    /// Gets the Product object by name.
    /// Returns null if not found.
    /// </summary>
    public Product? GetProduct(string name) =>
        Products.FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the maximum application rate across all products.
    /// Used by RateCalculator to determine the recommended speed
    /// (speed must be slow enough for the highest-rate product).
    /// </summary>
    public double GetMaxRateLPerHa()
    {
        if (Products.Count == 0) return 200;
        return Products.Where(p => !p.IsControl).Select(p => p.RateLPerHa).DefaultIfEmpty(200).Max();
    }

    /// <summary>Validates the trial definition. Returns a list of errors (empty = valid).</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(TrialName))
            errors.Add("Trial name is required.");

        if (Products.Count == 0)
            errors.Add("At least one product is required.");

        if (SwathWidthMeters <= 0)
            errors.Add("Swath width must be positive.");

        // Check all assigned products have routing
        foreach (string productName in UniqueProducts)
        {
            if (!ProductToChannels.ContainsKey(productName))
                errors.Add($"Product '{productName}' has no boom channel assignment.");

            if (GetProduct(productName) == null)
                errors.Add($"Product '{productName}' referenced in plots but not defined.");
        }

        // Check for channel conflicts (same channel → two products)
        var channelToProduct = new Dictionary<int, string>();
        foreach (var (productName, channels) in ProductToChannels)
        {
            foreach (int ch in channels)
            {
                if (channelToProduct.TryGetValue(ch, out string? existing))
                {
                    errors.Add($"Channel {ch} assigned to both '{existing}' and '{productName}'.");
                }
                else
                {
                    channelToProduct[ch] = productName;
                }
            }
        }

        return errors;
    }
}
