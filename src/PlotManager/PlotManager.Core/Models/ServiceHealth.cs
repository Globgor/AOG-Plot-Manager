namespace PlotManager.Core.Models;

/// <summary>
/// Service health status for monitoring and UI display.
/// Each networked service (SensorHub, AogUdpClient) exposes a Health property.
/// </summary>
public enum ServiceHealth
{
    /// <summary>Service is operating normally, receiving data on schedule.</summary>
    Healthy,

    /// <summary>Service is running but experiencing intermittent errors.</summary>
    Degraded,

    /// <summary>Service has failed and is not processing data.</summary>
    Failed,
}
