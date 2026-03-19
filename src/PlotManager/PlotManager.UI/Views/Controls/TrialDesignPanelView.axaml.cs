using Avalonia.Controls;
using PlotManager.Core.Models;

namespace PlotManager.UI.Views.Controls;

public partial class TrialDesignPanelView : UserControl
{
    public bool IsValid => CurrentTrialMap != null;
public PlotManager.Core.Models.TrialMap? CurrentTrialMap { get; private set; }
public PlotGrid? CurrentGrid { get; private set; }
public event System.EventHandler? DesignChanged;

    public TrialDesignPanelView() { InitializeComponent(); }
}
