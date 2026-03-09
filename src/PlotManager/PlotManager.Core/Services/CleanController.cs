using System.Timers;

namespace PlotManager.Core.Services;

using PlotManager.Core.Protocol;

/// <summary>
/// Pulse-clean controller for purging chemical residue from booms.
/// Opens selected boom valves in a pulsed pattern (ON/OFF cycles)
/// to flush lines with water or air before switching products.
///
/// Safety: blocked at speeds > 0.5 km/h.
/// </summary>
public class CleanController : IDisposable
{
    private System.Timers.Timer? _pulseTimer;
    private ITransport? _transport;
    private bool _valvesOpen;
    private int _cyclesRemaining;
    private ushort _cleanMask;
    private bool _disposed;

    /// <summary>Duration of valve-open pulse in milliseconds (default: 2000ms).</summary>
    public int PulseOnMs { get; set; } = 2000;

    /// <summary>Duration of valve-closed pause in milliseconds (default: 1000ms).</summary>
    public int PulseOffMs { get; set; } = 1000;

    /// <summary>Number of clean cycles per run (default: 3).</summary>
    public int CycleCount { get; set; } = 3;

    /// <summary>Maximum speed for cleaning (km/h).</summary>
    public double MaxCleanSpeedKmh { get; set; } = 0.5;

    /// <summary>Whether a clean cycle is currently running.</summary>
    public bool IsCleaning => _pulseTimer != null && _pulseTimer.Enabled;

    /// <summary>Cycles remaining in the current run.</summary>
    public int RemainingCycles => _cyclesRemaining;

    /// <summary>Fires on clean state changes (for UI).</summary>
    public event Action<bool, int>? OnCleanStateChanged; // (isCleaning, cyclesRemaining)

    /// <summary>Fires on errors.</summary>
    public event Action<string>? OnCleanError;

    /// <summary>
    /// Sets the transport layer for sending valve commands.
    /// </summary>
    public void SetTransport(ITransport transport)
    {
        _transport = transport;
    }

    /// <summary>
    /// Starts a pulse-clean cycle for the specified boom valve channels.
    /// </summary>
    /// <param name="boomChannels">Valve channels to clean (0-based).</param>
    /// <param name="currentSpeedKmh">Current tractor speed.</param>
    /// <param name="plotModeActive">Whether Plot Mode is active.</param>
    /// <returns>True if cleaning started.</returns>
    public bool StartClean(IEnumerable<int> boomChannels, double currentSpeedKmh, bool plotModeActive)
    {
        if (IsCleaning) return false;

        if (currentSpeedKmh > MaxCleanSpeedKmh)
        {
            OnCleanError?.Invoke(
                $"Cannot clean while moving ({currentSpeedKmh:F1} km/h). " +
                $"Stop the tractor (max {MaxCleanSpeedKmh:F1} km/h).");
            return false;
        }

        if (plotModeActive)
        {
            OnCleanError?.Invoke("Cannot clean while Plot Mode is active.");
            return false;
        }

        // Build mask from channels
        _cleanMask = 0;
        foreach (int ch in boomChannels)
        {
            if (ch >= 0 && ch < 14)
                _cleanMask |= (ushort)(1 << ch);
        }

        if (_cleanMask == 0)
        {
            OnCleanError?.Invoke("No boom channels selected for cleaning.");
            return false;
        }

        _cyclesRemaining = CycleCount;
        _valvesOpen = false;

        // Start with ON phase
        StartOnPhase();

        return true;
    }

    /// <summary>
    /// Stops the clean cycle immediately.
    /// </summary>
    public void StopClean()
    {
        _pulseTimer?.Stop();
        _pulseTimer?.Dispose();
        _pulseTimer = null;
        _cyclesRemaining = 0;
        _valvesOpen = false;
        SendValveMask(0);
        OnCleanStateChanged?.Invoke(false, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopClean();
        GC.SuppressFinalize(this);
    }

    private void StartOnPhase()
    {
        _valvesOpen = true;
        SendValveMask(_cleanMask);
        OnCleanStateChanged?.Invoke(true, _cyclesRemaining);

        _pulseTimer?.Dispose();
        _pulseTimer = new System.Timers.Timer(PulseOnMs);
        _pulseTimer.Elapsed += OnPulseTimerElapsed;
        _pulseTimer.AutoReset = false;
        _pulseTimer.Start();
    }

    private void StartOffPhase()
    {
        _valvesOpen = false;
        SendValveMask(0);

        _cyclesRemaining--;
        OnCleanStateChanged?.Invoke(true, _cyclesRemaining);

        if (_cyclesRemaining <= 0)
        {
            // All cycles complete
            StopClean();
            return;
        }

        _pulseTimer?.Dispose();
        _pulseTimer = new System.Timers.Timer(PulseOffMs);
        _pulseTimer.Elapsed += OnPulseTimerElapsed;
        _pulseTimer.AutoReset = false;
        _pulseTimer.Start();
    }

    private void OnPulseTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_disposed) return;

        if (_valvesOpen)
            StartOffPhase();
        else
            StartOnPhase();
    }

    private void SendValveMask(ushort mask)
    {
        if (_transport == null) return;
        try
        {
            byte[] packet = PlotProtocol.BuildSetValves(mask);
            _transport.SendAsync(packet).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnCleanError?.Invoke($"Failed to send clean command: {ex.Message}");
        }
    }
}
