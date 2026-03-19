using Avalonia.Controls;
using PlotManager.Core.Models;

namespace PlotManager.UI.Views;

public partial class MachineProfileWindow : Window
{
    public MachineProfile Profile { get; private set; }

    public MachineProfileWindow(MachineProfile profile)
    {
        Profile = profile;
        InitializeComponent();
    }
}
