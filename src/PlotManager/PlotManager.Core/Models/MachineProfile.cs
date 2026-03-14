using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlotManager.Core.Models;





/// <summary>
/// Connection settings for hardware communication.
/// </summary>
public class ConnectionSettings
{
    /// <summary>Serial port for Teensy (e.g. "COM3" on Windows, "/dev/ttyACM0" on Linux). Empty = auto-detect.</summary>
    public string TeensyComPort { get; set; } = string.Empty;

    /// <summary>Baud rate for Teensy communication.</summary>
    public int TeensyBaudRate { get; set; } = 115200;



    /// <summary>UDP port for receiving PGN broadcasts from AgOpenGPS (AOG sends to modules on 8888).</summary>
    public int AogUdpListenPort { get; set; } = 8888;
    /// <summary>UDP port for sending section feedback to AgOpenGPS (AOG listens on 9999).</summary>
    public int AogUdpSendPort { get; set; } = 9999;

    /// <summary>AOG host address.</summary>
    public string AogHost { get; set; } = "127.0.0.1";
}

/// <summary>
/// Boom-level configuration within a Machine Profile.
/// Each boom can have its own hydraulic delays (different hose lengths).
/// </summary>
public class BoomProfile
{
    /// <summary>Boom index (0-based).</summary>
    public int BoomId { get; set; }

    /// <summary>Human-readable label.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Y-offset from GPS antenna (meters, negative = behind).</summary>
    public double YOffsetMeters { get; set; }

    /// <summary>X-offset from centerline (meters, positive = right).</summary>
    public double XOffsetMeters { get; set; }

    /// <summary>Valve channel on Teensy (0–13).</summary>
    public int ValveChannel { get; set; }

    /// <summary>Spray pattern width along driving direction (meters).</summary>
    public double SprayWidthMeters { get; set; } = 0.25;

    /// <summary>Overlap % to activate (0–100).</summary>
    public double ActivationOverlapPercent { get; set; } = 70;

    /// <summary>Overlap % to deactivate (0–100).</summary>
    public double DeactivationOverlapPercent { get; set; } = 30;

    /// <summary>
    /// Per-boom activation delay override (ms).
    /// -1 = use global SystemActivationDelayMs. ≥0 = override.
    /// Longer hose = higher value.
    /// </summary>
    public double ActivationDelayOverrideMs { get; set; } = -1;

    /// <summary>
    /// Per-boom deactivation delay override (ms).
    /// -1 = use global SystemDeactivationDelayMs. ≥0 = override.
    /// </summary>
    public double DeactivationDelayOverrideMs { get; set; } = -1;

    /// <summary>Whether this boom is installed/enabled.</summary>
    public bool Enabled { get; set; } = true;



    /// <summary>
    /// Returns effective activation delay: per-boom override or global fallback.
    /// </summary>
    public double GetEffectiveActivationDelay(double globalDelayMs) =>
        ActivationDelayOverrideMs >= 0 ? ActivationDelayOverrideMs : globalDelayMs;

    /// <summary>
    /// Returns effective deactivation delay: per-boom override or global fallback.
    /// </summary>
    public double GetEffectiveDeactivationDelay(double globalDelayMs) =>
        DeactivationDelayOverrideMs >= 0 ? DeactivationDelayOverrideMs : globalDelayMs;
}

/// <summary>
/// Machine Profile — complete serializable configuration for the sprayer.
/// Contains all physics parameters the operator calibrates in the field.
/// Saved as JSON, switchable between fluid presets (water vs adjuvant).
/// </summary>
public class MachineProfile
{
    // ── Identity ──

    /// <summary>Profile display name (e.g. "Gevax 10-boom / Water").</summary>
    public string ProfileName { get; set; } = "Стандартний профіль";

    /// <summary>Optional notes (e.g. "Calibrated 2026-03-09 on water at 5 km/h").</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Last modification timestamp (ISO 8601).</summary>
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;



    /// <summary>
    /// Global valve activation delay in ms (default for all booms).
    /// Per-boom overrides in BoomProfile take precedence when set.
    /// </summary>
    public double SystemActivationDelayMs { get; set; } = 300;

    /// <summary>Global valve deactivation delay in ms.</summary>
    public double SystemDeactivationDelayMs { get; set; } = 150;



    // ── Spatial Offsets ──

    /// <summary>Desired pre-activation distance (meters).</summary>
    public double PreActivationMeters { get; set; } = 0.5;

    /// <summary>Desired pre-deactivation distance (meters).</summary>
    public double PreDeactivationMeters { get; set; } = 0.2;

    // ── Crab-Walk (COG) ──

    /// <summary>
    /// Minimum Heading-vs-COG difference (degrees) to trigger crab-walk correction.
    /// Below this threshold, heading is used. Above — COG is used for rear boom projection.
    /// Typical: 2–5°.
    /// </summary>
    public double CogHeadingThresholdDegrees { get; set; } = 3.0;

    // ── GPS/RTK ──



    /// <summary>GPS update frequency in Hz. Used for acceleration filter calibration.</summary>
    public int GpsUpdateRateHz { get; set; } = 10;



    // ── Connections ──

    /// <summary>Hardware communication settings.</summary>
    public ConnectionSettings Connection { get; set; } = new();

    // ── Boom Array ──

    /// <summary>Per-boom configuration.</summary>
    public List<BoomProfile> Booms { get; set; } = new();

    // ════════════════════════════════════════════════════════════════════
    // Serialization
    // ════════════════════════════════════════════════════════════════════

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Serializes this profile to a JSON string.</summary>
    public string ToJson()
    {
        LastModifiedUtc = DateTime.UtcNow;
        return JsonSerializer.Serialize(this, _jsonOptions);
    }

