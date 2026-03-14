using CommunityToolkit.Mvvm.ComponentModel;
using PlotManager.Core.Models;
using PlotManager.Core.Models.Hardware;
using System.Linq;

namespace PlotManager.Avalonia.ViewModels;

public partial class TankViewModel : ViewModelBase
{
    private readonly Tank _tank;

    public TankViewModel(Tank tank)
    {
        _tank = tank;
        Name = tank.Name;
        ValvesString = string.Join(", ", tank.ConnectedValveIds);
        SelectedProduct = tank.LoadedProduct;
    }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _valvesString;

    [ObservableProperty]
    private Product? _selectedProduct;

    public Tank GetTank() => _tank;

    public void ApplyChanges()
    {
        _tank.Name = Name;
        _tank.LoadedProduct = SelectedProduct;
        _tank.ConnectedValveIds = ValvesString
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .ToList();
    }
}
