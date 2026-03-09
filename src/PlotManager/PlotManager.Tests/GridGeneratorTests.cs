using PlotManager.Core.Models;
using PlotManager.Core.Services;
using Xunit;

namespace PlotManager.Tests;

/// <summary>
/// Tests for GridGenerator — verifies grid creation with correct geometry.
/// </summary>
public class GridGeneratorTests
{
    private readonly GridGenerator _generator = new();

    [Fact]
    public void Generate_CreatesCorrectNumberOfPlots()
    {
        // Arrange
        var parameters = new GridGenerator.GridParams
        {
            Rows = 4,
            Columns = 3,
            PlotWidthMeters = 3.0,
            PlotLengthMeters = 10.0,
            Origin = new GeoPoint(50.0, 30.0),
        };

        // Act
        PlotGrid grid = _generator.Generate(parameters);

        // Assert
        Assert.Equal(4, grid.Rows);
        Assert.Equal(3, grid.Columns);
        Assert.Equal(12, grid.TotalPlots);
    }

    [Fact]
    public void Generate_PlotsHaveCorrectDimensions()
    {
        // Arrange
        var parameters = new GridGenerator.GridParams
        {
            Rows = 2,
            Columns = 2,
            PlotWidthMeters = 5.0,
            PlotLengthMeters = 15.0,
            Origin = new GeoPoint(50.0, 30.0),
        };

        // Act
        PlotGrid grid = _generator.Generate(parameters);

        // Assert
        Plot firstPlot = grid.Plots[0, 0];
        Assert.Equal(5.0, firstPlot.WidthMeters);
        Assert.Equal(15.0, firstPlot.LengthMeters);
    }

    [Fact]
    public void Generate_FirstPlotContainsOrigin()
    {
        // Arrange
        var origin = new GeoPoint(50.0, 30.0);
        var parameters = new GridGenerator.GridParams
        {
            Rows = 2,
            Columns = 2,
            PlotWidthMeters = 5.0,
            PlotLengthMeters = 10.0,
            Origin = origin,
        };

        // Act
        PlotGrid grid = _generator.Generate(parameters);

        // Assert — origin should be inside the first plot
        Plot firstPlot = grid.Plots[0, 0];
        Assert.True(firstPlot.Contains(origin));
    }

    [Fact]
    public void Generate_InvalidParams_Throws()
    {
        // Arrange
        var parameters = new GridGenerator.GridParams
        {
            Rows = 0, // Invalid!
            Columns = 3,
            PlotWidthMeters = 3.0,
            PlotLengthMeters = 10.0,
            Origin = new GeoPoint(50.0, 30.0),
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _generator.Generate(parameters));
    }

    [Fact]
    public void Generate_PlotsDoNotOverlap()
    {
        // Arrange
        var parameters = new GridGenerator.GridParams
        {
            Rows = 3,
            Columns = 3,
            PlotWidthMeters = 5.0,
            PlotLengthMeters = 10.0,
            BufferWidthMeters = 1.0,
            BufferLengthMeters = 2.0,
            Origin = new GeoPoint(50.0, 30.0),
        };

        // Act
        PlotGrid grid = _generator.Generate(parameters);

        // Assert — adjacent plots should not overlap
        for (int row = 0; row < grid.Rows; row++)
        {
            for (int col = 0; col < grid.Columns - 1; col++)
            {
                Plot current = grid.Plots[row, col];
                Plot next = grid.Plots[row, col + 1];

                // Current NE longitude should be <= next SW longitude
                Assert.True(current.NorthEast.Longitude <= next.SouthWest.Longitude,
                    $"Plots [{row},{col}] and [{row},{col + 1}] overlap horizontally.");
            }
        }
    }
}
