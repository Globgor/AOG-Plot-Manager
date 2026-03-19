using Avalonia.Controls;
using PlotManager.Core.Models;
using PlotManager.Core.Services;
using PlotManager.Core.Protocol;

namespace PlotManager.UI.Views;

public partial class PassMonitorWindow : Window
{
    public bool IsClosed { get; private set; }

    public PassMonitorWindow(
        PlotModeController plotController,
        SensorHub sensorHub,
        SectionController sectionController,
        PassTracker passTracker,
        AogUdpClient aogClient,
        PlotGrid? grid,
        TrialMap? trialMap,
        PrimeController primeController,
        CleanController cleanController,
        AutoWeatherService autoWeather,
        TrialLogger trialLogger,
        PlotLogger? plotLogger)
    {
        InitializeComponent();
        Closed += (_, _) => IsClosed = true;
    }
}
