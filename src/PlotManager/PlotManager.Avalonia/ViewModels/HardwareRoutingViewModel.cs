using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlotManager.Core.Models;
using PlotManager.Core.Models.Hardware;
using System.Collections.ObjectModel;
using System;

namespace PlotManager.Avalonia.ViewModels;

public partial class HardwareRoutingViewModel : ViewModelBase
{
    private readonly HardwareRoutingProfile _profile;

    public Action? OnCloseRequest { get; set; }

    [ObservableProperty]
    private string _profileName;

    public ObservableCollection<TankViewModel> Tanks { get; } = new();
    
    public ObservableCollection<Product> AvailableProducts { get; } = new();

    public HardwareRoutingViewModel(HardwareRoutingProfile profile)
    {
        _profile = profile;
        ProfileName = profile.Name;

        AvailableProducts.Add(new Product { Id = "P1", Name = "Water" });
        AvailableProducts.Add(new Product { Id = "P2", Name = "Herbicide A" });
        AvailableProducts.Add(new Product { Id = "P3", Name = "Fungicide B" });

        foreach (var tank in profile.Tanks)
        {
            Tanks.Add(new TankViewModel(tank));
        }
    }

    [RelayCommand]
    private void AddTank()
    {
        Tanks.Add(new TankViewModel(new Tank { Name = "New Tank" }));
    }

    [RelayCommand]
    private void SaveAndClose()
    {
        _profile.Name = ProfileName;
        _profile.Tanks.Clear();
        foreach (var tankVm in Tanks)
        {
            tankVm.ApplyChanges();
            _profile.Tanks.Add(tankVm.GetTank());
        }
        OnCloseRequest?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        OnCloseRequest?.Invoke();
    }
}
