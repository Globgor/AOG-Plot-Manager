using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace PlotManager.Avalonia.ViewModels;

public partial class SpeedometerViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeedColor))]
    private double _targetSpeed = 6.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeedColor))]
    private double _currentSpeed = 0.0;

    public IBrush SpeedColor
    {
        get
        {
            if (TargetSpeed <= 0) return Brushes.Gray;
            
            double delta = Math.Abs(CurrentSpeed - TargetSpeed);
            double errorPercentage = delta / TargetSpeed;

            if (errorPercentage <= 0.1) // Within 10%
                return Brushes.Green;
            else if (errorPercentage <= 0.2) // Within 20%
                return Brushes.Orange;
            else
                return Brushes.Red;
        }
    }

    public void Update(double targetSpeed, double currentSpeed)
    {
        TargetSpeed = targetSpeed;
        CurrentSpeed = currentSpeed;
    }
}
