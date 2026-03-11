namespace PlotManager.Core.Models;

/// <summary>
/// GPS Fix Quality levels from NMEA GGA sentence.
/// RTK Fix is required for trial-grade precision.
/// </summary>
public enum GpsFixQuality : byte
{
    /// <summary>No fix — GPS not available.</summary>
    NoFix = 0,

    /// <summary>Autonomous GPS (3-5m accuracy). NOT acceptable for trials.</summary>
    Autonomous = 1,

    /// <summary>DGPS (sub-meter). Marginal for trials.</summary>
    Dgps = 2,

    /// <summary>RTK Float (decimeter). Marginal — may drift.</summary>
    RtkFloat = 5,

    /// <summary>RTK Fix (centimeter). Required for trial work.</summary>
    RtkFix = 4,
}

/// <summary>
/// Represents a single nozzle on a boom.
/// Currently a placeholder for future per-nozzle control.
/// </summary>
public class Nozzle
{
    /// <summary>Nozzle ID within the boom (0-based).</summary>
    public int NozzleId { get; init; }

    /// <summary>X-offset from boom center in meters (positive = right of center).</summary>
    public double XOffsetMeters { get; init; }

    /// <summary>Whether this nozzle is enabled (future: per-nozzle shut-off).</summary>
    public bool Enabled { get; set; } = true;

    public override string ToString() => $"Nozzle {NozzleId} (X={XOffsetMeters:+0.00;-0.00}m)";
}

/// <summary>
/// Represents a physical boom (штанга) on the sprayer.
/// Each boom has its own Y-offset relative to the GPS antenna
/// and maps to one valve channel on the Teensy.
/// </summary>
public class Boom
{
    /// <summary>Boom ID (0-based, 0–9 for 10 booms).</summary>
    public int BoomId { get; init; }

    /// <summary>Human-readable name (e.g. "Boom 1", "Left Wing").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Y-offset in meters from the GPS antenna along the machine heading.
    /// Negative = mounted behind antenna, Positive = in front.
    /// Boom 1 (front) might be -0.50m, Boom 10 (rear) might be -1.00m.
    /// </summary>
    public double YOffsetMeters { get; set; }

    /// <summary>Valve channel on Teensy (0–13). Maps to bit position in the 14-bit mask.</summary>
    public int ValveChannel { get; init; }

    /// <summary>
    /// Physical spray width of this boom along the driving direction (meters).
    /// Used for overlap % calculation. Measure from front to back of spray pattern.
    /// For a single-nozzle boom this is roughly the nozzle fan width projected forward.
    /// </summary>
    public double SprayWidthMeters { get; set; } = 0.25;

    /// <summary>
    /// Minimum overlap percentage to ACTIVATE this boom (0–100).
    /// Boom turns ON when this % of its spray footprint is inside the plot.
    /// Default: 70% — the boom starts spraying when 70% of its width is over the plot.
    /// </summary>
    public double ActivationOverlapPercent { get; set; } = 70;

    /// <summary>
    /// Minimum overlap percentage to keep this boom active (0–100).
    /// Boom turns OFF when overlap drops below this %.
    /// Default: 30% — allows gradual transition at plot edges.
    /// </summary>
    public double DeactivationOverlapPercent { get; set; } = 30;

    /// <summary>Nozzles mounted on this boom (for future per-nozzle control).</summary>
    public List<Nozzle> Nozzles { get; init; } = new();

