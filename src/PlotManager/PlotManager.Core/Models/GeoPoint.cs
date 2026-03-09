namespace PlotManager.Core.Models;

/// <summary>
/// Represents a WGS84 geographic coordinate with optional UTM projection.
/// </summary>
public readonly struct GeoPoint
{
    /// <summary>Latitude in decimal degrees (WGS84).</summary>
    public double Latitude { get; }

    /// <summary>Longitude in decimal degrees (WGS84).</summary>
    public double Longitude { get; }

    /// <summary>Easting in meters (UTM). Computed lazily.</summary>
    public double Easting { get; }

    /// <summary>Northing in meters (UTM). Computed lazily.</summary>
    public double Northing { get; }

    /// <summary>UTM zone number (1-60).</summary>
    public int UtmZone { get; }

    public GeoPoint(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;

        // Compute UTM zone from longitude
        UtmZone = (int)Math.Floor((longitude + 180.0) / 6.0) + 1;

        // Simplified UTM projection (Transverse Mercator)
        // For production use, replace with a proper geodetic library
        (Easting, Northing) = ToUtm(latitude, longitude, UtmZone);
    }

    /// <summary>
    /// Calculates the distance in meters between two GeoPoints using the Haversine formula.
    /// </summary>
    public double DistanceTo(GeoPoint other)
    {
        const double R = 6371000.0; // Earth radius in meters
        double dLat = ToRadians(other.Latitude - Latitude);
        double dLon = ToRadians(other.Longitude - Longitude);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(ToRadians(Latitude)) * Math.Cos(ToRadians(other.Latitude)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    public override string ToString() => $"({Latitude:F6}, {Longitude:F6})";

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static (double easting, double northing) ToUtm(double lat, double lon, int zone)
    {
        // Simplified UTM conversion stub
        // TODO: Replace with proper Transverse Mercator projection (e.g., ProjNET or DotSpatial)
        double centralMeridian = (zone - 1) * 6.0 - 180.0 + 3.0;
        double dLon = lon - centralMeridian;

        double latRad = ToRadians(lat);
        double cosLat = Math.Cos(latRad);

        // Approximate easting and northing (good enough for initial development)
        double easting = 500000.0 + dLon * 111320.0 * cosLat;
        double northing = lat * 110540.0;
        if (lat < 0) northing += 10000000.0;

        return (easting, northing);
    }
}
