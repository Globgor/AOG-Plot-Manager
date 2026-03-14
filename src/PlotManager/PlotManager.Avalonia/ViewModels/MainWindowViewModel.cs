using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlotManager.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private MapViewModel _mapViewModel;

    [ObservableProperty]
    private SpeedometerViewModel _speedometerViewModel;

    [ObservableProperty]
    private HardwareRoutingViewModel _routingViewModel;

    [ObservableProperty]
    private bool _isShowingRouting;

    public MainWindowViewModel()
    {
        _speedometerViewModel = new SpeedometerViewModel();
        _mapViewModel = new MapViewModel(_speedometerViewModel);

        var defaultProfile = new PlotManager.Core.Models.Hardware.HardwareRoutingProfile();
        defaultProfile.Tanks.Add(new PlotManager.Core.Models.Hardware.Tank { Name = "Main Tank", ConnectedValveIds = new System.Collections.Generic.List<int> { 1, 2, 3 } });
        _routingViewModel = new HardwareRoutingViewModel(defaultProfile);
        _routingViewModel.OnCloseRequest = () => IsShowingRouting = false;
    }

    [RelayCommand]
    private void SetGpsProvider(string providerType)
    {
        // TODO: Implement actual provider switching logic
        System.Diagnostics.Debug.WriteLine($"Selected GPS Provider: {providerType}");
    }

    [RelayCommand]
    private void SetMachineProvider(string providerType)
    {
        // TODO: Implement actual provider switching logic
        System.Diagnostics.Debug.WriteLine($"Selected Machine Provider: {providerType}");
    }

    [RelayCommand]
    private void ShowHardwareRouting()
    {
        IsShowingRouting = true;
    }
}
