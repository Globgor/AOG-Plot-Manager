using Avalonia.Controls;
using Avalonia.Interactivity;
using PlotManager.Core.Services;
using System;

namespace PlotManager.UI.Views;

public partial class OperationSettingsWindow : Window
{
    private readonly SectionController _sectionController;
    private readonly PrimeController? _primeController;
    private readonly CleanController? _cleanController;
    private readonly AutoWeatherService? _autoWeather;
    private readonly TrialLogger? _trialLogger;

    public OperationSettingsWindow()
    {
        InitializeComponent();
        
        // This is ONLY for Avalonia XAML designer preview. Do not use at runtime.
        _sectionController = null!;
    }

    public OperationSettingsWindow(
        SectionController sectionController,
        PrimeController? primeController = null,
        CleanController? cleanController = null,
        AutoWeatherService? autoWeather = null,
        TrialLogger? trialLogger = null)
    {
        _sectionController = sectionController ?? throw new ArgumentNullException(nameof(sectionController));
        _primeController = primeController;
        _cleanController = cleanController;
        _autoWeather = autoWeather;
        _trialLogger = trialLogger;

        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        nudTargetSpeed.Value = (decimal)_sectionController.TargetSpeedKmh;
        nudSpeedTolerance.Value = (decimal)(_sectionController.SpeedToleranceFraction * 100);

        if (_primeController != null)
            nudMaxPrimeSpeed.Value = (decimal)_primeController.MaxPrimeSpeedKmh;

        if (_cleanController != null)
        {
            nudPulseOnMs.Value = _cleanController.PulseOnMs;
            nudPulseOffMs.Value = _cleanController.PulseOffMs;
            nudCycleCount.Value = _cleanController.CycleCount;
        }

        if (_autoWeather != null)
        {
            nudWeatherThreshold.Value = _autoWeather.StationaryThresholdMs / 1000m;
            nudStoppedSpeedKmh.Value = (decimal)_autoWeather.StoppedSpeedKmh;
        }

        if (_trialLogger != null)
        {
            nudLogIntervalMs.Value = _trialLogger.LogIntervalMs;
            chkLogAllStates.IsChecked = _trialLogger.LogAllStates;
        }
    }

    private void BtnApply_Click(object? sender, RoutedEventArgs e)
    {
        _sectionController.TargetSpeedKmh = (double)(nudTargetSpeed.Value ?? 5.0m);
        _sectionController.SpeedToleranceFraction = (double)(nudSpeedTolerance.Value ?? 10m) / 100.0;

        if (_primeController != null)
            _primeController.MaxPrimeSpeedKmh = (double)(nudMaxPrimeSpeed.Value ?? 0.5m);

        if (_cleanController != null)
        {
            _cleanController.PulseOnMs = (int)(nudPulseOnMs.Value ?? 2000m);
            _cleanController.PulseOffMs = (int)(nudPulseOffMs.Value ?? 1000m);
            _cleanController.CycleCount = (int)(nudCycleCount.Value ?? 3m);
        }

        if (_autoWeather != null)
        {
            _autoWeather.StationaryThresholdMs = (int)(nudWeatherThreshold.Value ?? 10m) * 1000;
            _autoWeather.StoppedSpeedKmh = (double)(nudStoppedSpeedKmh.Value ?? 0.1m);
        }

        if (_trialLogger != null)
        {
            _trialLogger.LogIntervalMs = (int)(nudLogIntervalMs.Value ?? 1000m);
            _trialLogger.LogAllStates = chkLogAllStates.IsChecked ?? false;
        }

        Close(true);
    }
}