    /// <summary>Whether this boom is currently active/installed.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Calculates the overlap percentage between this boom's spray footprint
    /// and a plot boundary along the driving direction.
    /// </summary>
    /// <param name="boomPosition">GPS position of this boom's center (after Y-offset projection).</param>
    /// <param name="plot">The plot to check against.</param>
    /// <param name="headingRad">Current heading in radians.</param>
    /// <returns>Overlap percentage 0–100. 100 = fully inside, 0 = fully outside.</returns>
    public double CalculateOverlap(GeoPoint boomPosition, Plot plot, double headingRad)
    {
        if (SprayWidthMeters <= 0) return plot.Contains(boomPosition) ? 100 : 0;

        double halfWidth = SprayWidthMeters / 2.0;

        // The spray footprint extends from (boomPos - halfWidth) to (boomPos + halfWidth)
        // along the heading direction.
        // We compute what fraction of this segment overlaps with the plot's
        // north-south extent (simplified for heading=0; for arbitrary headings
        // we project along the heading vector).

        double cosH = Math.Cos(headingRad);
        double sinH = Math.Sin(headingRad);

        // Project boom front/back points
        double cosLat = Math.Cos(boomPosition.Latitude * Math.PI / 180.0); // cache — used twice below
        double frontLat = boomPosition.Latitude + (halfWidth / 110540.0) * cosH;
        double frontLon = boomPosition.Longitude + (halfWidth / (111320.0 * cosLat)) * sinH;
        double backLat = boomPosition.Latitude - (halfWidth / 110540.0) * cosH;
        double backLon = boomPosition.Longitude - (halfWidth / (111320.0 * cosLat)) * sinH;

        // Compute overlap along driving direction by projecting onto the
        // plot's lat/lon bounding box
        double boomMinLat = Math.Min(frontLat, backLat);
        double boomMaxLat = Math.Max(frontLat, backLat);
        double boomMinLon = Math.Min(frontLon, backLon);
        double boomMaxLon = Math.Max(frontLon, backLon);

        // Lat overlap
        double latOverlap = Math.Max(0,
            Math.Min(boomMaxLat, plot.NorthEast.Latitude) -
            Math.Max(boomMinLat, plot.SouthWest.Latitude));
        double latSpan = boomMaxLat - boomMinLat;

        // Lon overlap
        double lonOverlap = Math.Max(0,
            Math.Min(boomMaxLon, plot.NorthEast.Longitude) -
            Math.Max(boomMinLon, plot.SouthWest.Longitude));
        double lonSpan = boomMaxLon - boomMinLon;

        // For heading ≈ 0/180: primary axis is latitude
        // For heading ≈ 90/270: primary axis is longitude
        // Use the dominant axis based on heading
        double absCos = Math.Abs(cosH);
        double absSin = Math.Abs(sinH);

        double overlapFraction;
        if (absCos > absSin && latSpan > 1e-12)
            overlapFraction = latOverlap / latSpan;
        else if (lonSpan > 1e-12)
            overlapFraction = lonOverlap / lonSpan;
        else
            overlapFraction = plot.Contains(boomPosition) ? 1.0 : 0;

        return Math.Clamp(overlapFraction * 100.0, 0, 100);
    }

    public override string ToString() =>
        $"Boom {BoomId + 1} (Ch={ValveChannel}, Y={YOffsetMeters:+0.00;-0.00}m, " +
        $"Overlap={ActivationOverlapPercent:F0}%/{DeactivationOverlapPercent:F0}%)";
}

/// <summary>
/// Complete hardware configuration for the sprayer.
/// Contains the boom hierarchy and future feature flags.
/// </summary>
public class HardwareSetup
{
    /// <summary>All physical booms on the sprayer.</summary>
    public List<Boom> Booms { get; init; } = new();


    /// <summary>Number of enabled booms.</summary>
    public int EnabledBoomCount => Booms.Count(b => b.Enabled);

    /// <summary>
    /// Gets the boom assigned to a product via the routing table.
    /// Returns all booms whose valve channel matches any section assigned to the product.
    /// </summary>
    public IReadOnlyList<Boom> GetBoomsForProduct(string product, HardwareRouting routing)
    {
        IReadOnlyList<int> sections = routing.GetSections(product);
        return Booms.Where(b => b.Enabled && sections.Contains(b.ValveChannel)).ToList();
    }

    /// <summary>
    /// Builds a valve mask from a list of active boom IDs.
    /// </summary>
    public ushort BuildValveMask(IEnumerable<int> activeBoomIds)
    {
        ushort mask = 0;
        foreach (int boomId in activeBoomIds)
        {
            Boom? boom = Booms.FirstOrDefault(b => b.BoomId == boomId && b.Enabled);
            if (boom != null && boom.ValveChannel >= 0 && boom.ValveChannel < 14)
            {
                mask |= (ushort)(1 << boom.ValveChannel);
            }
        }
        return mask;
    }

    /// <summary>
    /// Creates a default 10-boom setup with sequential valve channels
    /// and evenly spaced Y-offsets.
    /// </summary>
    /// <param name="frontOffsetMeters">Y-offset of the frontmost boom (e.g. -0.30).</param>
    /// <param name="spacingMeters">Spacing between booms (e.g. 0.05 = 5cm apart).</param>
    public static HardwareSetup CreateDefault10Boom(
        double frontOffsetMeters = -0.30,
        double spacingMeters = 0.05)
    {
        var setup = new HardwareSetup();
        for (int i = 0; i < 10; i++)
        {
            setup.Booms.Add(new Boom
            {
                BoomId = i,
                Name = $"Boom {i + 1}",
                ValveChannel = i,
                YOffsetMeters = frontOffsetMeters - (i * spacingMeters),
                Nozzles = new List<Nozzle>
                {
                    new Nozzle { NozzleId = 0, XOffsetMeters = 0 }
                },
            });
        }
        return setup;
    }
}
