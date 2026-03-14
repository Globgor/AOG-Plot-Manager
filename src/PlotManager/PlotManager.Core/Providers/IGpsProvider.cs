using System;
using PlotManager.Core.Models;

namespace PlotManager.Core.Providers;

public class GpsStateEventArgs : EventArgs
{
    public GeoPoint Position { get; set; }
    public double SpeedKmh { get; set; }
    public double HeadingDegrees { get; set; }
}

public interface IGpsProvider
{
    event EventHandler<GpsStateEventArgs> OnPositionUpdate;
    bool IsConnected { get; }
    void Connect();
    void Disconnect();
}
