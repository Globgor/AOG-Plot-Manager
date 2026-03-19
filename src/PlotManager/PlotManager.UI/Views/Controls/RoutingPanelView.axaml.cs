using Avalonia.Controls;
using PlotManager.Core.Models;

namespace PlotManager.UI.Views.Controls;

public partial class RoutingPanelView : UserControl
{
    public bool IsValid => CurrentRouting != null;
public PlotManager.Core.Models.HardwareRouting? CurrentRouting { get; private set; }
public event System.EventHandler? RoutingChanged;
public void SetTrialMap(PlotManager.Core.Models.TrialMap m) { }
public void SetRouting(PlotManager.Core.Models.HardwareRouting r) { CurrentRouting = r; RoutingChanged?.Invoke(this, System.EventArgs.Empty); }

    public RoutingPanelView() { InitializeComponent(); }
}
