using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using PlotManager.Core.Models;
using PlotManager.Core.Protocol;
using PlotManager.Core.Services;
using System;
using System.IO;
using System.Linq;

namespace PlotManager.UI.Views;

public partial class PassMonitorWindow : Window
{
    // ── Core dependencies ──
    private readonly PlotModeController _plotController;
    private readonly SensorHub _sensorHub;
    private readonly SectionController _sectionController;
    private readonly PassTracker _passTracker;
    private readonly AogUdpClient _aogClient;
    private readonly PlotGrid? _grid;
    private readonly TrialMap? _trialMap;

    // ── Phase 5: Operational services ──
    private readonly PrimeController? _primeController;
    private readonly CleanController? _cleanController;
    private readonly AutoWeatherService? _autoWeather;
    private readonly TrialLogger? _trialLogger;
    private readonly IPlotLogger? _logger;

    private readonly DispatcherTimer _pollTimer;
    private DateTime _lastGpsTime = DateTime.MinValue;

    public bool IsClosed { get; private set; }

    // ONLY for XAML designer preview
    public PassMonitorWindow()
    {
        InitializeComponent();
    }

    public PassMonitorWindow(
        PlotModeController plotController,
        SensorHub sensorHub,
        SectionController sectionController,
        PassTracker passTracker,
        AogUdpClient aogClient,
        PlotGrid? grid,
        TrialMap? trialMap,
        PrimeController? primeController = null,
        CleanController? cleanController = null,
        AutoWeatherService? autoWeather = null,
        TrialLogger? trialLogger = null,
        IPlotLogger? plotLogger = null)
    {
        _plotController = plotController;
        _sensorHub = sensorHub;
        _sectionController = sectionController;
        _passTracker = passTracker;
        _aogClient = aogClient;
        _grid = grid;
        _trialMap = trialMap;
        _primeController = primeController;
        _cleanController = cleanController;
        _autoWeather = autoWeather;
        _trialLogger = trialLogger;
        _logger = plotLogger;

        InitializeComponent();
        
        Closed += (_, _) => IsClosed = true;

        // Setup control states
        if (_primeController == null) btnPrime.IsEnabled = false;
        if (_cleanController == null) btnClean.IsEnabled = false;
        if (_trialLogger == null) btnTrial.IsEnabled = false;

        // Ensure safe threshold is configured
        telemetryPanel.MinSafePressureBar = _sectionController.MinSafeAirPressureBar;

        // Configure map with grid + trial map
        if (_grid != null && _trialMap != null)
        {
            mapControl.SetGridAndMap(_grid, _trialMap);
        }

        // ── Wire Core events ──
        _plotController.OnSpatialUpdate += HandleSpatialUpdate;
        _plotController.OnValveMaskSent += HandleValveMaskSent;
        _sensorHub.OnTelemetryUpdated += HandleTelemetryUpdated;

        if (_autoWeather != null)
        {
            _autoWeather.OnWeatherFetchRequired += HandleWeatherFetchRequired;
        }

        if (_primeController != null)
        {
            _primeController.OnPrimeError += msg => Dispatcher.UIThread.InvokeAsync(() =>
                ShowErrorDialog("Prime Error", msg));
        }

        if (_cleanController != null)
        {
            _cleanController.OnCleanError += msg => Dispatcher.UIThread.InvokeAsync(() =>
                ShowErrorDialog("Clean Error", msg));
        }

        // ── Polling timer for interlock states (10 Hz) ──
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _pollTimer.Tick += PollInterlockStates;
        _pollTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _pollTimer?.Stop();
        
        if (_plotController != null)
        {
            _plotController.OnSpatialUpdate -= HandleSpatialUpdate;
            _plotController.OnValveMaskSent -= HandleValveMaskSent;
        }
        if (_sensorHub != null)
        {
            _sensorHub.OnTelemetryUpdated -= HandleTelemetryUpdated;
        }
        if (_autoWeather != null)
        {
            _autoWeather.OnWeatherFetchRequired -= HandleWeatherFetchRequired;
        }

        base.OnClosed(e);
    }

    // ════════════════════════════════════════════════════════════════════
    // Trial / Settings / Operations Handlers
    // ════════════════════════════════════════════════════════════════════

    private async void ShowErrorDialog(string title, string msg)
    {
        // For simplicity, just update trial status or logger if no MessageBox is ready
        _logger?.Error(title, msg);
    }

