using PlotManager.Core.Models;
using PlotManager.Core.Services;

namespace PlotManager.Tests;

/// <summary>
/// Tests for SessionService (STORE-1/2) and ExperimentDesigner.Replications (TRIAL-1).
/// </summary>
public class SessionServiceTests : IDisposable
{
    private readonly string _tmpDir;

    public SessionServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"SessionTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { }
    }

    // ════════════════════════════════════════════════════════════════════
    // SessionService — Save / Load roundtrip
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionService_SaveAndLoad_RoundtripGridParams()
    {
        var svc = new SessionService();
        var session = new FieldSession
        {
            SessionName       = "TestRound",
            OriginLatitude    = 50.123,
            OriginLongitude   = 30.456,
            HeadingDegrees    = 45.0,
            Rows              = 4,
            Columns           = 6,
            PlotWidthMeters   = 6.0,
            PlotLengthMeters  = 12.0,
            BufferWidthMeters  = 0.5,
            BufferLengthMeters = 1.0,
        };

        string path = Path.Combine(_tmpDir, "test.session.json");
        svc.Save(session, path);
        var loaded = svc.Load(path);

        Assert.Equal(session.OriginLatitude,    loaded.OriginLatitude,    5);
        Assert.Equal(session.OriginLongitude,   loaded.OriginLongitude,   5);
        Assert.Equal(session.HeadingDegrees,    loaded.HeadingDegrees,    3);
        Assert.Equal(session.Rows,              loaded.Rows);
        Assert.Equal(session.Columns,           loaded.Columns);
        Assert.Equal(session.PlotWidthMeters,   loaded.PlotWidthMeters,   3);
        Assert.Equal(session.PlotLengthMeters,  loaded.PlotLengthMeters,  3);
    }

    [Fact]
    public void SessionService_SaveAndLoad_RoundtripHardwareRouting()
    {
        var svc = new SessionService();
        var session = new FieldSession
        {
            SessionName = "RoutingTest",
            ProductToSections = new Dictionary<string, List<int>>
            {
                ["HerbicideA"] = new List<int> { 0, 1 },
                ["Control"]    = new List<int> { 2 },
            }
        };

        string path = Path.Combine(_tmpDir, "routing.session.json");
        svc.Save(session, path);
        var loaded = svc.Load(path);

        Assert.Equal(2, loaded.ProductToSections.Count);
        Assert.Equal(new[] { 0, 1 }, loaded.ProductToSections["HerbicideA"]);
        Assert.Equal(new[] { 2 },    loaded.ProductToSections["Control"]);
    }

    [Fact]
    public void SessionService_Load_MissingFile_ThrowsFileNotFound()
    {
        var svc = new SessionService();
        Assert.Throws<FileNotFoundException>(() =>
            svc.Load(Path.Combine(_tmpDir, "nonexistent.json")));
    }

    [Fact]
    public void SessionService_Load_InvalidJson_ThrowsJsonException()
    {
        string path = Path.Combine(_tmpDir, "bad.json");
        File.WriteAllText(path, "not valid json {{ }}}}");
        var svc = new SessionService();
        Assert.ThrowsAny<Exception>(() => svc.Load(path));
    }

    [Fact]
    public void FieldSession_ToGridParams_ReturnsCorrectParams()
    {
        var session = new FieldSession
        {
            OriginLatitude    = 50.0,
            OriginLongitude   = 30.0,
            HeadingDegrees    = 90.0,
            Rows              = 3,
            Columns           = 4,
            PlotWidthMeters   = 6.0,
            PlotLengthMeters  = 12.0,
            BufferWidthMeters  = 0.5,
            BufferLengthMeters = 1.0,
        };

        var p = session.ToGridParams();

        Assert.Equal(50.0,  p.Origin.Latitude,  5);
        Assert.Equal(30.0,  p.Origin.Longitude, 5);
        Assert.Equal(90.0,  p.HeadingDegrees,   3);
        Assert.Equal(3,     p.Rows);
        Assert.Equal(4,     p.Columns);
        Assert.Equal(6.0,   p.PlotWidthMeters,  3);
        Assert.Equal(12.0,  p.PlotLengthMeters, 3);
    }

    [Fact]
    public void FieldSession_ToHardwareRouting_RoundtripChannels()
    {
        var session = new FieldSession
        {
            ProductToSections = new Dictionary<string, List<int>>
            {
                ["A"] = new List<int> { 0, 1 },
                ["B"] = new List<int> { 2 },
            }
        };

        var routing = session.ToHardwareRouting();
        Assert.Equal(2, routing.ProductToSections.Count);
        Assert.Equal(new[] { 0, 1 }, routing.GetSections("A"));
        Assert.Equal(new[] { 2 },    routing.GetSections("B"));
    }

    [Fact]
    public void FieldSession_SetFromGridParams_CapturesAllFields()
    {
        var gp = new GridGenerator.GridParams
        {
            Origin           = new GeoPoint(51.0, 31.0),
            HeadingDegrees   = 180.0,
            Rows             = 2,
            Columns          = 3,
            PlotWidthMeters  = 5.0,
            PlotLengthMeters = 10.0,
        };

        var session = new FieldSession();
        session.SetFromGridParams(gp);

        Assert.Equal(51.0, session.OriginLatitude,  5);
        Assert.Equal(31.0, session.OriginLongitude, 5);
        Assert.Equal(180.0, session.HeadingDegrees, 3);
        Assert.Equal(2, session.Rows);
        Assert.Equal(3, session.Columns);
    }

    [Fact]
    public void FieldSession_SetFromRouting_CapturesProductMap()
    {
        var routing = new HardwareRouting
        {
            ProductToSections = new Dictionary<string, List<int>>
            {
                ["X"] = new List<int> { 5, 6 },
            }
        };

        var session = new FieldSession();
        session.SetFromRouting(routing);

        Assert.Equal(new[] { 5, 6 }, session.ProductToSections["X"]);
    }

    [Fact]
    public void SessionService_SavedAt_IsSetOnSave()
    {
        var svc = new SessionService();
        var session = new FieldSession { SessionName = "TimeTest" };
        string path = Path.Combine(_tmpDir, "time.session.json");

        var before = DateTime.UtcNow.AddSeconds(-1);
        svc.Save(session, path);
        var after = DateTime.UtcNow.AddSeconds(1);

        var loaded = svc.Load(path);
        Assert.InRange(loaded.SavedAt, before, after);
    }
}

