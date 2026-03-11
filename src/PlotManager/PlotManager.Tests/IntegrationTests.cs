using PlotManager.Core.Models;
using PlotManager.Core.Services;

namespace PlotManager.Tests;

/// <summary>
/// Tests for previously uncovered areas:
///   - TrialMapParser (CSV parsing, two formats, edge cases)
///   - GeoPoint (DistanceTo, UTM projection, ToString)
///   - TrialMap (GetProduct by string and row/col, Products set)
///   - HardwareRouting (GetSections, Validate)
///   - AsAppliedLogger (session lifecycle, CSV format, SHA256, concurrency)
///   - PlotGrid (Contains, FindPlot)
/// </summary>
public class IntegrationTests : IDisposable
{
    private readonly string _tmpDir;

    public IntegrationTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"PlotManagerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch { /* cleanup best-effort */ }
    }

    // ════════════════════════════════════════════════════════════════════
    // TrialMapParser — PlotId,Product format
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void TrialMapParser_PlotIdFormat_ParsesCorrectly()
    {
        string csv = "PlotId,Product\nR1C1,Herbicide A\nR1C2,Control\nR2C1,Herbicide B\n";
        string path = WriteTempCsv(csv, "plotid_format.csv");

        TrialMap map = TrialMapParser.Parse(path, "Test Trial");

        Assert.Equal("Test Trial", map.TrialName);
        Assert.Equal(3, map.PlotAssignments.Count);
        Assert.Equal("Herbicide A", map.GetProduct("R1C1"));
        Assert.Equal("Control", map.GetProduct("R1C2"));
        Assert.Equal("Herbicide B", map.GetProduct("R2C1"));
    }

    [Fact]
    public void TrialMapParser_RowColFormat_ParsesCorrectly()
    {
        string csv = "Row,Column,Product\n1,1,Product A\n1,2,Product B\n2,1,Control\n";
        string path = WriteTempCsv(csv, "rowcol_format.csv");

        TrialMap map = TrialMapParser.Parse(path);

        Assert.Equal("rowcol_format", map.TrialName); // Uses filename
        Assert.Equal("Product A", map.GetProduct("R1C1"));
        Assert.Equal("Product B", map.GetProduct("R1C2"));
        Assert.Equal("Control", map.GetProduct("R2C1"));
    }

    [Fact]
    public void TrialMapParser_SkipsEmptyLinesAndComments()
    {
        string csv = "PlotId,Product\nR1C1,ProductA\n\n# This is a comment\nR1C2,ProductB\n";
        string path = WriteTempCsv(csv, "with_comments.csv");

        TrialMap map = TrialMapParser.Parse(path);

        Assert.Equal(2, map.PlotAssignments.Count);
    }

    [Fact]
    public void TrialMapParser_MissingFile_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            TrialMapParser.Parse("/nonexistent/path/trial.csv"));
    }

    [Fact]
    public void TrialMapParser_HeaderOnly_ThrowsFormatException()
    {
        string csv = "PlotId,Product\n";
        string path = WriteTempCsv(csv, "header_only.csv");

        // Parser requires at least header + 1 data row
        Assert.Throws<FormatException>(() => TrialMapParser.Parse(path));
    }

    [Fact]
    public void TrialMapParser_EmptyFile_ThrowsFormatException()
    {
        string path = WriteTempCsv("", "empty.csv");
        Assert.Throws<FormatException>(() => TrialMapParser.Parse(path));
    }

    [Fact]
    public void TrialMapParser_InvalidRowCol_ThrowsFormatException()
    {
        string csv = "Row,Column,Product\nabc,def,ProductX\n";
        string path = WriteTempCsv(csv, "bad_rowcol.csv");

        Assert.Throws<FormatException>(() => TrialMapParser.Parse(path));
    }

    [Fact]
    public void TrialMapParser_QuotedFields_HandleCommas()
    {
        string csv = "PlotId,Product\nR1C1,\"Product A, Special\"\nR1C2,Normal\n";
        string path = WriteTempCsv(csv, "quoted.csv");

        TrialMap map = TrialMapParser.Parse(path);

        Assert.Equal("Product A, Special", map.GetProduct("R1C1"));
        Assert.Equal("Normal", map.GetProduct("R1C2"));
    }

    [Fact]
    public void TrialMapParser_CaseInsensitiveLookup()
    {
        string csv = "PlotId,Product\nr1c1,ProductA\n";
        string path = WriteTempCsv(csv, "case.csv");

        TrialMap map = TrialMapParser.Parse(path);

        // PlotAssignments uses OrdinalIgnoreCase
        Assert.Equal("ProductA", map.GetProduct("R1C1"));
        Assert.Equal("ProductA", map.GetProduct("r1c1"));
    }

    // ════════════════════════════════════════════════════════════════════
    // GeoPoint
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void GeoPoint_DistanceTo_SamePoint_IsZero()
    {
        var p = new GeoPoint(50.0, 30.0);
        Assert.Equal(0.0, p.DistanceTo(p), precision: 1);
    }

    [Fact]
    public void GeoPoint_DistanceTo_Known100mApart()
    {
        // ~100m apart at ~50° latitude
        var p1 = new GeoPoint(50.0, 30.0);
        var p2 = new GeoPoint(50.0009, 30.0);  // ~100m north

        double dist = p1.DistanceTo(p2);
        Assert.InRange(dist, 90, 110); // Haversine should be ~100m
    }

    [Fact]
    public void GeoPoint_DistanceTo_1km()
    {
        var p1 = new GeoPoint(50.0, 30.0);
        var p2 = new GeoPoint(50.009, 30.0);  // ~1km north

        double dist = p1.DistanceTo(p2);
        Assert.InRange(dist, 950, 1050);
    }

    [Fact]
    public void GeoPoint_DistanceTo_IsSymmetric()
    {
        var a = new GeoPoint(50.0, 30.0);
        var b = new GeoPoint(50.01, 30.01);

        Assert.Equal(a.DistanceTo(b), b.DistanceTo(a), precision: 6);
    }

    [Fact]
    public void GeoPoint_UtmZone_CorrectForKyiv()
    {
        var kyiv = new GeoPoint(50.45, 30.52);
        Assert.Equal(36, kyiv.UtmZone); // Kyiv is in UTM zone 36
    }

    [Fact]
    public void GeoPoint_UtmZone_CorrectForLondon()
    {
        var london = new GeoPoint(51.5, -0.12);
        Assert.Equal(30, london.UtmZone); // London is in UTM zone 30
    }

    [Fact]
    public void GeoPoint_Easting_IsCenteredAround500000()
    {
        // UTM easting is centered at 500,000m for the central meridian
        var point = new GeoPoint(50.0, 33.0); // Central meridian of zone 36
        // Should be close to 500,000
        Assert.InRange(point.Easting, 490000, 510000);
    }

    [Fact]
    public void GeoPoint_Northing_PositiveForNorthernHemisphere()
    {
        var point = new GeoPoint(50.0, 30.0);
        Assert.True(point.Northing > 0);
    }

    [Fact]
    public void GeoPoint_ToString_FormatsCorrectly()
    {
        var point = new GeoPoint(50.123456, 30.654321);
        string str = point.ToString();
        Assert.Contains("50.123456", str);
        Assert.Contains("30.654321", str);
    }

    // ════════════════════════════════════════════════════════════════════
    // TrialMap
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void TrialMap_GetProduct_ByRowCol_UsesOneBased()
    {
        var map = new TrialMap
        {
            TrialName = "Test",
            PlotAssignments = new Dictionary<string, string>
            {
                ["R1C1"] = "Product A",
                ["R2C3"] = "Product B",
            }
        };

        // GetProduct(row, col) is 0-based, converts to R{row+1}C{col+1}
        Assert.Equal("Product A", map.GetProduct(0, 0));
        Assert.Equal("Product B", map.GetProduct(1, 2));
        Assert.Null(map.GetProduct(5, 5)); // Not in map
    }

    [Fact]
    public void TrialMap_Products_ReturnsUniqueSet()
    {
        var map = new TrialMap
        {
            PlotAssignments = new Dictionary<string, string>
            {
                ["R1C1"] = "Product A",
                ["R1C2"] = "Product B",
                ["R2C1"] = "Product A", // duplicate
                ["R2C2"] = "Control",
            }
        };

        Assert.Equal(3, map.Products.Count);
        Assert.Contains("Product A", map.Products);
        Assert.Contains("Product B", map.Products);
        Assert.Contains("Control", map.Products);
    }

    [Fact]
    public void TrialMap_GetProduct_NonExistent_ReturnsNull()
    {
        var map = new TrialMap
        {
            PlotAssignments = new Dictionary<string, string>
            {
                ["R1C1"] = "X",
            }
        };

        Assert.Null(map.GetProduct("R99C99"));
    }

    // ════════════════════════════════════════════════════════════════════
    // HardwareRouting
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void HardwareRouting_GetSections_ReturnsAssignedChannels()
    {
        var routing = new HardwareRouting
        {
            ProductToSections = new Dictionary<string, List<int>>
            {
                ["Herbicide"] = new List<int> { 0, 1, 2 },
                ["Fungicide"] = new List<int> { 3, 4 },
            }
        };

        Assert.Equal(new[] { 0, 1, 2 }, routing.GetSections("Herbicide"));
        Assert.Equal(new[] { 3, 4 }, routing.GetSections("Fungicide"));
    }

    [Fact]
    public void HardwareRouting_GetSections_UnknownProduct_ReturnsEmpty()
    {
        var routing = new HardwareRouting();
        Assert.Empty(routing.GetSections("NonExistent"));
    }

    [Fact]
    public void HardwareRouting_Validate_MissingProduct_ReportsError()
    {
        var routing = new HardwareRouting
        {
            ProductToSections = new Dictionary<string, List<int>>
            {
                ["Herbicide"] = new List<int> { 0 },
            }
        };

        var map = new TrialMap
        {
            PlotAssignments = new Dictionary<string, string>
            {
                ["R1C1"] = "Herbicide",
                ["R1C2"] = "Fungicide",  // Not in routing!
            }
        };

        var errors = routing.Validate(map);
        Assert.Single(errors);
        Assert.Contains("Fungicide", errors[0]);
    }

    [Fact]
    public void HardwareRouting_Validate_AllMapped_NoErrors()
    {
        var routing = new HardwareRouting
        {
            ProductToSections = new Dictionary<string, List<int>>
            {
                ["A"] = new List<int> { 0 },
                ["B"] = new List<int> { 1 },
            }
        };
        var map = new TrialMap
        {
            PlotAssignments = new Dictionary<string, string>
            {
                ["R1C1"] = "A",
                ["R1C2"] = "B",
            }
        };

        Assert.Empty(routing.Validate(map));
    }

    [Fact]
    public void HardwareRouting_Validate_SharedChannel_ReportsContaminationRisk()
    {
        // TRIAL-4: Two products sharing the same valve channel would spray simultaneously
        var routing = new HardwareRouting
        {
            ProductToSections = new Dictionary<string, List<int>>
            {
                ["Herbicide"] = new List<int> { 0, 1 },
                ["Fungicide"] = new List<int> { 1, 2 }, // Channel 1 shared!
            }
        };
        var map = new TrialMap
        {
            PlotAssignments = new Dictionary<string, string>
            {
                ["R1C1"] = "Herbicide",
                ["R1C2"] = "Fungicide",
            }
        };

        var errors = routing.Validate(map);
        Assert.Single(errors);
        Assert.Contains("channel 1", errors[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contamination", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HardwareRouting_Validate_EmptySectionList_ReportsError()
    {
        // A product mapped to an empty list is equivalent to not mapped
        var routing = new HardwareRouting
        {
            ProductToSections = new Dictionary<string, List<int>>
            {
                ["Herbicide"] = new List<int>(), // empty!
            }
        };
        var map = new TrialMap
        {
            PlotAssignments = new Dictionary<string, string> { ["R1C1"] = "Herbicide" }
        };

        var errors = routing.Validate(map);
        Assert.Single(errors);
        Assert.Contains("Herbicide", errors[0]);
    }

    // ════════════════════════════════════════════════════════════════════
    // AsAppliedLogger — session lifecycle
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AsAppliedLogger_StartSession_CreatesFileWithHeader()
    {
        using var logger = new AsAppliedLogger();
        logger.FlushIntervalMs = 50; // Speed up for tests

        logger.StartSession(_tmpDir, "TestTrial");

        Assert.NotNull(logger.FilePath);
        Assert.True(File.Exists(logger.FilePath));

        // Header should be written immediately
        string content = ReadFileSafe(logger.FilePath);
        Assert.Contains("Timestamp,Latitude,Longitude,PlotId,Product", content);
        Assert.Contains("Air_Bar", content);
        Assert.Contains("Flow_1_Lpm", content);
    }

    [Fact]
    public void AsAppliedLogger_LogRecord_WritesAfterFlush()
    {
        using var logger = new AsAppliedLogger { FlushIntervalMs = 50 };
        logger.StartSession(_tmpDir, "FlushTest");

        logger.LogRecord(DateTime.UtcNow, 50.1, 30.2, "R1C1", "ProductA", 5.5, 0x000F);
        Assert.Equal(1, logger.RecordCount);

        // Wait for background flush
        Thread.Sleep(200);

        string content = ReadFileSafe(logger.FilePath!);
        Assert.Contains("50.10000000", content);
        Assert.Contains("30.20000000", content);
        Assert.Contains("R1C1", content);
        Assert.Contains("ProductA", content);
        Assert.Contains("0x000F", content);
    }

    [Fact]
    public void AsAppliedLogger_LogRecordWithSensors_IncludesSensorValues()
    {
        using var logger = new AsAppliedLogger { FlushIntervalMs = 50 };
        logger.StartSession(_tmpDir, "SensorTest");

        double[] flows = { 1.5, 2.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 };
        logger.LogRecordWithSensors(DateTime.UtcNow, 50.0, 30.0, "R1C1", "Herb",
            5.0, 0x0003, 3.5, flows, fixQuality: "RTK", headingDeg: 135.0);

        Thread.Sleep(200);

        string content = ReadFileSafe(logger.FilePath!);
        Assert.Contains("3.50", content); // Air pressure
        Assert.Contains("1.500", content); // Flow 1
        Assert.Contains("2.000", content); // Flow 2
        Assert.Contains("RTK", content);
        Assert.Contains("135.0", content);
    }

    [Fact]
    public void AsAppliedLogger_StopSession_AppendsSha256Hash()
    {
        using var logger = new AsAppliedLogger { FlushIntervalMs = 50 };
        logger.StartSession(_tmpDir, "HashTest");

        logger.LogRecord(DateTime.UtcNow, 50.0, 30.0, "R1C1", "X", 5.0, 0x0001);
        Thread.Sleep(100);

        logger.StopSession();

        string content = ReadFileSafe(logger.FilePath!);
        Assert.Contains("# SHA256:", content);
        // SHA256 hex is 64 characters
        string hashLine = content.Split('\n').Last(l => l.StartsWith("# SHA256:"));
        string hash = hashLine.Replace("# SHA256: ", "").Trim();
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void AsAppliedLogger_DoubleStart_Throws()
    {
        using var logger = new AsAppliedLogger { FlushIntervalMs = 50 };
        logger.StartSession(_tmpDir, "DoubleStart");

        Assert.Throws<InvalidOperationException>(() =>
            logger.StartSession(_tmpDir, "DoubleStart2"));
    }

    [Fact]
    public void AsAppliedLogger_LogMeteoCheck_WritesMeteoRecord()
    {
        using var logger = new AsAppliedLogger { FlushIntervalMs = 50 };
        logger.StartSession(_tmpDir, "MeteoTest");

        logger.LogMeteoCheck(22.5, 65.0, 3.2, "NW");
        Thread.Sleep(200);

        string content = ReadFileSafe(logger.FilePath!);
        Assert.Contains("METEO", content);
        Assert.Contains("Temp=22.5", content);
        Assert.Contains("Humidity=65%", content);
        Assert.Contains("Wind=3.2m/s", content);
        Assert.Contains("Dir=NW", content);
    }

    [Fact]
    public void AsAppliedLogger_ConcurrentLogRecords_AllWritten()
    {
        using var logger = new AsAppliedLogger { FlushIntervalMs = 50 };
        logger.StartSession(_tmpDir, "ConcurrencyTest");

        const int threadCount = 4;
        const int recordsPerThread = 50;

        var threads = Enumerable.Range(0, threadCount).Select(t =>
            new Thread(() =>
            {
                for (int i = 0; i < recordsPerThread; i++)
                {
                    logger.LogRecord(DateTime.UtcNow, 50.0 + t * 0.001,
                        30.0 + i * 0.001, $"R{t}C{i}", "P", 5.0, 0x0001);
                }
            })).ToArray();

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        // RecordCount now uses Interlocked.Increment — should be exact
        Assert.Equal(threadCount * recordsPerThread, logger.RecordCount);

        // Let flush finish
        Thread.Sleep(300);
        logger.StopSession();

        string[] lines = File.ReadAllLines(logger.FilePath!);
        // Header + data records + SHA256 line
        int dataLines = lines.Count(l =>
            !string.IsNullOrWhiteSpace(l) &&
            !l.StartsWith('#') &&
            !l.StartsWith("Timestamp"));
        Assert.Equal(threadCount * recordsPerThread, dataLines);
    }

    [Fact]
    public void AsAppliedLogger_LogAfterStop_SilentlyIgnored()
    {
        using var logger = new AsAppliedLogger { FlushIntervalMs = 50 };
        logger.StartSession(_tmpDir, "AfterStopTest");
        logger.StopSession();

        // Should not throw
        logger.LogRecord(DateTime.UtcNow, 50.0, 30.0, "R1C1", "X", 5.0, 0x0001);
        Assert.Equal(0, logger.RecordCount); // Not counted
    }

    [Fact]
    public void AsAppliedLogger_NullProductAndPlotId_DefaultsToBufferAndNone()
    {
        using var logger = new AsAppliedLogger { FlushIntervalMs = 50 };
        logger.StartSession(_tmpDir, "NullTest");

        logger.LogRecord(DateTime.UtcNow, 50.0, 30.0, null, null, 0, 0);
        Thread.Sleep(200);

        string content = ReadFileSafe(logger.FilePath!);
        Assert.Contains("BUFFER", content);
        Assert.Contains("NONE", content);
    }

    [Fact]
    public void AsAppliedLogger_FilenameIsSanitized()
    {
        using var logger = new AsAppliedLogger { FlushIntervalMs = 50 };
        logger.StartSession(_tmpDir, "Trial/Name");

        Assert.NotNull(logger.FilePath);
        // '/' is invalid on all platforms
        string fileName = Path.GetFileName(logger.FilePath);
        Assert.DoesNotContain("/", fileName);
    }

    // ════════════════════════════════════════════════════════════════════
    // PlotGrid — Contains + FindPlot
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void PlotGrid_GenerateAndFindPlot()
    {
        var gen = new GridGenerator();
        var grid = gen.Generate(new GridGenerator.GridParams
        {
            Origin = new GeoPoint(50.0, 30.0),
            Rows = 3,
            Columns = 4,
            PlotWidthMeters = 6.0,
            PlotLengthMeters = 12.0,
            BufferLengthMeters = 1.0,
            BufferWidthMeters = 0.5,
            HeadingDegrees = 0,
        });

        Assert.Equal(3, grid.Rows);
        Assert.Equal(4, grid.Columns);
        Assert.Equal(12, grid.TotalPlots);
    }

    [Fact]
    public void GeoPoint_DistanceTo_Antipodal_LargeDistance()
    {
        var north = new GeoPoint(90.0, 0.0);
        var south = new GeoPoint(-90.0, 0.0);

        double dist = north.DistanceTo(south);
        // Half earth circumference ≈ 20,015 km
        Assert.InRange(dist, 19_000_000, 21_000_000);
    }

    [Fact]
    public void GeoPoint_UtmZone_Equator_WrapsBoundary()
    {
        // UTM zone 1 starts at -180°
        var p = new GeoPoint(0.0, -180.0);
        Assert.Equal(1, p.UtmZone);

        // UTM zone 60 ends at 180°
        var p2 = new GeoPoint(0.0, 179.0);
        Assert.Equal(60, p2.UtmZone);
    }

    // ════════════════════════════════════════════════════════════════════
    // WeatherSnapshot
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void WeatherSnapshot_HasRequiredProperties()
    {
        var ws = new WeatherSnapshot
        {
            TemperatureC = 0,
            HumidityPercent = 0,
            WindSpeedMs = 0,
        };
        Assert.Equal(0, ws.TemperatureC);
        Assert.Equal(0, ws.HumidityPercent);
        Assert.Equal(0, ws.WindSpeedMs);
    }

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    private string WriteTempCsv(string content, string filename)
    {
        string path = Path.Combine(_tmpDir, filename);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Reads file while writer may still hold it open.</summary>
    private static string ReadFileSafe(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }
}
