using PlotManager.Core.Models;

namespace PlotManager.Core.Services;

/// <summary>
/// Controls section activation with hard cut-off logic.
/// Overrides standard AOG section control with precise boundary enforcement.
/// </summary>
public class SectionController
{
    private readonly IPlotLogger? _logger;

    /// <summary>
    /// Creates a SectionController with optional structured logging.
    /// </summary>
    public SectionController(IPlotLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>Whether speed interlock is currently blocking spray.</summary>
    public bool SpeedInterlockActive { get; private set; }

    /// <summary>Whether the RTK quality loss interlock is active.</summary>
    public bool RtkLostActive { get; private set; }

    /// <summary>Minimum acceptable GPS fix quality (default: RtkFix).</summary>
    public GpsFixQuality MinFixQuality { get; set; } = GpsFixQuality.RtkFix;

    /// <summary>Last received GPS fix quality.</summary>
    public GpsFixQuality LastFixQuality { get; private set; } = GpsFixQuality.RtkFix;

    /// <summary>Whether the system is in emergency stop state.</summary>
    public bool EmergencyStopActive { get; private set; }

    /// <summary>Whether air pressure interlock is currently blocking spray.</summary>
    public bool AirPressureLostActive { get; private set; }

    /// <summary>
    /// Minimum safe air pressure (Bar). Below this for AirPressureTimeoutSeconds → E-STOP.
    /// 0 = disable pressure interlock.
    /// </summary>
    public double MinSafeAirPressureBar { get; set; } = 2.0;

    /// <summary>
    /// Seconds of sustained low air pressure before triggering interlock.
    /// Same principle as RTK timeout — prevents false alarms from sensor noise.
    /// </summary>
    public double AirPressureTimeoutSeconds { get; set; } = 2.0;

    /// <summary>Whether air pressure is currently degraded (but timeout not yet expired).</summary>
    public bool AirPressureDegraded { get; private set; }

    /// <summary>Last measured air pressure (Bar).</summary>
    public double LastAirPressureBar { get; private set; }

    /// <summary>Target speed in km/h for interlock.</summary>
    public double TargetSpeedKmh { get; set; } = 5.0;

    /// <summary>Allowed speed deviation percentage (0.10 = ±10%).</summary>
    public double SpeedToleranceFraction { get; set; } = 0.10;

    /// <summary>Last recorded speed (for UI display).</summary>
    public double LastSpeedKmh { get; private set; }

    /// <summary>Minimum acceptable speed based on current settings.</summary>
    public double MinSpeedKmh => TargetSpeedKmh * (1.0 - SpeedToleranceFraction);

    /// <summary>Maximum acceptable speed based on current settings.</summary>
    public double MaxSpeedKmh => TargetSpeedKmh * (1.0 + SpeedToleranceFraction);

    /// <summary>
    /// Speed hysteresis band (km/h). Once interlock triggers,
    /// speed must return this far inside the acceptable range to clear.
    /// Prevents rapid on/off toggling at the speed boundary.
    /// </summary>
    public double SpeedHysteresisKmh { get; set; } = 0.1;

    // ── RTK Timeout Logic ──

    /// <summary>
    /// Seconds to wait after RTK fix is lost before triggering E-STOP.
    /// Prevents false alarms from single dropped GPS packets.
    /// 0 = instant E-STOP (original behavior).
    /// </summary>
    public double RtkLossTimeoutSeconds { get; set; } = 2.0;

    /// <summary>Whether RTK quality has been degraded (but timeout not yet expired).</summary>
    public bool RtkDegraded { get; private set; }

    /// <summary>Time when RTK loss was first detected.</summary>
    private DateTime _rtkLossStart = DateTime.MinValue;

    /// <summary>Time when low air pressure was first detected.</summary>
    private DateTime _airPressureLossStart = DateTime.MinValue;

    // ── Events ──

    /// <summary>Fires when speed interlock state changes (for UI indicator).</summary>
    public event Action<bool, double>? OnSpeedInterlockChanged;

    /// <summary>Fires when RTK quality drops below minimum (for logging/UI).</summary>
    public event Action<GpsFixQuality>? OnRtkLost;

    /// <summary>Fires when RTK quality is restored.</summary>
    public event Action<GpsFixQuality>? OnRtkRestored;

    /// <summary>Fires when RTK is degraded but timeout not yet expired (warning).</summary>
    public event Action<GpsFixQuality, double>? OnRtkDegraded;

    /// <summary>Fires when emergency stop state changes.</summary>
    public event Action<bool>? OnEmergencyStopChanged;

    /// <summary>Fires when air pressure drops below safe threshold for timeout period.</summary>
    public event Action<double>? OnAirPressureLost;

    /// <summary>Fires when air pressure is restored above safe threshold.</summary>
    public event Action<double>? OnAirPressureRestored;

    /// <summary>
    /// Applies all safety interlocks to the computed valve mask.
    /// Returns the final mask to send to Teensy.
    /// </summary>
    /// <param name="computedMask">Mask from SpatialEngine.</param>
    /// <param name="currentSpeedKmh">Current tractor speed in km/h.</param>
    /// <returns>Final 14-bit valve mask (0x0000 if any interlock is active).</returns>
    public ushort ApplyInterlocks(ushort computedMask, double currentSpeedKmh)
    {
        LastSpeedKmh = currentSpeedKmh;

        // Emergency stop overrides everything
        if (EmergencyStopActive)
            return 0;

        // RTK interlock
        if (RtkLostActive)
            return 0;

        // Air pressure interlock
        if (AirPressureLostActive)
            return 0;

        // Speed interlock check with hysteresis
        bool wasActive = SpeedInterlockActive;
        if (wasActive)
        {
            // Must return inside range by hysteresis margin to clear
            SpeedInterlockActive = currentSpeedKmh < (MinSpeedKmh + SpeedHysteresisKmh)
                || currentSpeedKmh > (MaxSpeedKmh - SpeedHysteresisKmh);
        }
        else
        {
            SpeedInterlockActive = currentSpeedKmh < MinSpeedKmh || currentSpeedKmh > MaxSpeedKmh;
        }

        // Fire event on state change
        if (SpeedInterlockActive != wasActive)
        {
            _logger?.Warn("Interlocks",
                $"Speed interlock {(SpeedInterlockActive ? "ACTIVE" : "CLEARED")} | " +
                $"speed={currentSpeedKmh:F2} range=[{MinSpeedKmh:F1},{MaxSpeedKmh:F1}]");
            OnSpeedInterlockChanged?.Invoke(SpeedInterlockActive, currentSpeedKmh);
        }

        if (SpeedInterlockActive)
            return 0;

        return computedMask;
    }

    /// <summary>
    /// Checks speed without applying to a mask. For UI display purposes.
    /// Returns true if speed is within acceptable range.
    /// </summary>
    public bool IsSpeedAcceptable(double speedKmh)
    {
        return speedKmh >= MinSpeedKmh && speedKmh <= MaxSpeedKmh;
    }

    /// <summary>
    /// Checks RTK fix quality with timeout-based delay before E-STOP.
    /// If quality drops below MinFixQuality:
    ///   - RtkDegraded = true immediately (warning state)
    ///   - RtkLostActive = true only after RtkLossTimeoutSeconds expires
    /// Single packet losses don't trigger E-STOP.
    /// </summary>
    /// <param name="quality">Current GPS fix quality from PGN 253.</param>
    /// <returns>True if quality is acceptable (RTK not lost).</returns>
    public bool CheckRtkQuality(GpsFixQuality quality)
    {
        return CheckRtkQuality(quality, DateTime.UtcNow);
    }

    /// <summary>
    /// Testable overload with explicit timestamp.
    /// </summary>
    public bool CheckRtkQuality(GpsFixQuality quality, DateTime now)
    {
        LastFixQuality = quality;
        bool qualityOk = (int)quality >= (int)MinFixQuality;

        // Special case: RtkFloat (5) has worse precision than RtkFix (4)
        // despite higher enum value. If we require RtkFix, reject RtkFloat.
        if (MinFixQuality == GpsFixQuality.RtkFix && quality == GpsFixQuality.RtkFloat)
        {
            qualityOk = false;
        }

        if (qualityOk)
        {
            // RTK restored — clear everything
            bool wasLost = RtkLostActive;
            bool wasDegraded = RtkDegraded;
            RtkLostActive = false;
            RtkDegraded = false;
            _rtkLossStart = DateTime.MinValue;

            if (wasLost || wasDegraded)
            {
                OnRtkRestored?.Invoke(quality);
            }
            return true;
        }

        // Quality is below threshold
        if (!RtkDegraded)
        {
            // First detection — start timer
            RtkDegraded = true;
            _rtkLossStart = now;
            _logger?.Warn("Interlocks",
                $"RTK DEGRADED: quality={quality} (min={MinFixQuality}), timeout={RtkLossTimeoutSeconds}s");
            OnRtkDegraded?.Invoke(quality, RtkLossTimeoutSeconds);
        }

        // Check if timeout has expired
        if (RtkLossTimeoutSeconds <= 0)
        {
            // Instant mode — no timeout
            if (!RtkLostActive)
            {
                RtkLostActive = true;
                _logger?.Error("Interlocks", $"RTK LOST (instant mode): quality={quality}");
                OnRtkLost?.Invoke(quality);
            }
        }
        else
        {
            double elapsed = (now - _rtkLossStart).TotalSeconds;
            if (elapsed >= RtkLossTimeoutSeconds && !RtkLostActive)
            {
                RtkLostActive = true;
                _logger?.Error("Interlocks",
                    $"RTK LOST after {elapsed:F1}s: quality={quality}");
                OnRtkLost?.Invoke(quality);
            }
        }

        return !RtkLostActive;
    }

    /// <summary>
    /// Activates emergency stop — all valves forced closed.
    /// </summary>
    public void ActivateEmergencyStop()
    {
        EmergencyStopActive = true;
        _logger?.Error("Interlocks", "E-STOP ACTIVATED");
        OnEmergencyStopChanged?.Invoke(true);
    }

    /// <summary>
    /// Clears emergency stop (requires explicit user action).
    /// </summary>
    public void ClearEmergencyStop()
    {
        EmergencyStopActive = false;
        _logger?.Info("Interlocks", "E-STOP CLEARED");
        OnEmergencyStopChanged?.Invoke(false);
    }

    // ════════════════════════════════════════════════════════════════════
    // Air Pressure Interlock
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks pneumatic air pressure with timeout-based delay before E-STOP.
    /// If pressure drops below MinSafeAirPressureBar:
    ///   - AirPressureDegraded = true immediately (warning state)
    ///   - AirPressureLostActive = true after AirPressureTimeoutSeconds expires
    /// </summary>
    /// <param name="airPressureBar">Current air pressure in Bar from SensorHub.</param>
    /// <returns>True if pressure is acceptable.</returns>
    public bool CheckAirPressure(double airPressureBar)
    {
        return CheckAirPressure(airPressureBar, DateTime.UtcNow);
    }

    /// <summary>
    /// Testable overload with explicit timestamp.
    /// </summary>
    public bool CheckAirPressure(double airPressureBar, DateTime now)
    {
        LastAirPressureBar = airPressureBar;

        // Disabled — always OK
        if (MinSafeAirPressureBar <= 0)
            return true;

        bool pressureOk = airPressureBar >= MinSafeAirPressureBar;

        if (pressureOk)
        {
            // Pressure restored — clear everything
            bool wasLost = AirPressureLostActive;
            bool wasDegraded = AirPressureDegraded;
            AirPressureLostActive = false;
            AirPressureDegraded = false;
            _airPressureLossStart = DateTime.MinValue;

            if (wasLost || wasDegraded)
            {
                OnAirPressureRestored?.Invoke(airPressureBar);
            }
            return true;
        }

        // Pressure below threshold
        if (!AirPressureDegraded)
        {
            // First detection — start timer
            AirPressureDegraded = true;
            _airPressureLossStart = now;
        }

        // Check if timeout has expired
        if (AirPressureTimeoutSeconds <= 0)
        {
            // L6 FIX: Instant mode — no timeout (parity with RTK interlock)
            if (!AirPressureLostActive)
            {
                AirPressureLostActive = true;
                _logger?.Error("Interlocks",
                    $"AIR PRESSURE LOST (instant): pressure={airPressureBar:F2} bar (min={MinSafeAirPressureBar:F1})");
                OnAirPressureLost?.Invoke(airPressureBar);
            }
        }
        else
        {
            double elapsed = (now - _airPressureLossStart).TotalSeconds;
            if (elapsed >= AirPressureTimeoutSeconds && !AirPressureLostActive)
            {
                AirPressureLostActive = true;
                _logger?.Error("Interlocks",
                    $"AIR PRESSURE LOST after {elapsed:F1}s: pressure={airPressureBar:F2} bar (min={MinSafeAirPressureBar:F1})");
                OnAirPressureLost?.Invoke(airPressureBar);
            }
        }

        return !AirPressureLostActive;
    }
}