/// <summary>
/// Tests for ExperimentDesigner.Replications (TRIAL-1) and ValidatePlotCount.
/// </summary>
public class ExperimentDesignerReplicationTests
{
    private PlotGrid MakeGrid(int rows, int cols)
    {
        var gen = new GridGenerator();
        return gen.Generate(new GridGenerator.GridParams
        {
            Origin          = new GeoPoint(50.0, 30.0),
            Rows            = rows,
            Columns         = cols,
            PlotWidthMeters = 6.0,
            PlotLengthMeters = 12.0,
        });
    }

    // ── CRD ──

    [Fact]
    public void CRD_SufficientPlots_Generates()
    {
        var grid       = MakeGrid(4, 3);  // 12 plots
        var treatments = new List<string> { "A", "B", "C" };
        var designer   = new ExperimentDesigner(seed: 42);

        // 3 treatments × 4 replications = 12 plots exactly
        var map = designer.GenerateDesign(grid, treatments, ExperimentalDesignType.CRD, replications: 4);
        Assert.Equal(12, map.PlotAssignments.Count);
        // TrialName format: "Generated CRD (3 treatments × 4 reps)"
        Assert.Contains("3 treatments", map.TrialName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4 reps", map.TrialName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CRD_InsufficientPlots_ThrowsInvalidOperation()
    {
        var grid       = MakeGrid(2, 3);  // 6 plots
        var treatments = new List<string> { "A", "B", "C" };
        var designer   = new ExperimentDesigner();

        // 3 treatments × 3 reps = 9 required, only 6 available
        Assert.Throws<InvalidOperationException>(() =>
            designer.GenerateDesign(grid, treatments, ExperimentalDesignType.CRD, replications: 3));
    }

    // ── RCBD ──

    [Fact]
    public void RCBD_SufficientReps_Generates()
    {
        var grid       = MakeGrid(4, 3);  // 4 rows = 4 blocks, 3 cols = 3 treatments
        var treatments = new List<string> { "A", "B", "C" };
        var designer   = new ExperimentDesigner(seed: 1);

        var map = designer.GenerateDesign(grid, treatments, ExperimentalDesignType.RCBD, replications: 4);
        Assert.Equal(12, map.PlotAssignments.Count);
    }

    [Fact]
    public void RCBD_TooFewRows_ThrowsInvalidOperation()
    {
        var grid       = MakeGrid(2, 3);  // only 2 rows
        var treatments = new List<string> { "A", "B", "C" };
        var designer   = new ExperimentDesigner();

        // Requires 4 replications (rows), but grid has 2
        Assert.Throws<InvalidOperationException>(() =>
            designer.GenerateDesign(grid, treatments, ExperimentalDesignType.RCBD, replications: 4));
    }

    [Fact]
    public void RCBD_TooFewColumns_ThrowsInvalidOperation()
    {
        var grid       = MakeGrid(4, 2);  // 2 columns < 3 treatments
        var treatments = new List<string> { "A", "B", "C" };
        var designer   = new ExperimentDesigner();

        Assert.Throws<InvalidOperationException>(() =>
            designer.GenerateDesign(grid, treatments, ExperimentalDesignType.RCBD, replications: 4));
    }

    // ── Latin Square ──

    [Fact]
    public void LatinSquare_SquareGrid_Generates()
    {
        var grid       = MakeGrid(3, 3);
        var treatments = new List<string> { "A", "B", "C" };
        var designer   = new ExperimentDesigner(seed: 7);

        var map = designer.GenerateDesign(grid, treatments, ExperimentalDesignType.LatinSquare);
        Assert.Equal(9, map.PlotAssignments.Count);
    }

    [Fact]
    public void LatinSquare_TooSmallGrid_ThrowsInvalidOperation()
    {
        var grid       = MakeGrid(2, 3);  // 2 rows < 3 treatments
        var treatments = new List<string> { "A", "B", "C" };
        var designer   = new ExperimentDesigner();

        Assert.Throws<InvalidOperationException>(() =>
            designer.GenerateDesign(grid, treatments, ExperimentalDesignType.LatinSquare));
    }

    // ── Input validation ──

    [Fact]
    public void GenerateDesign_ZeroReplications_ThrowsArgumentOutOfRange()
    {
        var grid     = MakeGrid(3, 3);
        var designer = new ExperimentDesigner();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            designer.GenerateDesign(grid, new List<string> { "A" }, ExperimentalDesignType.CRD, replications: 0));
    }

    [Fact]
    public void GenerateDesign_DefaultReplications_IsOne()
    {
        var grid       = MakeGrid(3, 3);
        var treatments = new List<string> { "A", "B", "C" };
        var designer   = new ExperimentDesigner(seed: 99);

        // Should not throw with default replications=1
        var map = designer.GenerateDesign(grid, treatments, ExperimentalDesignType.CRD);
        Assert.NotEmpty(map.PlotAssignments);
    }
}
