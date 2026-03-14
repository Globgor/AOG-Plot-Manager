using PlotManager.Core.Models;
using PlotManager.Core.Models.Hardware;
using PlotManager.Core.Providers;
using System;

namespace PlotManager.Core.Services;

/// <summary>
/// Orchestrates the hardware routing. 
/// Translates logical "Apply Product X" commands into physical valve activations 
/// via IMachineControlProvider based on the HardwareRoutingProfile.
/// </summary>
public class HardwareRouter
{
    private readonly IMachineControlProvider _machineControlProvider;
    private HardwareRoutingProfile _currentProfile;

    public HardwareRouter(IMachineControlProvider machineControlProvider, HardwareRoutingProfile profile)
    {
        _machineControlProvider = machineControlProvider;
        _currentProfile = profile;
    }

    public void UpdateProfile(HardwareRoutingProfile profile)
    {
        _currentProfile = profile;
    }

    public void ApplyProduct(Product? product)
    {
        if (!_machineControlProvider.IsConnected) return;

        if (product == null)
        {
            // Turn off everything
            _machineControlProvider.SetActiveValves(Array.Empty<int>());
            return;
        }

        var activeValves = _currentProfile.GetValvesForProduct(product);
        _machineControlProvider.SetActiveValves(activeValves);
    }
}
