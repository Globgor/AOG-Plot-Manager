using PlotManager.Core.Models;
using PlotManager.Core.Services;
using Xunit;
using ClosedXML.Excel;

namespace PlotManager.Tests;

/// <summary>
/// Tests for TrialExcelExporter: validates xlsx output structure, sheet content,
/// and null handling.
/// </summary>
public class TrialExcelExporterTests
{
    private static PlotGrid MakeGrid(int rows, int cols)
    {
        var gen = new GridGenerator();
        return gen.Generate(new GridGenerator.GridParams
        {
            Rows = rows,
            Columns = cols,
            PlotWidthMeters = 2.8,
            PlotLengthMeters = 10.0,
            BufferWidthMeters = 0,
            BufferLengthMeters = 4.0,
            Origin = new GeoPoint(48.5, 35.0),
            HeadingDegrees = 0,
        });
    }

    private static TrialMap MakeTrialMap(int rows, int cols)
    {
        var assignments = new Dictionary<string, string>();
        string[] treatments = { "Гербіцid", "Контроль", "Добриво_Б" };
        int idx = 0;
        for (int r = 1; r <= rows; r++)
            for (int c = 1; c <= cols; c++)
                assignments[$"R{r}C{c}"] = treatments[idx++ % treatments.Length];

        return new TrialMap
        {
            TrialName = "Test Export",
            PlotAssignments = assignments
        };
    }

    // ════════════════════════════════════════════════════════════════════
    // Core correctness
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExportToExcel_CreatesFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"trial_test_{Guid.NewGuid()}.xlsx");
        try
        {
            var exporter = new TrialExcelExporter();
            var trialMap = MakeTrialMap(3, 4);
            var grid = MakeGrid(3, 4);

            exporter.ExportToExcel(trialMap, grid, path);

            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExportToExcel_HasTwoSheets()
    {
        string path = Path.Combine(Path.GetTempPath(), $"trial_test_{Guid.NewGuid()}.xlsx");
        try
        {
            new TrialExcelExporter().ExportToExcel(MakeTrialMap(2, 3), MakeGrid(2, 3), path);

            using var wb = new XLWorkbook(path);
            Assert.Equal(2, wb.Worksheets.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExportToExcel_DataSheet_HasCorrectRowCount()
    {
        int rows = 3, cols = 4; // 12 plots
        string path = Path.Combine(Path.GetTempPath(), $"trial_test_{Guid.NewGuid()}.xlsx");
        try
        {
            new TrialExcelExporter().ExportToExcel(MakeTrialMap(rows, cols), MakeGrid(rows, cols), path);

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet(2); // "Дані (Список)"
            // Row 1 = header, rows 2..13 = data
            int dataRows = ws.LastRowUsed()!.RowNumber() - 1;
            Assert.Equal(rows * cols, dataRows);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExportToExcel_DataSheet_ContainsPlotIds()
    {
        string path = Path.Combine(Path.GetTempPath(), $"trial_test_{Guid.NewGuid()}.xlsx");
        try
        {
            var trialMap = MakeTrialMap(2, 2);
            new TrialExcelExporter().ExportToExcel(trialMap, MakeGrid(2, 2), path);

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet(2);
            var plotIds = ws.Column(1).CellsUsed()
                .Skip(1) // skip header
                .Select(c => c.GetValue<string>())
                .ToList();

            Assert.Contains("R1C1", plotIds);
            Assert.Contains("R1C2", plotIds);
            Assert.Contains("R2C1", plotIds);
            Assert.Contains("R2C2", plotIds);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExportToExcel_MapSheet_HasGridCells()
    {
        string path = Path.Combine(Path.GetTempPath(), $"trial_test_{Guid.NewGuid()}.xlsx");
        try
        {
            var trialMap = MakeTrialMap(2, 3);
            new TrialExcelExporter().ExportToExcel(trialMap, MakeGrid(2, 3), path);

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet(1); // "Карта (Схема)"

            // Row 2, Col 2 → R1C1
            var cell = ws.Cell(2, 2);
            Assert.Contains("R1C1", cell.GetValue<string>());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Edge cases
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExportToExcel_NullGrid_ExportsDataSheetOnly()
    {
        string path = Path.Combine(Path.GetTempPath(), $"trial_test_{Guid.NewGuid()}.xlsx");
        try
        {
            var trialMap = MakeTrialMap(2, 2);
            new TrialExcelExporter().ExportToExcel(trialMap, null, path);

            Assert.True(File.Exists(path));
            using var wb = new XLWorkbook(path);
            // Should have at least the data sheet
            Assert.True(wb.Worksheets.Count >= 1);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExportToExcel_NullTrialMap_Throws()
    {
        var exporter = new TrialExcelExporter();
        Assert.Throws<ArgumentNullException>(() =>
            exporter.ExportToExcel(null!, null, "/tmp/test.xlsx"));
    }

    [Fact]
    public void ExportToExcel_EmptyAssignments_CreatesEmptyDataSheet()
    {
        string path = Path.Combine(Path.GetTempPath(), $"trial_test_{Guid.NewGuid()}.xlsx");
        try
        {
            var trialMap = new TrialMap { PlotAssignments = new() };
            new TrialExcelExporter().ExportToExcel(trialMap, null, path);

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet(1); // Only data sheet since no grid
            // Row 1 = header only
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            Assert.Equal(1, lastRow);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
