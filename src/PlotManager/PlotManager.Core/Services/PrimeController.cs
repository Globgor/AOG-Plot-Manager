namespace PlotManager.Core.Services;

using PlotManager.Core.Protocol;

/// <summary>
/// Controls the Prime/Flush manual override.
/// Safety rules:
///   - Cannot prime if tractor is moving faster than MaxPrimeSpeedKmh (0.5 km/h default)
///   - Cannot prime if PlotMode is active (avoid accidental spray on trial plots)
///   - Sends all-14-valves-OPEN while priming, all-OFF when released
/// </summary>
public class PrimeController
{
    private ITransport? _transport;
    private bool _priming;

    /// <summary>Maximum allowed speed for priming (km/h). Safety threshold.</summary>
    public double MaxPrimeSpeedKmh { get; set; } = 0.5;

    /// <summary>Whether priming is currently active.</summary>
    public bool IsPriming => _priming;

    /// <summary>Fires when prime state changes (for UI indicator).</summary>
    public event Action<bool>? OnPrimeStateChanged;

    /// <summary>Fires on validation errors (for UI display).</summary>
    public event Action<string>? OnPrimeError;

    /// <summary>
    /// Sets the transport layer for sending valve commands.
    /// </summary>
    public void SetTransport(ITransport transport)
    {
        _transport = transport;
    }

    /// <summary>
    /// Attempts to start priming (open all 14 valves).
    /// Returns false if safety checks fail.
    /// </summary>
    /// <param name="currentSpeedKmh">Current tractor speed.</param>
    /// <param name="plotModeActive">Whether Plot Mode is currently enabled.</param>
    /// <returns>True if priming started successfully.</returns>
    public bool StartPrime(double currentSpeedKmh, bool plotModeActive)
    {
        if (_priming) return true; // Already priming

        // Safety checks
        if (currentSpeedKmh > MaxPrimeSpeedKmh)
        {
            OnPrimeError?.Invoke(
                $"Cannot prime while moving ({currentSpeedKmh:F1} km/h). " +
                $"Stop the tractor (max {MaxPrimeSpeedKmh:F1} km/h).");
            return false;
        }

        if (plotModeActive)
        {
            OnPrimeError?.Invoke(
                "Cannot prime while Plot Mode is active. " +
                "Disable Plot Mode first to avoid accidental spray on trial plots.");
            return false;
        }

        _priming = true;
        SendValveMask(0x3FFF); // All 14 valves OPEN
        OnPrimeStateChanged?.Invoke(true);
        return true;
    }

    /// <summary>
    /// Stops priming (close all valves).
    /// </summary>
    public void StopPrime()
    {
        if (!_priming) return;

        _priming = false;
        SendValveMask(0x0000); // All valves CLOSED
        OnPrimeStateChanged?.Invoke(false);
    }

    /// <summary>
    /// Emergency release — forces stop regardless of state.
    /// </summary>
    public void ForceStop()
    {
        _priming = false;
        SendValveMask(0x0000);
        OnPrimeStateChanged?.Invoke(false);
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
            OnPrimeError?.Invoke($"Failed to send prime command: {ex.Message}");
        }
    }
}
