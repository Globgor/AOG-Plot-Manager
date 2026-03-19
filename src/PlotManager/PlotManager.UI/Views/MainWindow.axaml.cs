using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using PlotManager.Core.Models;
using PlotManager.Core.Protocol;
using PlotManager.Core.Services;
using PlotManager.UI.Views.Controls;
using System;
using System.IO;

namespace PlotManager.UI.Views;

public partial class MainWindow : Window
{
    // ── Wizard step names ──
    private static readonly string[] WizardSteps =
        { "Профіль", "Дизайн Досліду", "Локація (GPS)", "Routing" };

    // ── Wizard panels (lazy-created) ──
    private WelcomePanel? _welcomePanel;
    private ProfileStepPanelView? _profileStep;
    private TrialDesignPanelView? _designStep;
    private FieldPlacementPanelView? _placementStep;
    private RoutingPanelView? _routingStep;

    // ── Core services ──
    private SpatialEngine? _spatialEngine;
    private SectionController? _sectionController;
    private SensorHub? _sensorHub;
    private PassTracker? _passTracker;
    private PlotModeController? _plotController;
    private AogUdpClient? _aogClient;
    private PrimeController? _primeController;
    private CleanController? _cleanController;
    private AutoWeatherService? _autoWeather;
    private TrialLogger? _trialLogger;
    private PlotLogger? _plotLogger;
    private PassMonitorWindow? _passMonitorWindow;

    // ── State ──
    private bool _isOnWelcome = true;
    private int _currentStep;
    private readonly SessionService _sessionSvc = new();
    private DispatcherTimer? _healthPollTimer;

    public MainWindow()
    {
        InitializeComponent();
        ShowWelcome();
        Closing += OnWindowClosing;
    }

    // ════════════════════════════════════════════════════════════════════
    // Navigation
    // ════════════════════════════════════════════════════════════════════

    private void ShowWelcome()
    {
        _isOnWelcome = true;
        NavBar.IsVisible = false;

        _welcomePanel ??= new WelcomePanel();
        _welcomePanel.NewSetupRequested     += (_, _) => EnterWizard(loadProfile: false);
        _welcomePanel.LoadProfileRequested   += (_, _) => EnterWizard(loadProfile: true);
        _welcomePanel.ResumeSessionRequested += (_, path) => ResumeSession(path);

        ContentArea.Content = _welcomePanel;
        SetStatus("Ласкаво просимо — оберіть дію для початку");
    }

    private async void EnterWizard(bool loadProfile)
    {
        _isOnWelcome = false;

        // Ensure step panels are created
        EnsureStepPanels();

        if (loadProfile)
        {
            var dlg = new OpenFileDialog
            {
                Filters = { new FileDialogFilter { Name = "Machine Profile", Extensions = { "json" } } },
                Title   = "Завантажити профіль машини",
            };
            var files = await dlg.ShowAsync(this);
            if (files is { Length: > 0 })
            {
                try
                {
                    var loaded = MachineProfile.LoadFromFile(files[0]);
                    _profileStep!.SetProfile(loaded);
                }
                catch (Exception ex)
                {
                    await MessageBoxHelper.ShowError(this, ex.Message);
                }
            }
        }
        else
        {
            var profile = MachineProfile.CreateDefault();
            var editorWin = new MachineProfileWindow(profile);
            var ok = await editorWin.ShowDialog<bool>(this);
            if (ok)
                _profileStep!.SetProfile(editorWin.Profile);
        }

        ShowStep(0);
    }

    private void EnsureStepPanels()
    {
        if (_profileStep != null) return;

        _profileStep  = new ProfileStepPanelView();
        _designStep   = new TrialDesignPanelView();
        _placementStep = new FieldPlacementPanelView();
        _routingStep  = new RoutingPanelView();

        _profileStep.ProfileChanged  += (_, _) => UpdateNavState();
        _designStep.DesignChanged    += (_, _) =>
        {
            if (_designStep.CurrentTrialMap != null)
                _placementStep.SetLogicalTrialMap(_designStep.CurrentTrialMap);
            UpdateNavState();
        };
        _placementStep.PlacementChanged += (_, _) =>
        {
            if (_placementStep.PlacedTrialMap != null)
                _routingStep.SetTrialMap(_placementStep.PlacedTrialMap);
            UpdateNavState();
        };
        _routingStep.RoutingChanged += (_, _) => UpdateNavState();
    }

    private void ShowStep(int step)
    {
        EnsureStepPanels();
        _currentStep = Math.Clamp(step, 0, WizardSteps.Length - 1);
        NavBar.IsVisible = true;
        NavBar.CurrentStep = _currentStep;

        ContentArea.Content = _currentStep switch
        {
            0 => (Control)_profileStep!,
            1 => _designStep!,
            2 => _placementStep!,
            3 => _routingStep!,
            _ => _profileStep!,
        };

        UpdateNavState();
        SetStatus(WizardSteps[_currentStep]);
    }

