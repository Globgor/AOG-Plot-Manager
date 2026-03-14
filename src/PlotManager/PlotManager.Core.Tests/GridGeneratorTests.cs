using PlotManager.Core.Models;
using PlotManager.Core.Services;
using Xunit;
using System;

namespace PlotManager.Core.Tests.Services;

public class GridGeneratorTests
{
    private readonly GridGenerator _generator = new();

    [Fact]
    public void Generate_Near180thMeridian_ShouldWrapLongitudePerfectly()
    {
        // Arrange
        var originNearDateline = new GeoPoint(45.0, 179.9999);
        
        var parameters = new GridGenerator.GridParams
        {
            Rows = 1,
            Columns = 10,
            PlotWidthMeters = 30, // Pushing it firmly across the dateline
            PlotLengthMeters = 10,
            Origin = originNearDateline,
            HeadingDegrees = 0.0 // North (Columns offset East)
        };

        // Act
        var grid = _generator.Generate(parameters);

        // Assert
        var lastPlot = grid.Plots[0, 9];
        
        // The Longitude must have wrapped back to -179.99... instead of going to 180.001
        Assert.InRange(lastPlot.NorthEast.Longitude, -180.0, 180.0);
        Assert.True(lastPlot.NorthEast.Longitude < 0, "Longitude failed to wrap across the 180th meridian and became an invalid WGS84 coordinate.");
    }
}
