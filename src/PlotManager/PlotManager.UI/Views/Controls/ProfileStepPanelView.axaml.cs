using Avalonia.Controls;
using PlotManager.Core.Models;

namespace PlotManager.UI.Views.Controls;

public partial class ProfileStepPanelView : UserControl
{
    public bool IsValid => _profile != null;
public PlotManager.Core.Models.MachineProfile? Profile => _profile;
private PlotManager.Core.Models.MachineProfile? _profile;
public event System.EventHandler? ProfileChanged;
public void SetProfile(PlotManager.Core.Models.MachineProfile p) { _profile = p; ProfileChanged?.Invoke(this, System.EventArgs.Empty); }

    public ProfileStepPanelView() { InitializeComponent(); }
}
