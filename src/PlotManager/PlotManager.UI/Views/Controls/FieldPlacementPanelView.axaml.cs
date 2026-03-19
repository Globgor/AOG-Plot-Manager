using Avalonia.Controls;
using PlotManager.Core.Models;

namespace PlotManager.UI.Views.Controls;

public partial class FieldPlacementPanelView : UserControl
{
    public bool IsValid => PlacedTrialMap != null;
public PlotManager.Core.Models.TrialMap? PlacedTrialMap { get; private set; }
public PlotGrid? RestoredGrid { get; private set; }
public PlotGrid? CurrentGrid => RestoredGrid;
public event System.EventHandler? PlacementChanged;
public void SetLogicalTrialMap(PlotManager.Core.Models.TrialMap m) { }
public void SetRestoredGrid(PlotGrid g) { RestoredGrid = g; PlacementChanged?.Invoke(this, System.EventArgs.Empty); }

    public FieldPlacementPanelView() { InitializeComponent(); }
}
