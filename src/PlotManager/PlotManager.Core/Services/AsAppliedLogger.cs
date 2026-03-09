using System.Globalization;
using System.Text;

namespace PlotManager.Core.Services;

/// <summary>
/// Background CSV logger for As-Applied records.
/// Records every spray event for scientific reproducibility.
///
/// CSV columns: Timestamp, Latitude, Longitude, PlotId, Product, SpeedKmh, ValveMask, Notes,
///              Air_Bar, Flow_1_Lpm, Flow_2_Lpm, ..., Flow_10_Lpm
/// </summary>
public class AsAppliedLogger : IDisposable
{
    private StreamWriter? _writer;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>Path to the current log file.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Number of records written.</summary>
    public long RecordCount { get; private set; }

    /// <summary>
    /// Starts a new logging session. Creates the CSV file with headers.
    /// </summary>
    /// <param name="directory">Directory to write the log file into.</param>
    /// <param name="trialName">Trial name for the filename.</param>
    public void StartSession(string directory, string trialName)
    {
        if (_writer != null)
            throw new InvalidOperationException("Session already active. Call StopSession() first.");

        Directory.CreateDirectory(directory);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string safeName = SanitizeFilename(trialName);
        FilePath = Path.Combine(directory, $"as_applied_{safeName}_{timestamp}.csv");

        _writer = new StreamWriter(FilePath, false, Encoding.UTF8)
        {
            AutoFlush = true,
        };

        // Build header with optional sensor columns
        var header = "Timestamp,Latitude,Longitude,PlotId,Product,SpeedKmh,ValveMask,Notes";
        header += ",Air_Bar";
        for (int i = 1; i <= 10; i++)
        {
            header += $",Flow_{i}_Lpm";
        }
        _writer.WriteLine(header);
        RecordCount = 0;
    }

    /// <summary>
    /// Logs a single spray event record.
    /// </summary>
    public void LogRecord(
        DateTime timestamp,
        double latitude,
        double longitude,
        string? plotId,
        string? product,
        double speedKmh,
        ushort valveMask,
        string? notes = null)
    {
        if (_disposed || _writer == null) return;

        lock (_lock)
        {
            string line = string.Join(",",
                timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
                latitude.ToString("F8", CultureInfo.InvariantCulture),
                longitude.ToString("F8", CultureInfo.InvariantCulture),
                plotId ?? "BUFFER",
                product ?? "NONE",
                speedKmh.ToString("F2", CultureInfo.InvariantCulture),
                $"0x{valveMask:X4}",
                EscapeCsv(notes ?? string.Empty));

            _writer.WriteLine(line);
            RecordCount++;
        }
    }

    /// <summary>
    /// Logs a record with sensor telemetry data (11 extra columns).
    /// </summary>
    public void LogRecordWithSensors(
        DateTime timestamp,
        double latitude,
        double longitude,
        string? plotId,
        string? product,
        double speedKmh,
        ushort valveMask,
        double? airPressureBar,
        double[]? flowRatesLpm,
        string? notes = null)
    {
        if (_disposed || _writer == null) return;

        lock (_lock)
        {
            string baseLine = string.Join(",",
                timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
                latitude.ToString("F8", CultureInfo.InvariantCulture),
                longitude.ToString("F8", CultureInfo.InvariantCulture),
                plotId ?? "BUFFER",
                product ?? "NONE",
                speedKmh.ToString("F2", CultureInfo.InvariantCulture),
                $"0x{valveMask:X4}",
                EscapeCsv(notes ?? string.Empty));

            // Sensor columns: Air_Bar + 10x Flow_Lpm
            var sb = new StringBuilder(baseLine);
            sb.Append(',');
            sb.Append(airPressureBar.HasValue
                ? airPressureBar.Value.ToString("F2", CultureInfo.InvariantCulture)
                : "NaN");

            for (int i = 0; i < 10; i++)
            {
                sb.Append(',');
                if (flowRatesLpm != null && i < flowRatesLpm.Length)
                    sb.Append(flowRatesLpm[i].ToString("F3", CultureInfo.InvariantCulture));
                else
                    sb.Append("NaN");
            }

            _writer.WriteLine(sb.ToString());
            RecordCount++;
        }
    }

    /// <summary>
    /// Logs a meteo check record at the beginning of a session.
    /// </summary>
    public void LogMeteoCheck(
        double temperatureC,
        double humidityPercent,
        double windSpeedMs,
        string? windDirection = null)
    {
        if (_disposed || _writer == null) return;

        lock (_lock)
        {
            string notes = $"METEO: Temp={temperatureC:F1}°C Humidity={humidityPercent:F0}% " +
                           $"Wind={windSpeedMs:F1}m/s Dir={windDirection ?? "N/A"}";

            string line = string.Join(",",
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
                "0", "0", "SYSTEM", "METEO", "0", "0x0000", EscapeCsv(notes));

            _writer.WriteLine(line);
        }
    }

    /// <summary>
    /// Stops the current logging session and flushes all data.
    /// </summary>
    public void StopSession()
    {
        lock (_lock)
        {
            _writer?.Flush();
            _writer?.Close();
            _writer?.Dispose();
            _writer = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopSession();
        GC.SuppressFinalize(this);
    }

    private static string SanitizeFilename(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