    private void UpdateNavState()
    {
        NavBar.SetBackVisible(_currentStep > 0);

        if (_currentStep < WizardSteps.Length - 1)
        {
            NavBar.SetNextText("Далі →");
            NavBar.SetNextAccent(false);
            bool canAdvance = _currentStep switch
            {
                0 => _profileStep?.IsValid ?? false,
                1 => _designStep?.IsValid  ?? false,
                2 => _placementStep?.IsValid ?? false,
                _ => true,
            };
            NavBar.SetNextEnabled(canAdvance);
        }
        else
        {
            NavBar.SetNextText("🚀 Запуск");
            NavBar.SetNextAccent(true); // green
            NavBar.SetNextEnabled(_routingStep?.IsValid ?? false);
        }
    }

    private void OnBack(object? sender, EventArgs e)
    {
        if (_currentStep > 0)
            ShowStep(_currentStep - 1);
        else
            ShowWelcome();
    }

    private void OnNext(object? sender, EventArgs e)
    {
        if (_currentStep < WizardSteps.Length - 1)
            ShowStep(_currentStep + 1);
        else
            LaunchPassMonitor();
    }

    // ════════════════════════════════════════════════════════════════════
    // Launch Pass Monitor
    // ════════════════════════════════════════════════════════════════════

    private void LaunchPassMonitor()
    {
        if (_passMonitorWindow != null && !_passMonitorWindow.IsClosed)
        {
            _passMonitorWindow.Activate();
            return;
        }

        var machineProfile = _profileStep?.Profile;
        var placedMap      = _placementStep?.PlacedTrialMap;
        var currentGrid    = _placementStep?.CurrentGrid;    // PlotGrid is separate from TrialMap
        var currentRouting = _routingStep?.CurrentRouting;

        // Lazy-init logger
        _plotLogger ??= CreateLogger();

        _spatialEngine     ??= new SpatialEngine();
        _sectionController ??= new SectionController(logger: _plotLogger);
        _sensorHub         ??= new SensorHub(logger: _plotLogger);
        _passTracker       ??= new PassTracker(logger: _plotLogger);
        _aogClient         ??= new AogUdpClient(logger: _plotLogger);

        if (_plotController == null)
        {
            _plotController = new PlotModeController(
                _spatialEngine, _sectionController, _aogClient, logger: _plotLogger);
            _plotController.WireInterlocks(_sensorHub);

            if (machineProfile != null)
            {
                var hwSetup       = machineProfile.ToHardwareSetup();
                var delayProvider = machineProfile.CreateBoomDelayProvider();
                _plotController.SetHardwareSetup(hwSetup, delayProvider);
                machineProfile.ApplyToSpatialEngine(_spatialEngine);
                machineProfile.ApplyToSectionController(_sectionController);
            }
        }

        if (currentGrid != null && placedMap != null && currentRouting != null)
        {
            _spatialEngine.Configure(currentGrid, placedMap, currentRouting);
            _passTracker.Configure(currentGrid);
        }

        _primeController ??= new PrimeController(logger: _plotLogger);
        _cleanController ??= new CleanController(logger: _plotLogger);
        _autoWeather     ??= new AutoWeatherService();
        _trialLogger     ??= new TrialLogger();

        if (_plotController.Transport != null)
        {
            _primeController.SetTransport(_plotController.Transport);
            _cleanController.SetTransport(_plotController.Transport);
        }

        _passMonitorWindow = new PassMonitorWindow(
            _plotController, _sensorHub, _sectionController,
            _passTracker, _aogClient, currentGrid, placedMap,
            _primeController, _cleanController, _autoWeather, _trialLogger,
            _plotLogger);

        _passMonitorWindow.Show(this);
        SetStatus("Pass Monitor запущено");
        StartHealthPolling();

        // Auto-save session
        if (currentGrid != null)
            AutoSaveSession(GridToParams(currentGrid), currentRouting, placedMap.TrialName);
    }

    private PlotLogger CreateLogger()
    {
        var logger = new PlotLogger();
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AOGPlotManager", "logs");
        logger.StartSession(logDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}");
        return logger;
    }

    private static GridGenerator.GridParams GridToParams(PlotGrid g) => new()
    {
        Origin             = g.Origin,
        HeadingDegrees     = g.HeadingDegrees,
        Rows               = g.Rows,
        Columns            = g.Columns,
        PlotWidthMeters    = g.PlotWidthMeters,
        PlotLengthMeters   = g.PlotLengthMeters,
        BufferWidthMeters  = g.BufferWidthMeters,
        BufferLengthMeters = g.BufferLengthMeters,
    };