    private void OnPrimePressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (_primeController == null) return;
        double speed = _plotController.LastGps?.SpeedKmh ?? 0;
        bool plotMode = _plotController.PlotModeEnabled;
        _primeController.StartPrime(speed, plotMode);
    }

    private void OnPrimeReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        _primeController?.StopPrime();
    }

    private void OnCleanClick(object? sender, RoutedEventArgs e)
    {
        if (_cleanController == null) return;
        
        if (_cleanController.IsCleaning)
        {
            _cleanController.StopClean();
            btnClean.Content = "🧹 Clean";
            btnClean.Background = SolidColorBrush.Parse("#FF9800");
        }
        else
        {
            // Clean all channels
            bool plotMode = _plotController.PlotModeEnabled;
            var channels = Enumerable.Range(0, 10);
            if (_cleanController.StartClean(channels, 0, plotMode))
            {
                btnClean.Content = "⏹ Stop";
                btnClean.Background = SolidColorBrush.Parse("#F44336");
            }
        }
    }

    private async void OnTrialToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_trialLogger == null) return;

        if (_trialLogger.IsActive)
        {
            _trialLogger.StopSession();
            btnTrial.Content = "📝 Start Trial";
            btnTrial.Background = SolidColorBrush.Parse("#4CAF50");
            lblTrialStatus.Text = "Trial: stopped";
            _logger?.Info("UI", "Trial session stopped");
        }
        else
        {
            var weatherForm = new WeatherSnapshotWindow();
            var result = await weatherForm.ShowDialog<WeatherSnapshot?>(this);
            if (result == null) return;

            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AOGPlotManager", "trials");

            Directory.CreateDirectory(logDir);

            string trialName = _trialMap?.TrialName ?? "Unknown";
            _trialLogger.StartSession(logDir, trialName, result);

            btnTrial.Content = "⏹ Stop Trial";
            btnTrial.Background = SolidColorBrush.Parse("#F44336");
            lblTrialStatus.Text = $"Trial: recording — {trialName}";
            _logger?.Info("UI", $"Trial session started: {trialName}");
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var dlg = new OperationSettingsWindow(
            _sectionController, _primeController, _cleanController,
            _autoWeather, _trialLogger);
        
        await dlg.ShowDialog(this);
    }

    private void OnEstopClick(object? sender, RoutedEventArgs e)
    {
        // 1. Force-close all valves immediately
        _primeController?.ForceStop();
        _sectionController.ActivateEmergencyStop();

        // 2. Send all-zero packet to machine module via AOG channel
        _aogClient.SendSectionControl(0x0000);

        // 3. Stop trial recording
        if (_trialLogger?.IsActive == true)
        {
            _trialLogger.StopSession();
            btnTrial.Content = "📝 Start Trial";
            btnTrial.Background = SolidColorBrush.Parse("#4CAF50");
        }

        // 4. Log the event
        _logger?.Error("ESTOP", "EMERGENCY STOP activated by operator");

        // 5. Visual feedback
        btnEstop.Content = "✔ STOPPED";
        btnEstop.Background = SolidColorBrush.Parse("#505050");
        lblTrialStatus.Text = "⛔ E-STOP activated";

        _logger?.Info("ESTOP", "All valves closed, trial recording stopped");
    }

    // ════════════════════════════════════════════════════════════════════
    // Core Event Handlers
    // ════════════════════════════════════════════════════════════════════

    private void HandleSpatialUpdate(SpatialResult result)
    {
        // Thread-safe logger feed
        if (_trialLogger?.IsActive == true)
        {
            var gps = _plotController.LastGps;
            string? plotId = result.ActivePlot != null
                ? $"R{result.ActivePlot.Row + 1}C{result.ActivePlot.Column + 1}"
                : null;
            _trialLogger.UpdateState(
                gps?.Latitude ?? 0, gps?.Longitude ?? 0,
                gps?.HeadingDegrees ?? 0, gps?.SpeedKmh ?? 0,
                plotId, result.ActiveProduct,
                result.ValveMask, result.State,
                _sensorHub.LatestSnapshot);
        }

        _autoWeather?.UpdateSpeed(_plotController.LastGps?.SpeedKmh ?? 0);

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_grid != null)
            {
                mapControl.UpdateSprayer(result, _plotController.LastGps, _grid);
            }

            boomPanel.UpdateValveMask(result.ValveMask);
            if (result.ActiveProduct != null)
            {
                string rateText = _passTracker.GetStatusText();
                boomPanel.UpdateProduct(result.ActiveProduct, rateText);
                // Map context updates
                fieldContextPanel.UpdateSpatial(result, _plotController.LastGps?.SpeedKmh ?? 0, 0); // target speed should be passed if available
                fieldContextPanel.UpdatePass(_passTracker.CurrentPass);
                if (_plotController.LastGps != null)
                {
                    fieldContextPanel.UpdateGps(_plotController.LastGps.Latitude, _plotController.LastGps.Longitude, _plotController.LastGps.HeadingDegrees);
                }
                fieldContextPanel.SetNextProduct(null);
            }
        });
    }

    private void HandleValveMaskSent(ushort mask)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            interlockBar.UpdateTeensyStatus(stale: false, estop: false); // Teensy RX blink analog
        });
    }

    private void HandleTelemetryUpdated(SensorSnapshot snapshot)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            telemetryPanel.UpdateSnapshot(snapshot);
            interlockBar.UpdateTeensyStatus(stale: false, estop: false); // TX blink analog
        });
    }

    private async void HandleWeatherFetchRequired()
    {
        // Called by AutoWeatherService when conditions are met
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var weatherForm = new WeatherSnapshotWindow();
            var result = await weatherForm.ShowDialog<WeatherSnapshot?>(this);
            // AppendWeatherEvent is not available in TrialLogger, skip for now
        });
    }

    private void PollInterlockStates(object? sender, EventArgs e)
    {
        var gps = _plotController.LastGps;
        double speed = gps?.SpeedKmh ?? 0;
        var rtk = gps?.FixQuality ?? GpsFixQuality.NoFix;

        if (gps != null) _lastGpsTime = DateTime.UtcNow;

        interlockBar.UpdateFromController(_sectionController);
        interlockBar.UpdateAogStatus(stale: (DateTime.UtcNow - _lastGpsTime).TotalSeconds > 2.0);
    }
}
