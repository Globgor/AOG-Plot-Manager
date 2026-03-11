using System.Collections.Concurrent;
using System.Globalization;

namespace PlotManager.Core.Services;

/// <summary>
/// Thread-safe, file-backed structured logger for PlotManager.
///
/// Architecture mirrors AsAppliedLogger: ConcurrentQueue + background flush thread.
/// Log format: [ISO8601] [LEVEL] [SOURCE] Message
///
/// Usage:
///   var logger = new PlotLogger();
///   logger.StartSession("/logs", "trial_001");
///   logger.Info("PlotMode", "Valve mask changed: 0x000F → 0x003F");
///   logger.StopSession();
/// </summary>
public class PlotLogger : IPlotLogger, IDisposable
{
    private StreamWriter? _writer;
    private readonly ConcurrentQueue<string> _queue = new();
    private Thread? _flushThread;
    private volatile bool _disposed;
    private volatile bool _sessionActive;

    /// <summary>Path to the current log file.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Flush interval in milliseconds (default: 500ms — faster than CSV for diagnostics).</summary>
    public int FlushIntervalMs { get; set; } = 500;

    /// <summary>Total log entries written in current session.</summary>
    private long _entryCount;
    public long EntryCount => Interlocked.Read(ref _entryCount);

    // Lock to serialize DrainQueue calls (StopSession vs FlushLoop race)
    private readonly object _drainLock = new();

    /// <summary>
    /// Starts a new logging session. Creates the log file and launches the flush thread.
    /// </summary>
    /// <param name="directory">Directory for the log file.</param>
    /// <param name="sessionName">Session name (used in filename).</param>
    public void StartSession(string directory, string sessionName)
    {
        if (_sessionActive)
            throw new InvalidOperationException("Logger session already active. Call StopSession() first.");

        Directory.CreateDirectory(directory);

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string safeName = SanitizeFilename(sessionName);
        FilePath = Path.Combine(directory, $"plotmanager_{safeName}_{timestamp}.log");

        _writer = new StreamWriter(FilePath, false, new System.Text.UTF8Encoding(false));
        _writer.WriteLine($"# PlotManager Diagnostic Log — {sessionName}");
        _writer.WriteLine($"# Started: {DateTime.UtcNow:O}");
        _writer.Flush();

        Interlocked.Exchange(ref _entryCount, 0);
        _sessionActive = true;

        _flushThread = new Thread(FlushLoop)
        {
            IsBackground = true,
            Name = "PlotLogger-Flush",
            Priority = ThreadPriority.BelowNormal,
        };
        _flushThread.Start();
    }

    /// <summary>Stops the session, drains the queue, and closes the file.</summary>
    public void StopSession()
    {
        _sessionActive = false;
        _flushThread?.Join(timeout: TimeSpan.FromSeconds(3));
        _flushThread = null;

        DrainQueue();
        _writer?.Flush();
        _writer?.Close();
        _writer?.Dispose();
        _writer = null;
    }

    // ════════════════════════════════════════════════════════════════════
    // IPlotLogger implementation
    // ════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public void Info(string source, string message) => Enqueue("INFO", source, message);

    /// <inheritdoc/>
    public void Warn(string source, string message) => Enqueue("WARN", source, message);

    /// <inheritdoc/>
    public void Error(string source, string message, Exception? ex = null)
    {
        string entry = ex != null
            ? $"{message} | {ex.GetType().Name}: {ex.Message}"
            : message;
        Enqueue("ERROR", source, entry);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopSession();
        GC.SuppressFinalize(this);
    }

    // ════════════════════════════════════════════════════════════════════
    // Private
    // ════════════════════════════════════════════════════════════════════

    private void Enqueue(string level, string source, string message)
    {
        if (_disposed || !_sessionActive) return;

        string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
        _queue.Enqueue($"[{ts}] [{level}] [{source}] {message}");
        Interlocked.Increment(ref _entryCount);
    }

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

        lock (_drainLock) // Prevent concurrent DrainQueue from FlushLoop + StopSession
        {
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
            catch (IOException)
            {
                // Disk full or permission denied — can't log about logging failure.
                // Mark session inactive to stop further enqueuing.
                _sessionActive = false;
            }
        }
    }

    private static string SanitizeFilename(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.ToString();
    }
}
