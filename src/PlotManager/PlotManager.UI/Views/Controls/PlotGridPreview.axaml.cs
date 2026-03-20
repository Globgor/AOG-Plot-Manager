using Avalonia.Controls;
using PlotManager.Core.Models;

namespace PlotManager.UI.Views.Controls;

public partial class PlotGridPreview : UserControl
{
    private PlotGrid? _grid;
    private TrialMap? _trialMap;

    public PlotGridPreview()
    {
        InitializeComponent();
    }

    public void SetGrid(PlotGrid? grid)
    {
        _grid = grid;
        InvalidateVisual();
    }

    public void SetTrialMap(TrialMap? map)
    {
        _trialMap = map;
        InvalidateVisual();
    }
}