    private void AutoSaveSession(GridGenerator.GridParams? gp, HardwareRouting? routing, string? trialName)
    {
        if (gp == null && routing == null) return;
        try
        {
            var session = new FieldSession { SessionName = trialName ?? "Session", TrialName = trialName };
            if (gp != null) session.SetFromGridParams(gp);
            if (routing != null) session.SetFromRouting(routing);
            _sessionSvc.Save(session);
        }
        catch (Exception ex)
        {
            _plotLogger?.Warn("Session", $"Auto-save failed: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Resume Session
    // ════════════════════════════════════════════════════════════════════

    private async void ResumeSession(string sessionPath)
    {
        FieldSession session;
        try { session = _sessionSvc.Load(sessionPath); }
        catch (Exception ex)
        {
            await MessageBoxHelper.ShowError(this, $"Не вдалося завантажити сесію:\n{ex.Message}");
            return;
        }

        EnsureStepPanels();
        var routing = session.ToHardwareRouting();
        _routingStep!.SetRouting(routing);

        try
        {
            var gp   = session.ToGridParams();
            var grid = new GridGenerator().Generate(gp);
            _placementStep!.SetRestoredGrid(grid);
        }
        catch { /* proceed without grid */ }

        var dlg = new OpenFileDialog
        {
            Filters = { new FileDialogFilter { Name = "Machine Profile", Extensions = { "json" } } },
            Title   = "Завантажити профіль машини (або скасуйте для продовження без профілю)",
        };
        var files = await dlg.ShowAsync(this);
        if (files is { Length: > 0 })
        {
            try { _profileStep!.SetProfile(MachineProfile.LoadFromFile(files[0])); }
            catch { /* ignore */ }
        }

        _isOnWelcome = false;
        NavBar.IsVisible = false;
        SetStatus($"Відновлено сесію: {session.SessionName}");
        LaunchPassMonitor();
    }

    // ════════════════════════════════════════════════════════════════════
    // Status bar + Health
    // ════════════════════════════════════════════════════════════════════

    private void SetStatus(string text)
    {
        Dispatcher.UIThread.Post(() => StatusLabel.Text = text);
    }

    private void StartHealthPolling()
    {
        if (_healthPollTimer != null) return;
        _healthPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _healthPollTimer.Tick += (_, _) => UpdateHealthStatus();
        _healthPollTimer.Start();
    }

    private void UpdateHealthStatus()
    {
        if (_sensorHub == null && _aogClient == null) return;

        var sensorHealth     = _sensorHub?.Health ?? ServiceHealth.Healthy;
        var aogHealth        = _aogClient?.Health ?? ServiceHealth.Healthy;
        bool estop           = _sensorHub?.LatestSnapshot.IsEstop == true;
        bool telemetryStale  = _sensorHub?.LatestSnapshot.IsStale == true;

        if (estop && _sectionController != null && !_sectionController.EmergencyStopActive)
        {
            _sectionController.ActivateEmergencyStop();
            _plotLogger?.Error("UI", "Teensy E-STOP detected via telemetry");
        }

        ServiceHealth worst = ServiceHealth.Healthy;
        if (sensorHealth == ServiceHealth.Failed || aogHealth == ServiceHealth.Failed)
            worst = ServiceHealth.Failed;
        else if (sensorHealth == ServiceHealth.Degraded || aogHealth == ServiceHealth.Degraded)
            worst = ServiceHealth.Degraded;

        if (estop)
        {
            HealthLabel.Text       = "🛑 E-STOP ACTIVE";
            HealthLabel.Foreground = new SolidColorBrush(Color.Parse("#F44336"));
        }
        else if (worst == ServiceHealth.Failed)
        {
            HealthLabel.Text       = $"❌ Service failed (SEN:{sensorHealth} UDP:{aogHealth})";
            HealthLabel.Foreground = new SolidColorBrush(Color.Parse("#F44336"));
        }
        else if (worst == ServiceHealth.Degraded || telemetryStale)
        {
            string stale = telemetryStale ? " Telemetry stale" : "";
            HealthLabel.Text       = $"⚠ Degraded{stale}";
            HealthLabel.Foreground = new SolidColorBrush(Color.Parse("#FFA726"));
        }
        else
        {
            HealthLabel.Text       = $"✔ Healthy | Log:{_plotLogger?.EntryCount ?? 0}";
            HealthLabel.Foreground = new SolidColorBrush(Color.Parse("#4CAF50"));
        }
    }

    private async void OnViewLog(object? sender, RoutedEventArgs e)
    {
        if (_plotLogger?.FilePath == null || !File.Exists(_plotLogger.FilePath))
        {
            await MessageBoxHelper.ShowInfo(this, "Лог ще не створено.");
            return;
        }
        var logWin = new LogViewerWindow(_plotLogger.FilePath);
        logWin.Show(this);
    }

    // ════════════════════════════════════════════════════════════════════
    // Cleanup
    // ════════════════════════════════════════════════════════════════════

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _passMonitorWindow?.Close();
        _plotController?.Dispose();
        _sensorHub?.Dispose();
        _aogClient?.Dispose();
        _cleanController?.Dispose();
        _trialLogger?.Dispose();
        _autoWeather?.Dispose();
        _healthPollTimer?.Stop();
        _plotLogger?.StopSession();
    }
}
