using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlotManager.Core.Services;

using PlotManager.Core.Models;

/// <summary>
/// Persists and restores a field session (GridParams + HardwareRouting)
/// to/from a JSON file in %LocalAppData%/AOGPlotManager/sessions/.
///
/// This solves STORE-1 and STORE-2 — operator no longer has to re-enter
/// GPS origin/heading and routing table every time they restart the app.
/// </summary>
public class SessionService
{
    private static readonly string DefaultSessionDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AOGPlotManager", "sessions");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Saves a session (grid params + routing) to <paramref name="filePath"/>.
    /// If <paramref name="filePath"/> is null, saves to the default sessions directory
    /// with a timestamped filename.
    /// </summary>
    /// <returns>The path the session was saved to.</returns>
    public string Save(FieldSession session, string? filePath = null)
    {
        session.SavedAt = DateTime.UtcNow;
        session.AppVersion = "0.2.0";

        filePath ??= GetDefaultPath(session.SessionName);

        string dir = Path.GetDirectoryName(filePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(session, JsonOptions);
        File.WriteAllText(filePath, json);
        return filePath;
    }

    /// <summary>
    /// Loads a session from <paramref name="filePath"/>.
    /// </summary>
    /// <exception cref="FileNotFoundException">If the file does not exist.</exception>
    /// <exception cref="JsonException">If the file is not valid JSON.</exception>
    public FieldSession Load(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Session file not found: {filePath}", filePath);

        string json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<FieldSession>(json, JsonOptions)
               ?? throw new JsonException("Session file is empty or invalid.");
    }

    /// <summary>
    /// Returns a list of all session files in the default sessions directory,
    /// ordered by last write time (newest first).
    /// </summary>
    public IReadOnlyList<string> ListSavedSessions()
    {
        if (!Directory.Exists(DefaultSessionDir))
            return Array.Empty<string>();

        return Directory
            .GetFiles(DefaultSessionDir, "*.session.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
    }

    /// <summary>
    /// Returns the most recently saved session file, or null if none exist.
    /// </summary>
    public string? GetLatestSessionPath()
    {
        var sessions = ListSavedSessions();
        return sessions.Count > 0 ? sessions[0] : null;
    }

    // ── Private helpers ──

    private static string GetDefaultPath(string sessionName)
    {
        // Sanitise filename
        string safe = string.Concat((sessionName ?? "session")
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(DefaultSessionDir, $"{safe}_{timestamp}.session.json");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Data model
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// All data required to resume a field trial session without re-entering
/// GPS coordinates or routing assignments.
/// </summary>
public class FieldSession
{
    /// <summary>Human-readable session name (e.g. trial name or field ID).</summary>
    public string SessionName { get; set; } = "Session";

    /// <summary>App version that created this session (for forward-compat checks).</summary>
    public string? AppVersion { get; set; }

    /// <summary>UTC timestamp when the session was last saved.</summary>
    public DateTime SavedAt { get; set; }

    // ── Grid parameters ──

    /// <summary>Grid origin (GPS, SW corner).</summary>
    public double OriginLatitude { get; set; }

    /// <summary>Grid origin (GPS, SW corner).</summary>
    public double OriginLongitude { get; set; }

    /// <summary>Heading of the grid in degrees (0=North, 90=East).</summary>
    public double HeadingDegrees { get; set; }

    /// <summary>Number of rows in the trial grid.</summary>
    public int Rows { get; set; }

    /// <summary>Number of columns in the trial grid.</summary>
    public int Columns { get; set; }

    /// <summary>Width of each plot (metres, across boom).</summary>
    public double PlotWidthMeters { get; set; }

    /// <summary>Length of each plot (metres, along driving direction).</summary>
    public double PlotLengthMeters { get; set; }

    /// <summary>Buffer between columns (metres).</summary>
    public double BufferWidthMeters { get; set; } = 0.5;

    /// <summary>Buffer between rows (metres).</summary>
    public double BufferLengthMeters { get; set; } = 1.0;

    // ── Hardware routing ──

    /// <summary>
    /// Product → valve channel(s) mapping.
    /// Key: product name. Value: list of 0-based section indices.
    /// </summary>
    public Dictionary<string, List<int>> ProductToSections { get; set; } = new();

    // ── Trial design ──

    /// <summary>Path to the trial map CSV (for reference).</summary>
    public string? TrialMapCsvPath { get; set; }

    /// <summary>Trial name from the TrialMap.</summary>
    public string? TrialName { get; set; }

    // ── Convenience factory ──

    /// <summary>
    /// Builds a GridGenerator.GridParams from this session.
    /// </summary>
    public GridGenerator.GridParams ToGridParams() => new()
    {
        Origin           = new GeoPoint(OriginLatitude, OriginLongitude),
        HeadingDegrees   = HeadingDegrees,
        Rows             = Rows,
        Columns          = Columns,
        PlotWidthMeters  = PlotWidthMeters,
        PlotLengthMeters = PlotLengthMeters,
        BufferWidthMeters  = BufferWidthMeters,
        BufferLengthMeters = BufferLengthMeters,
    };

    /// <summary>
    /// Builds a HardwareRouting from this session.
    /// </summary>
    public HardwareRouting ToHardwareRouting() => new()
    {
        ProductToSections = ProductToSections
            .ToDictionary(kv => kv.Key, kv => new List<int>(kv.Value)),
    };

    /// <summary>
    /// Populates this session from a GridGenerator.GridParams.
    /// </summary>
    public void SetFromGridParams(GridGenerator.GridParams p)
    {
        OriginLatitude     = p.Origin.Latitude;
        OriginLongitude    = p.Origin.Longitude;
        HeadingDegrees     = p.HeadingDegrees;
        Rows               = p.Rows;
        Columns            = p.Columns;
        PlotWidthMeters    = p.PlotWidthMeters;
        PlotLengthMeters   = p.PlotLengthMeters;
        BufferWidthMeters  = p.BufferWidthMeters;
        BufferLengthMeters = p.BufferLengthMeters;
    }

    /// <summary>
    /// Populates this session from a HardwareRouting.
    /// </summary>
    public void SetFromRouting(HardwareRouting routing)
    {
        ProductToSections = routing.ProductToSections
            .ToDictionary(kv => kv.Key, kv => new List<int>(kv.Value));
    }
}