    /// <summary>Deserializes a MachineProfile from JSON.</summary>
    public static MachineProfile FromJson(string json)
    {
        var profile = JsonSerializer.Deserialize<MachineProfile>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Invalid machine profile JSON.");
        profile.Validate();
        return profile;
    }

    /// <summary>Saves profile to a file.</summary>
    public void SaveToFile(string path)
    {
        string json = ToJson();
        File.WriteAllText(path, json);
    }

    /// <summary>Loads profile from a file.</summary>
    public static MachineProfile LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        return FromJson(json);
    }

    // ════════════════════════════════════════════════════════════════════
    // Validation (S1 FIX)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates that all profile parameters are within sane ranges.
    /// Throws <see cref="InvalidOperationException"/> on first violation.
    /// Called automatically from <see cref="FromJson"/>.
    /// </summary>
    public void Validate()
    {
        var errors = new List<string>();

        // Timing
        if (SystemActivationDelayMs < 0)
            errors.Add($"SystemActivationDelayMs must be ≥ 0, got {SystemActivationDelayMs}");
        if (SystemDeactivationDelayMs < 0)
            errors.Add($"SystemDeactivationDelayMs must be ≥ 0, got {SystemDeactivationDelayMs}");

        // Booms
        if (Booms.Count == 0)
            errors.Add("Profile must have at least 1 boom.");

        // Channel uniqueness
        var channels = new HashSet<int>();
        foreach (var bp in Booms)
        {
            if (bp.ValveChannel < 0 || bp.ValveChannel > 13)
                errors.Add($"Boom '{bp.Name}': ValveChannel {bp.ValveChannel} out of range [0,13].");
            if (!channels.Add(bp.ValveChannel))
                errors.Add($"Duplicate ValveChannel {bp.ValveChannel} in boom '{bp.Name}'.");
        }





        // GPS Hz
        if (GpsUpdateRateHz <= 0)
            errors.Add($"GpsUpdateRateHz must be > 0, got {GpsUpdateRateHz}");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"MachineProfile validation failed:\n• {string.Join("\n• ", errors)}");
    }

    // ════════════════════════════════════════════════════════════════════
    // Factory
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Creates a default profile with 10 booms.</summary>
    public static MachineProfile CreateDefault()
    {
        var profile = new MachineProfile();
        for (int i = 0; i < 10; i++)
        {
            profile.Booms.Add(new BoomProfile
            {
                BoomId = i,
                Name = $"Boom {i + 1}",
                ValveChannel = i,
                YOffsetMeters = -0.30 - (i * 0.05),
            });
        }
        return profile;
    }

    // ════════════════════════════════════════════════════════════════════
    // Converters & Appliers
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Builds a HardwareSetup from this profile.</summary>
    public HardwareSetup ToHardwareSetup()
    {
        var setup = new HardwareSetup();
        foreach (BoomProfile bp in Booms)
        {
            setup.Booms.Add(new Boom
            {
                BoomId = bp.BoomId,
                Name = bp.Name,
                ValveChannel = bp.ValveChannel,
                YOffsetMeters = bp.YOffsetMeters,
                SprayWidthMeters = bp.SprayWidthMeters,
                ActivationOverlapPercent = bp.ActivationOverlapPercent,
                DeactivationOverlapPercent = bp.DeactivationOverlapPercent,
                Enabled = bp.Enabled,
                Nozzles = new List<Nozzle>
                {
                    new Nozzle { NozzleId = 0, XOffsetMeters = 0 },
                },
            });
        }
        return setup;
    }

    /// <summary>Applies hydraulic/spatial/COG/EMA settings to SpatialEngine.</summary>
    public void ApplyToSpatialEngine(PlotManager.Core.Services.SpatialEngine engine)
    {
        engine.PreActivationMeters = PreActivationMeters;
        engine.PreDeactivationMeters = PreDeactivationMeters;
        engine.SystemActivationDelayMs = SystemActivationDelayMs;
        engine.SystemDeactivationDelayMs = SystemDeactivationDelayMs;
        engine.CogHeadingThresholdDegrees = CogHeadingThresholdDegrees;
        // EMA alpha: lower GPS Hz → more smoothing needed
        engine.AccelerationSmoothingAlpha = GpsUpdateRateHz >= 10 ? 0.3 : 0.5;
    }

    /// <summary>
    /// Applies speed + RTK settings to SectionController.
    /// </summary>
        // The AgOpenGPS rate controller handles speed/RTK loss.
        // If we want the PlotManager to enforce these locally again,
        // we'd add properties to the PlotManager config directly.

    /// <summary>
    /// Creates a per-boom delay provider function for use with
    /// SpatialEngine.EvaluatePerBoom's boomDelayProvider parameter.
    /// Returns (activationMs, deactivationMs) for a given valve channel.
    /// </summary>
    public Func<int, (double actMs, double deactMs)> CreateBoomDelayProvider()
    {
        // Build lookup by valve channel
        var lookup = new Dictionary<int, BoomProfile>();
        foreach (BoomProfile bp in Booms)
        {
            lookup[bp.ValveChannel] = bp;
        }

        return (int valveChannel) =>
        {
            if (lookup.TryGetValue(valveChannel, out BoomProfile? bp))
            {
                return (
                    bp.GetEffectiveActivationDelay(SystemActivationDelayMs),
                    bp.GetEffectiveDeactivationDelay(SystemDeactivationDelayMs)
                );
            }
            return (SystemActivationDelayMs, SystemDeactivationDelayMs);
        };
    }
}
