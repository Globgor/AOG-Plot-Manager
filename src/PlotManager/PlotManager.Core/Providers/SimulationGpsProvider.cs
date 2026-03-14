using System;
using System.Threading;
using System.Threading.Tasks;
using PlotManager.Core.Models;

namespace PlotManager.Core.Providers;

/// <summary>
/// A simulated GPS provider for testing Plot Manager without field hardware.
/// Starts at a given point and drives straight at a given speed.
/// </summary>
public class SimulationGpsProvider : IGpsProvider
{
    public event EventHandler<GpsStateEventArgs>? OnPositionUpdate;

    private bool _isRunning;
    private Task? _simulationTask;
    private readonly CancellationTokenSource _cts = new();

    private GeoPoint _currentPosition;
    private double _headingDegrees;
    private double _speedKmh;

    public bool IsConnected => _isRunning;

    public SimulationGpsProvider(GeoPoint startPosition, double headingDegrees, double speedKmh)
    {
        _currentPosition = startPosition;
        _headingDegrees = headingDegrees;
        _speedKmh = speedKmh;
    }

    public void Connect()
    {
        if (_isRunning) return;
        _isRunning = true;
        
        _simulationTask = Task.Run(async () =>
        {
            // 10 Hz update rate (100ms)
            int delayMs = 100;
            
            while (_isRunning && !_cts.Token.IsCancellationRequested)
            {
                // speed km/h to m/s
                double speedMs = _speedKmh / 3.6;
                double distanceDriven = speedMs * (delayMs / 1000.0);

                // Update position using spherical approximation
                _currentPosition = Spatial.GeoMath.CalculateDestination(_currentPosition, distanceDriven, _headingDegrees);

                OnPositionUpdate?.Invoke(this, new GpsStateEventArgs
                {
                    Position = _currentPosition,
                    HeadingDegrees = _headingDegrees,
                    SpeedKmh = _speedKmh
                });

                await Task.Delay(delayMs, _cts.Token);
            }
        });
    }

    public void Disconnect()
    {
        _isRunning = false;
        _cts.Cancel();
        _simulationTask?.Wait(500);
    }
}
