using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using PlotManager.Core.Models;
using PlotManager.Core.Services;
using System;
using System.Collections.Generic;

namespace PlotManager.UI.Views.Controls;

public partial class FieldPlacementPanelView : UserControl
{
    private readonly GridGenerator _gridGenerator = new();

    private PlotGrid? _logicalGrid;
    private TrialMap? _logicalTrialMap;

    public PlotGrid? CurrentGrid => _logicalGrid;
    public TrialMap? PlacedTrialMap { get; private set; }
    public bool IsValid => PlacedTrialMap != null;
    public event EventHandler? PlacementChanged;

    public FieldPlacementPanelView()
    {
        InitializeComponent();
    }

    private Window? GetParentWindow() => this.GetVisualRoot() as Window;

    public void SetRestoredGrid(PlotGrid grid)
    {
        _logicalGrid = grid;
        PlacedTrialMap = _logicalTrialMap ?? new TrialMap
        {
            TrialName = "Restored"
        };
        PlacementChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetLogicalDesign(PlotGrid? logicalGrid, TrialMap? logicalTrialMap)
    {
        _logicalTrialMap = logicalTrialMap;
        _logicalGrid = logicalGrid;

        PlacedTrialMap = null;
        LblStatus.Text = "Оновіть фізичні координати для завантаженого дизайну.";
        LblStatus.Foreground = new SolidColorBrush(Color.Parse("#B0BEC5")); // TextSecondary
        
        // Emulating button style reset ("Classes="accent")
        BtnApply.Classes.Remove("success");
        BtnApply.Classes.Add("accent");
    }

    private async void OnApplyCoordinates(object? sender, RoutedEventArgs e)
    {
        var owner = GetParentWindow();

        if (_logicalGrid == null || _logicalTrialMap == null)
        {
            if (owner != null)
                await MessageBoxHelper.ShowWarning(owner, "Спочатку створіть Дизайн Досліду на кроці 2.");
            return;
        }

        try
        {
            var parameters = new GridGenerator.GridParams
            {
                Rows = _logicalGrid.Rows,
                Columns = _logicalGrid.Columns,
                PlotWidthMeters = _logicalGrid.PlotWidthMeters,
                PlotLengthMeters = _logicalGrid.PlotLengthMeters,
                BufferWidthMeters = _logicalGrid.BufferWidthMeters,
                BufferLengthMeters = _logicalGrid.BufferLengthMeters,
                Origin = new GeoPoint(
                    (double)(NumLatitude.Value ?? 50.0m),
                    (double)(NumLongitude.Value ?? 30.0m)),
                HeadingDegrees = (double)(NumHeading.Value ?? 0m)
            };

            var physicalGrid = _gridGenerator.Generate(parameters);

            var newAssignments = new Dictionary<string, string>(_logicalTrialMap.PlotAssignments);

            PlacedTrialMap = new TrialMap
            {
                TrialName = _logicalTrialMap.TrialName,
                PlotAssignments = newAssignments
            };

            LblStatus.Text = $"✅ Схема успішно прив'язана до ({parameters.Origin.Latitude:F6}, {parameters.Origin.Longitude:F6})";
            LblStatus.Foreground = new SolidColorBrush(Color.Parse("#4CAF50")); // AccentGreen

            BtnApply.Classes.Remove("accent");
            BtnApply.Classes.Add("success");

            PlacementChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            if (owner != null)
                await MessageBoxHelper.ShowError(owner, $"Помилка прив'язки:\n{ex.Message}");
        }
    }
}
