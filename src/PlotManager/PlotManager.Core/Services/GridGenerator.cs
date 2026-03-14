namespace PlotManager.Core.Services;

using PlotManager.Core.Models;

/// <summary>
/// Generates a rectangular grid of plots based on user-specified parameters.
/// The grid is anchored at a GPS origin point and oriented along a heading.
/// </summary>
public class GridGenerator
{
    /// <summary>
    /// Parameters for grid generation.
    /// </summary>
    public record GridParams
    {
        /// <summary>Number of rows (along the driving direction).</summary>
        public required int Rows { get; init; }

        /// <summary>Number of columns (across the boom).</summary>
        public required int Columns { get; init; }

        /// <summary>Width of each plot in meters (across the boom).</summary>
        public required double PlotWidthMeters { get; init; }

        /// <summary>Length of each plot in meters (along the driving direction).</summary>
        public required double PlotLengthMeters { get; init; }

        /// <summary>Buffer width between columns in meters.</summary>
        public double BufferWidthMeters { get; init; } = 0.5;

        /// <summary>Buffer length between rows in meters.</summary>
        public double BufferLengthMeters { get; init; } = 1.0;

        /// <summary>Origin GPS point (Southwest corner of grid).</summary>
        public required GeoPoint Origin { get; init; }

        /// <summary>Heading of the grid in degrees (0 = North, 90 = East).</summary>
        public double HeadingDegrees { get; init; } = 0.0;
    }

    /// <summary>
    /// Generates a PlotGrid from the given parameters.
    /// </summary>
    public PlotGrid Generate(GridParams parameters)
    {
        ValidateParams(parameters);

        var plots = new Plot[parameters.Rows, parameters.Columns];
        double headingRad = parameters.HeadingDegrees * Math.PI / 180.0;

        for (int row = 0; row < parameters.Rows; row++)
        {
            for (int col = 0; col < parameters.Columns; col++)
            {
                // Calculate offset from origin in local frame (meters)
                double offsetX = col * (parameters.PlotWidthMeters + parameters.BufferWidthMeters);
                double offsetY = row * (parameters.PlotLengthMeters + parameters.BufferLengthMeters);

                // Rotate by heading and convert to geographic offset
                GeoPoint sw = OffsetPoint(parameters.Origin, offsetX, offsetY, headingRad);
                GeoPoint ne = OffsetPoint(parameters.Origin,
                    offsetX + parameters.PlotWidthMeters,
                    offsetY + parameters.PlotLengthMeters,
                    headingRad);

                plots[row, col] = new Plot
                {
                    Row = row,
                    Column = col,
                    SouthWest = sw,
                    NorthEast = ne,
                    WidthMeters = parameters.PlotWidthMeters,
                    LengthMeters = parameters.PlotLengthMeters,
                };
            }
        }

        return new PlotGrid
        {
            Rows = parameters.Rows,
            Columns = parameters.Columns,
            PlotWidthMeters = parameters.PlotWidthMeters,
            PlotLengthMeters = parameters.PlotLengthMeters,
            BufferWidthMeters = parameters.BufferWidthMeters,
            BufferLengthMeters = parameters.BufferLengthMeters,
            Origin = parameters.Origin,
            HeadingDegrees = parameters.HeadingDegrees,
            Plots = plots,
        };
    }

    /// <summary>
    /// Offsets a GeoPoint by (dx, dy) meters in a rotated local frame.
    /// </summary>
    private static GeoPoint OffsetPoint(GeoPoint origin, double dx, double dy, double headingRad)
    {
        // Rotate offset by heading
        double rotatedDx = dx * Math.Cos(headingRad) - dy * Math.Sin(headingRad);
        double rotatedDy = dx * Math.Sin(headingRad) + dy * Math.Cos(headingRad);

        // Convert meters to degrees (approximate)
        double latOffset = rotatedDy / 110540.0;
        double cosLat = Math.Cos(origin.Latitude * Math.PI / 180.0);
        double lonOffset = rotatedDx / (111320.0 * Math.Max(cosLat, 0.01));

        double newLat = origin.Latitude + latOffset;
        double newLon = origin.Longitude + lonOffset;

        // Longitude wrapping (normalize to [-180, 180])
        newLon = (newLon + 180.0) % 360.0;
        if (newLon < 0) newLon += 360.0;
        newLon -= 180.0;

        return new GeoPoint(newLat, newLon);
    }

    private static void ValidateParams(GridParams p)
    {
        if (p.Rows <= 0) throw new ArgumentException("Rows must be positive.", nameof(p));
        if (p.Columns <= 0) throw new ArgumentException("Columns must be positive.", nameof(p));
        if (p.PlotWidthMeters <= 0) throw new ArgumentException("PlotWidthMeters must be positive.", nameof(p));
        if (p.PlotLengthMeters <= 0) throw new ArgumentException("PlotLengthMeters must be positive.", nameof(p));
        if (p.BufferWidthMeters < 0) throw new ArgumentException("BufferWidthMeters cannot be negative.", nameof(p));
        if (p.BufferLengthMeters < 0) throw new ArgumentException("BufferLengthMeters cannot be negative.", nameof(p));

        // T6 FIX: Reject origins near poles where cos(lat)→0 breaks meter→degree conversion
        if (p.Origin.Latitude < -85.0 || p.Origin.Latitude > 85.0)
            throw new ArgumentException(
                $"Origin latitude {p.Origin.Latitude:F1}° is outside valid range [-85, 85]. " +
                "Meter-to-degree conversion is unreliable near poles.", nameof(p));
    }
}
