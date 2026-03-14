using System;
using PlotManager.Core.Models;

namespace PlotManager.Core.Spatial;

/// <summary>
/// Geospatial mathematics for translating coordinates and calculating offsets based on WGS84.
/// Uses spherical Earth approximation (Haversine/Vincenty simplified) which is highly accurate
/// for short distances (< 1km) typical in agricultural trials.
/// </summary>
public static class GeoMath
{
    private const double EarthRadiusMeters = 6371000.0;

    /// <summary>
    /// Calculates a new coordinate by moving a given distance (meters) along a specific bearing (degrees).
    /// </summary>
    public static GeoPoint CalculateDestination(GeoPoint start, double distanceMeters, double bearingDegrees)
    {
        double lat1 = ToRadians(start.Latitude);
        double lon1 = ToRadians(start.Longitude);
        double bearingRad = ToRadians(bearingDegrees);
        double angularDistance = distanceMeters / EarthRadiusMeters;

        double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(angularDistance) +
                                Math.Cos(lat1) * Math.Sin(angularDistance) * Math.Cos(bearingRad));

        double lon2 = lon1 + Math.Atan2(Math.Sin(bearingRad) * Math.Sin(angularDistance) * Math.Cos(lat1),
                                        Math.Cos(angularDistance) - Math.Sin(lat1) * Math.Sin(lat2));

        return new GeoPoint(ToDegrees(lat2), ToDegrees(lon2));
    }

    /// <summary>
    /// Calculates the boom center coordinate given the antenna's coordinate, heading, and physical offsets.
    /// Positive Y is forward, negative Y is backward.
    /// Positive X is right, negative X is left.
    /// </summary>
    public static GeoPoint CalculateBoomPosition(GeoPoint antenna, double xOffsetMeters, double yOffsetMeters, double headingDegrees)
    {
        // Vector addition on a 2D plane (local tangent to the Earth's surface)
        double distance = Math.Sqrt(xOffsetMeters * xOffsetMeters + yOffsetMeters * yOffsetMeters);
        
        if (distance == 0) return antenna;

        // Calculate the angle relative to the tractor's forward direction
        // Atan2(X, Y) where Y is forward and X is right gives local bearing clockwise from forward vector.
        double localBearing = ToDegrees(Math.Atan2(xOffsetMeters, yOffsetMeters));
        
        double absoluteBearing = (headingDegrees + localBearing) % 360.0;
        if (absoluteBearing < 0) absoluteBearing += 360.0;

        return CalculateDestination(antenna, distance, absoluteBearing);
    }

    /// <summary>
    /// Calculates the LookAhead point projecting the boom center forward
    /// based on current speed and system latency (e.g. valve deadtime + air travel time).
    /// </summary>
    public static GeoPoint CalculateLookAhead(GeoPoint boomCenter, double speedKmh, double latencyMs, double headingDegrees)
    {
        if (speedKmh <= 0.1) return boomCenter;

        // Convert km/h to m/s
        double speedMs = speedKmh * (1000.0 / 3600.0);
        
        // Calculate distance traveled during latency
        double latencySeconds = latencyMs / 1000.0;
        double lookAheadDistanceMeters = speedMs * latencySeconds;

        return CalculateDestination(boomCenter, lookAheadDistanceMeters, headingDegrees);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
    private static double ToDegrees(double radians) => radians * 180.0 / Math.PI;
}
