using CommunityToolkit.Mvvm.ComponentModel;
using Mapsui;
using Mapsui.Tiling;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Mapsui.Projections;
using PlotManager.Core.Spatial;
using PlotManager.Core.Models;
using PlotManager.Core.Models.Trials;
using PlotManager.Core.Providers;
using PlotManager.Core.Services;
using System.Collections.Generic;

namespace PlotManager.Avalonia.ViewModels;

public partial class MapViewModel : ViewModelBase
{
    [ObservableProperty]
    private Map _map;

    private PlotManager.Core.Spatial.SpatialEngine _spatialEngine;
    private SimulationGpsProvider _gpsProvider;
    private Layer _tractorLayer;
    private SpeedometerViewModel _speedometer;
    private PneumaticRateController _rateController;

    public MapViewModel(SpeedometerViewModel speedometer)
    {
        _speedometer = speedometer;
        _rateController = new PneumaticRateController(5.0, 3.0); // 5 L/min flow, 3m width

        _map = new Map();
        _map.CRS = "EPSG:3857";
        _map.Layers.Add(OpenStreetMap.CreateTileLayer());
        
        _spatialEngine = new PlotManager.Core.Spatial.SpatialEngine();
        var trial = new SequentialTrial
        {
            PlotsCount = 20,
            PlotWidthMeters = 3.0,
            GapMeters = 1.0,
            PlotLengthMeters = 10.0
        };

        var p1 = new TrialProduct { Name = "Fertilizer A", ColorHex = "#FFFF0000" };
        var p2 = new TrialProduct { Name = "Fertilizer B", ColorHex = "#FF0000FF" };
        trial.ProductSequence.Add(p1); 
        trial.ProductSequence.Add(p2); 

        // 50.4501 N, 30.5234 E (Kyiv)
        _spatialEngine.InitializeTrialGrid(trial, new GeoPoint(50.4501, 30.5234), 45.0);

        UpdateTrialGridLayer();
        
        // Navigate to the start
        var center = SphericalMercator.FromLonLat(30.5234, 50.4501);
        _map.Navigator.CenterOnAndZoomTo(new MPoint(center.x, center.y), 1); // Resolution 1 is quite zoomed in

        // Initialize Tractor Layer
        _tractorLayer = new Layer("Tractor");
        _map.Layers.Add(_tractorLayer);

        // Start Simulation: Driving at 6 km/h, heading 45 degrees
        _gpsProvider = new SimulationGpsProvider(new GeoPoint(50.4501, 30.5234), 45.0, 6.0);
        _gpsProvider.OnPositionUpdate += OnGpsPositionUpdated;
        _gpsProvider.Connect();
    }

    private void OnGpsPositionUpdated(object? sender, GpsStateEventArgs e)
    {
        // Must run on UI thread if we manipulate UI-bound elements, but Mapsui rendering engine is thread-safe for DataHasChanged
        var mercator = SphericalMercator.FromLonLat(e.Position.Longitude, e.Position.Latitude);
        
        var pointFeature = new GeometryFeature(new NetTopologySuite.Geometries.Point(mercator.x, mercator.y));
        pointFeature.Styles.Add(new SymbolStyle 
        { 
            Fill = new Brush(Color.Red),
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 0.5 
        });

        _tractorLayer.DataSource = new Mapsui.Providers.MemoryProvider(new[] { pointFeature }) { CRS = "EPSG:3857" };
        _tractorLayer.DataHasChanged();

        // Update Spatial Engine (assuming boom is 2m behind antenna)
        var state = _spatialEngine.UpdatePosition(e.Position, e.SpeedKmh, e.HeadingDegrees, 0, -2.0, 300);
        
        // Pass state to Speedometer
        double targetSpeed = 0;
        if (state.IsInsidePlot && state.ActiveProduct != null)
        {
            // Calculate speed based on target rate (L/ha)
            targetSpeed = _rateController.CalculateTargetSpeedKmh(state.ActiveProduct.TargetRateLPerHa);
        }

        // Notify UI. Note: We use Avalonia's Dispatcher if we modify Observable collections, but SpeedometerViewModel ObservableProperty runs on UI thread internally if required?
        // Actually ObservableProperty doesn't automatically dispatch. Let's send updates directly; if Avalonia complains, we add Dispatcher.UIThread.
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => 
        {
            _speedometer.Update(targetSpeed, e.SpeedKmh);
        });
    }

    private void UpdateTrialGridLayer()
    {
        var features = new List<GeometryFeature>();
        foreach (var plotGeo in _spatialEngine.GetTrialGrid())
        {
            // Project Polygon to WebMercator (3857) since NTS generates WGS84 (4326)
            var projectedGeom = plotGeo.Polygon.Copy();
            projectedGeom.Apply(new DistanceToMercatorFilter());

            var feature = new GeometryFeature(projectedGeom);
            
            var color = Color.Gray;
            if (plotGeo.Product != null && !string.IsNullOrEmpty(plotGeo.Product.ColorHex))
            {
                // Simple parsing for formats like #FF0000 or #FFFF0000
                string hex = plotGeo.Product.ColorHex.Trim('#');
                if (hex.Length == 6)
                {
                    color = new Color(
                        System.Convert.ToInt32(hex.Substring(0, 2), 16),
                        System.Convert.ToInt32(hex.Substring(2, 2), 16),
                        System.Convert.ToInt32(hex.Substring(4, 2), 16));
                }
                else if (hex.Length == 8)
                {
                    color = new Color(
                        System.Convert.ToInt32(hex.Substring(2, 2), 16),
                        System.Convert.ToInt32(hex.Substring(4, 2), 16),
                        System.Convert.ToInt32(hex.Substring(6, 2), 16),
                        System.Convert.ToInt32(hex.Substring(0, 2), 16));
                }
            }
            color = new Color(color.R, color.G, color.B, 128); // 50% transparency
            
            feature.Styles.Add(new VectorStyle
            {
                Fill = new Brush(color),
                Outline = new Pen(Color.Black, 1.0)
            });
            
            features.Add(feature);
        }

        var memoryProvider = new Mapsui.Providers.MemoryProvider(features) { CRS = "EPSG:3857" };
        var layer = new Layer("TrialGrid")
        {
            DataSource = memoryProvider,
            Style = null
        };

        _map.Layers.Add(layer);
    }
    
    // Simple filter to project NTS Geometries to WebMercator directly
    private class DistanceToMercatorFilter : NetTopologySuite.Geometries.ICoordinateSequenceFilter
    {
        public bool Done => false;
        public bool GeometryChanged => true;

        public void Filter(NetTopologySuite.Geometries.CoordinateSequence seq, int i)
        {
            var lon = seq.GetX(i);
            var lat = seq.GetY(i);
            var mercator = SphericalMercator.FromLonLat(lon, lat);
            seq.SetX(i, mercator.x);
            seq.SetY(i, mercator.y);
        }
    }
}
