namespace PlotManager.Core.Models;

/// <summary>
/// Represents a rectangular plot (делянка) in the trial grid.
/// Defined by its grid position and geographic bounds.
/// </summary>
public class Plot
{
    /// <summary>Row index in the grid (0-based).</summary>
    public int Row { get; init; }

    /// <summary>Column index in the grid (0-based).</summary>
    public int Column { get; init; }

    /// <summary>Human-readable plot ID, e.g. "R1C3".</summary>
    public string PlotId => $"R{Row + 1}C{Column + 1}";

    /// <summary>Southwest corner of the plot.</summary>
    public GeoPoint SouthWest { get; init; }

    /// <summary>Northeast corner of the plot.</summary>
    public GeoPoint NorthEast { get; init; }

    /// <summary>Plot width in meters (East-West).</summary>
    public double WidthMeters { get; init; }

    /// <summary>Plot length in meters (North-South).</summary>
    public double LengthMeters { get; init; }

    /// <summary>
    /// Checks whether a given GPS point falls within this plot's bounds.
    /// </summary>
    public bool Contains(GeoPoint point)
    {
        return point.Latitude >= SouthWest.Latitude &&
               point.Latitude <= NorthEast.Latitude &&
               point.Longitude >= SouthWest.Longitude &&
               point.Longitude <= NorthEast.Longitude;
    }

    public override string ToString() =>
        $"Plot {PlotId} [{SouthWest} → {NorthEast}] {WidthMeters:F1}×{LengthMeters:F1}m";
}

/// <summary>
/// Represents the entire grid of plots for a trial.
/// </summary>
public class PlotGrid
{
    /// <summary>Number of rows in the grid.</summary>
    public int Rows { get; init; }

    /// <summary>Number of columns in the grid.</summary>
    public int Columns { get; init; }

    /// <summary>Width of each plot in meters.</summary>
    public double PlotWidthMeters { get; init; }

    /// <summary>Length of each plot in meters.</summary>
    public double PlotLengthMeters { get; init; }

    /// <summary>Buffer (path) width between rows in meters.</summary>
    public double BufferWidthMeters { get; init; }

    /// <summary>Buffer (path) length at start/end of plots in meters.</summary>
    public double BufferLengthMeters { get; init; }

    /// <summary>Origin point (Southwest corner of the entire grid).</summary>
    public GeoPoint Origin { get; init; }

    /// <summary>Heading of the grid in degrees (0 = North, 90 = East).</summary>
    public double HeadingDegrees { get; init; }

    /// <summary>All plots in the grid, indexed as [row, column].</summary>
    public Plot[,] Plots { get; init; } = new Plot[0, 0];

    /// <summary>Total number of plots.</summary>
    public int TotalPlots => Rows * Columns;

    /// <summary>
    /// Shifts all plot coordinates by the given offset in meters.
    /// Used for RTK nudge alignment — adjusts the virtual grid
    /// to match physical field stakes after satellite constellation changes.
    /// </summary>
    /// <param name="dNorthMeters">Northward shift in meters (positive = north).</param>
    /// <param name="dEastMeters">Eastward shift in meters (positive = east).</param>
    public void NudgeMeters(double dNorthMeters, double dEastMeters)
    {
        double dLat = dNorthMeters / 110540.0;

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                Plot plot = Plots[row, col];
                double cosLat = Math.Cos(plot.SouthWest.Latitude * Math.PI / 180.0);
                double dLon = dEastMeters / (111320.0 * cosLat);

                Plots[row, col] = new Plot
                {
                    Row = plot.Row,
                    Column = plot.Column,
                    WidthMeters = plot.WidthMeters,
                    LengthMeters = plot.LengthMeters,
                    SouthWest = new GeoPoint(
                        plot.SouthWest.Latitude + dLat,
                        plot.SouthWest.Longitude + dLon),
                    NorthEast = new GeoPoint(
                        plot.NorthEast.Latitude + dLat,
                        plot.NorthEast.Longitude + dLon),
                };
            }
        }
    }
}
