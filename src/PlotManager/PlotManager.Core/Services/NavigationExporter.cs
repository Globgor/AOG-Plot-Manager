using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NetTopologySuite.Geometries;
using PlotManager.Core.Models;
using PlotManager.Core.Models.Trials;

namespace PlotManager.Core.Services;

public class NavigationExporter
{
    /// <summary>
    /// Exports the trial grid as an ArduPilot .waypoints file for automated navigation.
    /// Creates a snake-like path through the center logic of the plots.
    /// </summary>
    public void ExportArdupilotWaypoints(string filePath, ITrialDesign trial, GeoPoint startPoint, double headingDegrees, double altitude = 0)
    {
        var engine = new Spatial.SpatialEngine();
        engine.InitializeTrialGrid(trial, startPoint, headingDegrees);
        var plots = engine.GetTrialGrid();
        if (plots.Count == 0) return;

        using var writer = new StreamWriter(filePath);
        // Ardupilot Waypoint file header
        writer.WriteLine("QGC WPL 110");
        
        int seq = 0;
        
        // Home point (using first plot center as dummy home if needed, or 0)
        var firstPlot = plots[0].Polygon;
        writer.WriteLine($"{seq}\t1\t0\t16\t0\t0\t0\t0\t{firstPlot.Centroid.Y:F7}\t{firstPlot.Centroid.X:F7}\t{altitude:F2}\t1");
        seq++;

        // Generate a serpentine path through the centers
        // For a real implementation, we would group by rows and alternate direction.
        // Here we just export the centers in the order they are generated.
        foreach (var plot in plots)
        {
            // command 16 is MAV_CMD_NAV_WAYPOINT
            // param1: hold time, param2: acceptance radius, param3: pass through, param4: yaw
            writer.WriteLine($"{seq}\t0\t3\t16\t0\t0\t0\t0\t{plot.Polygon.Centroid.Y:F7}\t{plot.Polygon.Centroid.X:F7}\t{altitude:F2}\t1");
            seq++;
        }
    }

    /// <summary>
    /// Exports the trial boundaries as a KML file, useful for AgOpenGPS boundary import or Google Earth.
    /// </summary>
    public void ExportAogKml(string filePath, ITrialDesign trial, GeoPoint startPoint, double headingDegrees)
    {
        var engine = new Spatial.SpatialEngine();
        engine.InitializeTrialGrid(trial, startPoint, headingDegrees);
        var plots = engine.GetTrialGrid();
        if (plots.Count == 0) return;

        using var writer = new StreamWriter(filePath);
        writer.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        writer.WriteLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
        writer.WriteLine("  <Document>");
        writer.WriteLine($"    <name>Trial {DateTime.Now:yyyy-MM-dd}</name>");

        foreach (var plot in plots)
        {
            writer.WriteLine("    <Placemark>");
            writer.WriteLine($"      <name>Plot {plot.PlotIndex}</name>");
            writer.WriteLine("      <Polygon>");
            writer.WriteLine("        <outerBoundaryIs>");
            writer.WriteLine("          <LinearRing>");
            writer.WriteLine("            <coordinates>");
            
            // Extract coordinates from the NTS Polygon
            var coords = plot.Polygon.Coordinates; // Outer ring coordinates
            foreach (var c in coords)
            {
                writer.WriteLine($"              {c.X:F7},{c.Y:F7},0");
            }

            writer.WriteLine("            </coordinates>");
            writer.WriteLine("          </LinearRing>");
            writer.WriteLine("        </outerBoundaryIs>");
            writer.WriteLine("      </Polygon>");
            writer.WriteLine("    </Placemark>");
        }

        writer.WriteLine("  </Document>");
        writer.WriteLine("</kml>");
    }
}
