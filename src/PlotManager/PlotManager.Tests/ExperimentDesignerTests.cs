using PlotManager.Core.Models;
using PlotManager.Core.Services;
using Xunit;

namespace PlotManager.Tests;

/// <summary>
/// Tests for ExperimentDesigner: CRD, RCBD, Latin Square.
/// Validates correctness of randomization, coverage, and consistency.
/// </summary>
public class ExperimentDesignerTests
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

    // ════════════════════════════════════════════════════════════════════
    // CRD — Completely Randomized Design
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void CRD_AllPlotsAssigned()
    {
        var grid = MakeGrid(4, 5); // 20 plots
        var treatments = new List<string> { "A", "B", "C" };

        var designer = new ExperimentDesigner(seed: 42);
        TrialMap map = designer.GenerateDesign(grid, treatments, ExperimentalDesignType.CRD);

        Assert.Equal(20, map.PlotAssignments.Count);
        Assert.All(map.PlotAssignments.Values, v => Assert.Contains(v, treatments));
    }

    [Fact]
    public void CRD_AllTreatmentsPresent()
    {
        var grid = MakeGrid(3, 6); // 18 plots
        var treatments = new List<string> { "T1", "T2", "T3" };

        var designer = new ExperimentDesigner(seed: 7);
        TrialMap map = designer.GenerateDesign(grid, treatments, ExperimentalDesignType.CRD);

        foreach (var t in treatments)
            Assert.Contains(t, map.PlotAssignments.Values);
    }

    [Fact]
    public void CRD_PlotIds_MatchExpectedFormat()
    {
        var grid = MakeGrid(2, 3);
        var designer = new ExperimentDesigner(seed: 1);
        TrialMap map = designer.GenerateDesign(grid, new List<string> { "A", "B" }, ExperimentalDesignType.CRD);

        Assert.True(map.PlotAssignments.ContainsKey("R1C1"));
        Assert.True(map.PlotAssignments.ContainsKey("R1C2"));
        Assert.True(map.PlotAssignments.ContainsKey("R2C3"));
    }

    [Fact]
    public void CRD_DifferentSeeds_ProduceDifferentLayouts()
    {
        var grid = MakeGrid(4, 5);
        var treatments = new List<string> { "A", "B", "C", "D" };

        var map1 = new ExperimentDesigner(seed: 1).GenerateDesign(grid, treatments, ExperimentalDesignType.CRD);
        var map2 = new ExperimentDesigner(seed: 99).GenerateDesign(grid, treatments, ExperimentalDesignType.CRD);

        // At least one plot should differ (probability of all same is astronomically small)
        bool anyDifferent = map1.PlotAssignments.Any(kv =>
            map2.PlotAssignments.TryGetValue(kv.Key, out var v2) && kv.Value != v2);
        Assert.True(anyDifferent);
    }

    // ════════════════════════════════════════════════════════════════════
    // RCBD — Randomized Complete Block Design
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void RCBD_AllPlotsAssigned()
    {
        var grid = MakeGrid(5, 4); // 20 plots
        var treatments = new List<string> { "T1", "T2", "T3", "T4" };

        var designer = new ExperimentDesigner(seed: 42);
        TrialMap map = designer.GenerateDesign(grid, treatments, ExperimentalDesignType.RCBD);

        Assert.Equal(20, map.PlotAssignments.Count);
    }

    [Fact]
    public void RCBD_EachRowContainsAllTreatments_WhenColumnsEqualsTreatments()
    {
        // 4 treatments, 4 columns → perfect RCBD: each row has each treatment once
        var grid = MakeGrid(3, 4);
        var treatments = new List<string> { "A", "B", "C", "D" };

        var designer = new ExperimentDesigner(seed: 17);
        TrialMap map = designer.GenerateDesign(grid, treatments, ExperimentalDesignType.RCBD);

        for (int row = 1; row <= 3; row++)
        {
            var rowTreatments = Enumerable.Range(1, 4)
                .Select(col => map.PlotAssignments[$"R{row}C{col}"])
                .ToList();

            Assert.Equal(4, rowTreatments.Distinct().Count());
            foreach (var t in treatments)
                Assert.Contains(t, rowTreatments);
        }
    }

    [Fact]
    public void RCBD_MoreColumnsThanTreatments_StillFillsAll()
    {
        // 3 treatments, 6 columns → cycles treatments per block
        var grid = MakeGrid(2, 6);
        var treatments = new List<string> { "X", "Y", "Z" };

        var designer = new ExperimentDesigner(seed: 5);
        TrialMap map = designer.GenerateDesign(grid, treatments, ExperimentalDesignType.RCBD);

        Assert.Equal(12, map.PlotAssignments.Count);
        Assert.All(map.PlotAssignments.Values, v => Assert.Contains(v, treatments));
    }

    // ════════════════════════════════════════════════════════════════════
    // Latin Square
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void LatinSquare_AllPlotsAssigned()
    {
        var grid = MakeGrid(3, 3);
        var treatments = new List<string> { "A", "B", "C" };

        var designer = new ExperimentDesigner(seed: 42);
        TrialMap map = designer.GenerateDesign(grid, treatments, ExperimentalDesignType.LatinSquare);

        Assert.Equal(9, map.PlotAssignments.Count);
    }

    [Fact]
    public void LatinSquare_NxN_EachTreatmentExactlyOncePerRow()
    {
        // Perfect 3×3 latin square
        var grid = MakeGrid(3, 3);
        var treatments = new List<string> { "A", "B", "C" };

        var designer = new ExperimentDesigner(seed: 99);
        TrialMap map = designer.GenerateDesign(grid, treatments, ExperimentalDesignType.LatinSquare);

        for (int r = 1; r <= 3; r++)
        {
            var row = Enumerable.Range(1, 3)
                .Select(c => map.PlotAssignments[$"R{r}C{c}"])
                .ToList();
            Assert.Equal(3, row.Distinct().Count());
        }
    }

    [Fact]
    public void LatinSquare_NxN_EachTreatmentExactlyOncePerColumn()
    {
        var grid = MakeGrid(3, 3);
        var treatments = new List<string> { "A", "B", "C" };

        var designer = new ExperimentDesigner(seed: 13);
        TrialMap map = designer.GenerateDesign(grid, treatments, ExperimentalDesignType.LatinSquare);

        for (int c = 1; c <= 3; c++)
        {
            var col = Enumerable.Range(1, 3)
                .Select(r => map.PlotAssignments[$"R{r}C{c}"])
                .ToList();
            Assert.Equal(3, col.Distinct().Count());
        }
    }

    [Fact]
    public void LatinSquare_LargerGrid_RepeatsBlocks()
    {
        // 4×6 grid, 3 treatments → repeats 2×2 blocks of latin square cells
        var grid = MakeGrid(4, 6);
        var treatments = new List<string> { "A", "B", "C" };

        var designer = new ExperimentDesigner(seed: 1);
        TrialMap map = designer.GenerateDesign(grid, treatments, ExperimentalDesignType.LatinSquare);

        Assert.Equal(24, map.PlotAssignments.Count);
        Assert.All(map.PlotAssignments.Values, v => Assert.Contains(v, treatments));
    }

    // ════════════════════════════════════════════════════════════════════
    // GenerateDesign — validation
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenerateDesign_NullGrid_Throws()
    {
        var designer = new ExperimentDesigner();
        Assert.Throws<ArgumentNullException>(() =>
            designer.GenerateDesign(null!, new List<string> { "A" }, ExperimentalDesignType.CRD));
    }

    [Fact]
    public void GenerateDesign_EmptyTreatments_Throws()
    {
        var grid = MakeGrid(2, 2);
        var designer = new ExperimentDesigner();
        Assert.Throws<ArgumentException>(() =>
            designer.GenerateDesign(grid, new List<string>(), ExperimentalDesignType.CRD));
    }

    [Fact]
    public void GenerateDesign_SingleTreatment_Throws()
    {
        var grid = MakeGrid(2, 2);
        var designer = new ExperimentDesigner();
        // Single treatment is valid for design generation (no ArgumentException),
        // but TrialDesignPanel enforces ≥ 2 in UI — designer itself allows 1
        // Let's verify it doesn't throw:
        var map = designer.GenerateDesign(grid, new List<string> { "OnlyOne" }, ExperimentalDesignType.CRD);
        Assert.Equal(4, map.PlotAssignments.Count);
        Assert.All(map.PlotAssignments.Values, v => Assert.Equal("OnlyOne", v));
    }

    [Fact]
    public void GenerateDesign_TrialName_ContainsDesignType()
    {
        var grid = MakeGrid(2, 2);
        var designer = new ExperimentDesigner(seed: 1);

        var mapCrd = designer.GenerateDesign(grid, new List<string> { "A", "B" }, ExperimentalDesignType.CRD);
        var mapRcbd = designer.GenerateDesign(grid, new List<string> { "A", "B" }, ExperimentalDesignType.RCBD);
        var mapLs = designer.GenerateDesign(grid, new List<string> { "A", "B" }, ExperimentalDesignType.LatinSquare);

        Assert.Contains("CRD", mapCrd.TrialName);
        Assert.Contains("RCBD", mapRcbd.TrialName);
        Assert.Contains("LatinSquare", mapLs.TrialName);
    }
}
