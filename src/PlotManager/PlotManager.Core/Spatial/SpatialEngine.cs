using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using PlotManager.Core.Models;
using PlotManager.Core.Models.Trials;

namespace PlotManager.Core.Spatial;

/// <summary>
/// The core component for evaluating complex geometries in real time (the 'Spatial Brain').
/// It models the trial field into physical WGS84 polygons (NetTopologySuite) and provides
/// 10Hz/5Hz evaluations of whether the applicator nozzle intersects an active area.
/// </summary>
public class SpatialEngine
{
    public class PlotGeo
    {
        public Polygon Polygon { get; set; }
        public TrialProduct? Product { get; set; }
        public int PlotIndex { get; set; }
        
        public PlotGeo(Polygon poly, TrialProduct? prod, int index)
        {
            Polygon = poly;
            Product = prod;
            PlotIndex = index;
        }
    }

    private readonly GeometryFactory _geometryFactory;
    private readonly List<PlotGeo> _trialGrid = new();

    public SpatialEngine()
    {
        // 4326 is the standard SRID for WGS84 GPS coordinates (Latitude/Longitude)
        _geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
    }

    public IReadOnlyList<PlotGeo> GetTrialGrid() => _trialGrid.AsReadOnly();

    /// <summary>
    /// Generates physical WGS84 polygons for the trial defined by ITrialDesign.
    /// Starts at 'startPoint' and runs along 'headingDegrees'.
    /// </summary>
    public void InitializeTrialGrid(ITrialDesign trial, GeoPoint startPoint, double headingDegrees)
    {
        _trialGrid.Clear();
        
        if (trial is SequentialTrial seq)
        {
            GenerateSequentialGrid(seq, startPoint, headingDegrees);
        }
        else
        {
            // Complex grid generations to be implemented later (Matrix parsing)
            throw new NotImplementedException("Grid generation for complex trials is not yet implemented.");
        }
    }

    private void GenerateSequentialGrid(SequentialTrial trial, GeoPoint startPoint, double headingDegrees)
    {
        // The trial acts as a linear band starting at startPoint.
        // We calculate vertices using GeoMath spherical approximations.
        GeoPoint currentCenter = startPoint;
        double halfWidth = trial.PlotWidthMeters / 2.0;
        
        // 90 degrees offset for lateral bounds relative to tractor forward vector
        double rightBearing = (headingDegrees + 90.0) % 360.0;
        double leftBearing = (headingDegrees + 270.0) % 360.0;
        
        for (int i = 0; i < trial.PlotsCount; i++)
        {
            double plotLen = trial.GetPlotLengthMeters(i);
            GeoPoint plotStart = currentCenter;
            GeoPoint plotEnd = GeoMath.CalculateDestination(plotStart, plotLen, headingDegrees);

            // Calculate 4 corners of the polygon
            GeoPoint bl = GeoMath.CalculateDestination(plotStart, halfWidth, leftBearing);
            GeoPoint br = GeoMath.CalculateDestination(plotStart, halfWidth, rightBearing);
            GeoPoint tl = GeoMath.CalculateDestination(plotEnd, halfWidth, leftBearing);
            GeoPoint tr = GeoMath.CalculateDestination(plotEnd, halfWidth, rightBearing);

            // NTS Polygons must be closed (first point == last point)
            var coordinates = new[]
            {
                new Coordinate(bl.Longitude, bl.Latitude),
                new Coordinate(br.Longitude, br.Latitude),
                new Coordinate(tr.Longitude, tr.Latitude),
                new Coordinate(tl.Longitude, tl.Latitude),
                new Coordinate(bl.Longitude, bl.Latitude) // Close linear ring
            };

            var ring = _geometryFactory.CreateLinearRing(coordinates);
            var polygon = _geometryFactory.CreatePolygon(ring);

            _trialGrid.Add(new PlotGeo(polygon, trial.GetProductForPlot(i), i));

            // Move the current reference point to the start of the next plot (End of current plot + Gap)
            GeoPoint postGapStart = GeoMath.CalculateDestination(plotEnd, trial.GapMeters, headingDegrees);
            currentCenter = postGapStart;
        }
    }

    /// <summary>
    /// Evaluates whether the future predicted position (LookAhead) enters a plot.
    /// Call this when new GPS positions arrive from the sensor layer.
    /// </summary>
    public SpatialState UpdatePosition(GeoPoint antennaLocation, double speedKmh, double headingDegrees, 
                                       double boomXOffsetMeters, double boomYOffsetMeters, double latencyMs)
    {
        if (_trialGrid.Count == 0) 
            return new SpatialState { IsInsidePlot = false };

        // 1. Calculate physical boom position
        var boomCenter = GeoMath.CalculateBoomPosition(antennaLocation, boomXOffsetMeters, boomYOffsetMeters, headingDegrees);

        // 2. Calculate point of application based on internal delay (LookAhead point)
        var lookAheadPoint = GeoMath.CalculateLookAhead(boomCenter, speedKmh, latencyMs, headingDegrees);
        
        // 3. Convert to NTS Point for spatial intersection
        var ntsPoint = _geometryFactory.CreatePoint(new Coordinate(lookAheadPoint.Longitude, lookAheadPoint.Latitude));

        // 4. Test Polygon.Contains
        // Note: For massive trials, replace LINQ FirstOrDefault with a Spatial Index (e.g., Quadtree or STRtree).
        // For a single strip (Sequential), this O(N) linear scan is sufficiently fast (<1ms).
        var activePlot = _trialGrid.FirstOrDefault(p => p.Polygon.Contains(ntsPoint));

        if (activePlot != null)
        {
            return new SpatialState
            {
                IsInsidePlot = true,
                ActivePlotIndex = activePlot.PlotIndex,
                ActiveProduct = activePlot.Product
            };
        }

        // We are flying over a gap or completely outside the trial area
        return new SpatialState
        {
            IsInsidePlot = false
        };
    }
}
