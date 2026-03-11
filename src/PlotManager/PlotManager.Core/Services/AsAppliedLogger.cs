using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PlotManager.Core.Services;

/// <summary>
/// Background CSV logger for As-Applied records.
/// Records every spray event for scientific reproducibility.
///
/// User #5 Hardening: Uses a ConcurrentQueue + background flush thread.
/// The main/telemetry thread only enqueues pre-formatted CSV lines (never blocks on I/O).
/// A dedicated writer thread wakes every 1 second, drains the queue, and writes
/// the batch to disk in a single operation, then flushes.
///
/// CSV columns: Timestamp, Latitude, Longitude, PlotId, Product, SpeedKmh, ValveMask, Notes,
///              Air_Bar, Flow_1_Lpm, Flow_2_Lpm, ..., Flow_10_Lpm
/// </summary>
public class AsAppliedLogger : IDisposable
{
    private StreamWriter? _writer;
    private readonly ConcurrentQueue<string> _queue = new();
    private Thread? _flushThread;
    private volatile bool _disposed;
    private volatile bool _sessionActive;

    /// <summary>Path to the current log file.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Number of records written (approximate — incremented on enqueue).</summary>
    public long RecordCount { get; private set; }

    /// <summary>Flush interval for the background writer (default: 1000ms).</summary>
    public int FlushIntervalMs { get; set; } = 1000;

    /// <summary>Fires when a background flush fails (disk full, permission denied).</summary>
    public event Action<string>? OnFlushError;

    /// <summary>
    /// Starts a new logging session. Creates the CSV file with headers.
    /// Launches the background flush thread.
    /// </summary>
    /// <param name="directory">Directory to write the log file into.</param>
    /// <param name="trialName">Trial name for the filename.</param>
    public void StartSession(string directory, string trialName)
    {
        if (_sessionActive)
            throw new InvalidOperationException("Session already active. Call StopSession() first.");

        Directory.CreateDirectory(directory);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string safeName = SanitizeFilename(trialName);
        FilePath = Path.Combine(directory, $"as_applied_{safeName}_{timestamp}.csv");

        _writer = new StreamWriter(FilePath, false, Encoding.UTF8);

        // Write header directly (before starting background thread)
        var header = "Timestamp,Latitude,Longitude,PlotId,Product,SpeedKmh,ValveMask,Notes";
        header += ",Air_Bar";
        for (int i = 1; i <= 10; i++)
        {
            header += $",Flow_{i}_Lpm";
        }
        // D1 FIX: GEP-required columns for scientific reporting
        header += ",FixQuality,HeadingDeg,OffReason";
        _writer.WriteLine(header);
        _writer.Flush();

        RecordCount = 0;
        _sessionActive = true;

        // Start background flush thread
        _flushThread = new Thread(FlushLoop)
        {
            IsBackground = true,
            Name = "AsAppliedLogger-Flush",
            Priority = ThreadPriority.BelowNormal,
        };
        _flushThread.Start();
    }

    /// <summary>
    /// Logs a single spray event record (no sensor data — pads with NaN).
    /// Non-blocking: enqueues a pre-formatted CSV line.
    /// </summary>
    public void LogRecord(
        DateTime timestamp,
        double latitude,
        double longitude,
        string? plotId,
        string? product,
        double speedKmh,
        ushort valveMask,
        string? notes = null,
        string? fixQuality = null,
        double? headingDeg = null,
        string? offReason = null)
    {
        if (_disposed || !_sessionActive) return;

        string line = string.Join(",",
            timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
            latitude.ToString("F8", CultureInfo.InvariantCulture),
            longitude.ToString("F8", CultureInfo.InvariantCulture),
            plotId ?? "BUFFER",
            product ?? "NONE",
            speedKmh.ToString("F2", CultureInfo.InvariantCulture),
            $"0x{valveMask:X4}",
            EscapeCsv(notes ?? string.Empty));

        // Pad sensor columns (Air_Bar + 10×Flow) with NaN to match header
        line += ",NaN";
        for (int i = 0; i < 10; i++) line += ",NaN";

        // D1 FIX: GEP columns
        line += $",{fixQuality ?? ""}";
        line += $",{(headingDeg.HasValue ? headingDeg.Value.ToString("F1", CultureInfo.InvariantCulture) : "")}";
        line += $",{offReason ?? ""}";

        _queue.Enqueue(line);
        RecordCount++;
    }

    /// <summary>
    /// Logs a record with sensor telemetry data (11 extra columns).
    /// Non-blocking: enqueues a pre-formatted CSV line.
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
        string? notes = null,
        string? fixQuality = null,
        double? headingDeg = null,
        string? offReason = null)
    {
        if (_disposed || !_sessionActive) return;

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

        // D1 FIX: GEP columns
        sb.Append(',');
        sb.Append(fixQuality ?? "");
        sb.Append(',');
        sb.Append(headingDeg.HasValue ? headingDeg.Value.ToString("F1", CultureInfo.InvariantCulture) : "");
        sb.Append(',');
        sb.Append(offReason ?? "");

        _queue.Enqueue(sb.ToString());
        RecordCount++;
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
        if (_disposed || !_sessionActive) return;

        string notes = $"METEO: Temp={temperatureC:F1}°C Humidity={humidityPercent:F0}% " +
                       $"Wind={windSpeedMs:F1}m/s Dir={windDirection ?? "N/A"}";

        string line = string.Join(",",
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
            "0", "0", "SYSTEM", "METEO", "0", "0x0000", EscapeCsv(notes));

        // L2 FIX: Pad sensor columns to match 19-column header
        line += ",NaN";
        for (int i = 0; i < 10; i++) line += ",NaN";

        _queue.Enqueue(line);
    }

    /// <summary>
    /// Stops the current logging session. Drains the queue and flushes all data.
    /// </summary>
    public void StopSession()
    {
        _sessionActive = false;

        // Wait for background thread to finish
        _flushThread?.Join(timeout: TimeSpan.FromSeconds(5));
        _flushThread = null;

        // Final drain of any remaining items
        DrainQueue();

        // S4 FIX: Append SHA256 hash footer for data integrity verification
        if (_writer != null && FilePath != null)
        {
            _writer.Flush();
            try
            {
                // Compute hash of everything written so far
                _writer.Close();
                _writer.Dispose();
                byte[] fileBytes = File.ReadAllBytes(FilePath);
                byte[] hashBytes = SHA256.HashData(fileBytes);
                string hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

                // Re-open in append mode to write hash footer
                using var appendWriter = new StreamWriter(FilePath, true, Encoding.UTF8);
                appendWriter.WriteLine($"# SHA256: {hashHex}");
            }
            catch (IOException)
            {
                // If we can't write the hash, don't fail — data is already saved
            }
            _writer = null;
        }
        else
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

    // ════════════════════════════════════════════════════════════════════
    // Background flush thread
    // ════════════════════════════════════════════════════════════════════

    private void FlushLoop()
    {
        while (_sessionActive && !_disposed)
        {
            Thread.Sleep(FlushIntervalMs);
            DrainQueue();
        }
    }

    private void DrainQueue()
    {
        if (_writer == null) return;

        try
        {
            int count = 0;
            while (_queue.TryDequeue(out string? line))
            {
                _writer.WriteLine(line);
                count++;
            }

            if (count > 0)
            {
                _writer.Flush();
            }
        }
        catch (IOException ex)
        {
            // R1 FIX: Don't crash on disk full / permission denied
            OnFlushError?.Invoke($"CSV flush failed: {ex.Message}");
        }
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
