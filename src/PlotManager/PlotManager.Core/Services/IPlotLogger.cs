namespace PlotManager.Core.Services;

/// <summary>
/// Minimal structured logging interface for PlotManager services.
/// Implementations must be thread-safe — multiple services call concurrently.
///
/// Format convention: [ISO8601] [LEVEL] [SOURCE] Message
/// Source is the service name (e.g. "PlotMode", "SensorHub", "Interlocks").
/// </summary>
public interface IPlotLogger
{
    /// <summary>Informational message (GPS cycle, valve change, pass start).</summary>
    void Info(string source, string message);

    /// <summary>Warning (RTK degraded, speed near limit, stale telemetry).</summary>
    void Warn(string source, string message);

    /// <summary>Error (exception, communication failure, data loss).</summary>
    void Error(string source, string message, Exception? ex = null);
}
