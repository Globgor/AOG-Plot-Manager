// Workflow: UI Modernization | Task: MainForm Wizard
using PlotManager.Core.Models;
using PlotManager.Core.Protocol;
using PlotManager.Core.Services;
using PlotManager.UI.Controls;

namespace PlotManager.UI.Forms;

/// <summary>
/// Main application window — wizard-style flow:
///   Step 0: Welcome (splash)
///   Step 1: Machine Profile
///   Step 2: Grid Setup + Preview
///   Step 3: Trial Map CSV Import
///   Step 4: Hardware Routing → Launch Pass Monitor
/// </summary>
public partial class MainForm : Form
{
    // ── Wizard step names (for nav bar) ──
    private static readonly string[] WizardSteps =
        { "Профіль", "Дизайн Досліду", "Локація (GPS)", "Routing" };

    // ── Wizard panels ──
    private WelcomePanel _welcomePanel = null!;
    private WizardNavBar _navBar = null!;
    private Panel _contentPanel = null!;

    private ProfileStepPanel _profileStep = null!;
    private TrialDesignPanel _designStep = null!;
    private FieldPlacementPanel _placementStep = null!;
    private RoutingPanel _routingStep = null!;

    // ── Core services (lazy-init when Pass Monitor opens) ──
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
    private FormPassMonitor? _passMonitorForm;
    private PlotLogger? _plotLogger;
    private System.Windows.Forms.Timer? _healthPollTimer;
    private MachineProfile? _machineProfile;

    // ── Status bar ──
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _healthLabel = null!;
    private Form? _logViewerForm;

    // ── State tracking ──
    private bool _isOnWelcome = true;
    private int _currentStep;

    // ── Session persistence (STORE-1/2) ──
    private readonly SessionService _sessionSvc = new();
    private string? _lastSavedSessionPath;

    public MainForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        // ── Form properties ──
        Text = "AOG Plot Manager v0.2.0";
        Size = new Size(1280, 850);
        MinimumSize = new Size(960, 700);
        StartPosition = FormStartPosition.CenterScreen;
        AppTheme.StyleForm(this);

        // ── Status Strip (bottom) ──
        BuildStatusStrip();
        Controls.Add(_statusStrip);

        // ── Nav Bar (top, hidden initially) ──
        _navBar = new WizardNavBar
        {
            Steps = WizardSteps,
            Visible = false,
        };
        _navBar.BackRequested += OnBack;
        _navBar.NextRequested += OnNext;
        Controls.Add(_navBar);

        // ── Content panel (fills remaining space) ──
        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.BgPrimary,
        };
        Controls.Add(_contentPanel);

        // ── Create all step panels ──
        _welcomePanel = new WelcomePanel();
        _welcomePanel.NewSetupRequested     += (_, _) => EnterWizard(loadProfile: false);
        _welcomePanel.LoadProfileRequested   += (_, _) => EnterWizard(loadProfile: true);
        _welcomePanel.ResumeSessionRequested += (_, path) => ResumeSession(path);

        _profileStep = new ProfileStepPanel();
        _profileStep.ProfileChanged += (_, _) => UpdateNavState();

        _designStep = new TrialDesignPanel();
        _designStep.DesignChanged += (_, _) =>
        {
            if (_designStep.CurrentTrialMap != null && _designStep.CurrentGrid != null)
            {
                _placementStep.SetLogicalTrialMap(_designStep.CurrentGrid, _designStep.CurrentTrialMap);
            }
            UpdateNavState();
        };

        _placementStep = new FieldPlacementPanel();
        _placementStep.PlacementChanged += (_, _) =>
        {
            // Auto-populate routing grid when trial map loaded & placed
            if (_placementStep.PlacedTrialMap != null)
            {
                _routingStep.SetTrialMap(_placementStep.PlacedTrialMap);
            }
            UpdateNavState();
        };

        _routingStep = new RoutingPanel();
        _routingStep.RoutingChanged += (_, _) =>
        {
            // Grid preview removed; routing is stored in _routingStep.CurrentRouting
            UpdateNavState();
        };

        // ── Show welcome ──
        ShowWelcome();

        FormClosing += MainForm_FormClosing;
        ResumeLayout(true);
    }

    // ════════════════════════════════════════════════════════════════════
    // Navigation logic
    // ════════════════════════════════════════════════════════════════════

    private void ShowWelcome()
    {
        _isOnWelcome = true;
        _navBar.Visible = false;
        _contentPanel.Controls.Clear();
        _contentPanel.Controls.Add(_welcomePanel);
        SetStatus("Ласкаво просимо — оберіть дію для початку");
    }

    private void EnterWizard(bool loadProfile)
    {
        _isOnWelcome = false;
        _navBar.Visible = true;

        if (loadProfile)
        {
            // Trigger load from file
            using var dlg = new OpenFileDialog
            {
                Filter = "Machine Profile (*.json)|*.json",
                Title = "Завантажити профіль машини",
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var loaded = MachineProfile.LoadFromFile(dlg.FileName);
                    _profileStep.SetProfile(loaded);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Помилка завантаження:\n{ex.Message}",
                        "❌ Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        else
        {
            // Create default profile and open editor
            var profile = MachineProfile.CreateDefault();
            using var form = new FormMachineProfile(profile);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                _profileStep.SetProfile(form.Profile);
            }
        }

        ShowStep(0);
    }

    private void ShowStep(int step)
    {
        _currentStep = Math.Clamp(step, 0, WizardSteps.Length - 1);
        _navBar.CurrentStep = _currentStep;

        _contentPanel.Controls.Clear();
        UserControl panel = _currentStep switch
        {
            0 => _profileStep,
            1 => _designStep,
            2 => _placementStep,
            3 => _routingStep,
            _ => _profileStep,
        };
        _contentPanel.Controls.Add(panel);

        UpdateNavState();
        SetStatus(WizardSteps[_currentStep]);
    }

    private void UpdateNavState()
    {
        // Back button: hidden on step 0
        _navBar.SetBackVisible(_currentStep > 0);

        // Next button text & enabled state
        if (_currentStep < WizardSteps.Length - 1)
        {
            _navBar.SetNextText("Далі →");
            _navBar.SetNextColor(AppTheme.AccentBlue);

            bool canAdvance = _currentStep switch
            {
                0 => _profileStep.IsValid,
                1 => _designStep.IsValid,
                2 => _placementStep.IsValid,
                _ => true,
            };
            _navBar.SetNextEnabled(canAdvance);
        }
        else
        {
            // Last step: Launch button
            _navBar.SetNextText("🚀 Запуск");
            _navBar.SetNextColor(AppTheme.AccentGreen);
            _navBar.SetNextEnabled(_routingStep.IsValid);
        }
    }

    private void OnBack(object? sender, EventArgs e)
    {
        if (_currentStep > 0)
            ShowStep(_currentStep - 1);
        else
            ShowWelcome(); // UI-2 fix: back from step 0 returns to welcome
    }

    private void OnNext(object? sender, EventArgs e)
    {
        if (_currentStep < WizardSteps.Length - 1)
        {
            ShowStep(_currentStep + 1);
        }
        else
        {
            // Last step → Launch Pass Monitor
            LaunchPassMonitor();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Pass Monitor launch (preserved from original)
    // ════════════════════════════════════════════════════════════════════

    private void LaunchPassMonitor()
    {
        if (_passMonitorForm != null && !_passMonitorForm.IsDisposed)
        {
            _passMonitorForm.BringToFront();
            return;
        }

        _machineProfile = _profileStep.Profile;
        // PlacedTrialMap contains the plot assignments, PlacedGrid contains the physical grid
        var currentGrid = _placementStep.PlacedGrid;
        var currentTrialMap = _placementStep.PlacedTrialMap;
        var currentRouting = _routingStep.CurrentRouting;

        // Lazy-init Core services
        if (_plotLogger == null)
        {
            _plotLogger = new PlotLogger();
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AOGPlotManager", "logs");
            _plotLogger.StartSession(logDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}");
        }

        _spatialEngine ??= new SpatialEngine();
        _sectionController ??= new SectionController(logger: _plotLogger);
        _sensorHub ??= new SensorHub(logger: _plotLogger);
        _passTracker ??= new PassTracker(logger: _plotLogger);
        _aogClient ??= new AogUdpClient(logger: _plotLogger);

        if (_plotController == null)
        {
            _plotController = new PlotModeController(
                _spatialEngine, _sectionController, _aogClient, logger: _plotLogger);
            _plotController.WireInterlocks(_sensorHub);

            if (_machineProfile != null)
            {
                var hwSetup = _machineProfile.ToHardwareSetup();
                var delayProvider = _machineProfile.CreateBoomDelayProvider();
                _plotController.SetHardwareSetup(hwSetup, delayProvider);
                _machineProfile.ApplyToSpatialEngine(_spatialEngine);
            }
        }

        if (currentGrid != null && currentTrialMap != null && currentRouting != null)
        {
            _spatialEngine.Configure(currentGrid, currentTrialMap, currentRouting);
            _passTracker.Configure(currentGrid);
        }

        _primeController ??= new PrimeController(logger: _plotLogger);
        _cleanController ??= new CleanController(logger: _plotLogger);
        _autoWeather ??= new AutoWeatherService();
        _trialLogger ??= new TrialLogger();

        if (_plotController.Transport != null)
        {
            _primeController.SetTransport(_plotController.Transport);
            _cleanController.SetTransport(_plotController.Transport);
        }

        _passMonitorForm = new FormPassMonitor(
            _plotController, _sensorHub, _sectionController,
            _passTracker, _aogClient, currentGrid, currentTrialMap,
            _primeController, _cleanController, _autoWeather, _trialLogger,
            _plotLogger);

        _passMonitorForm.Show();
        SetStatus("Pass Monitor запущено");
        StartHealthPolling();

        // Auto-save session for 'Resume' on next launch (STORE-1/2)
        AutoSaveSession(
            currentGrid != null ? GridToParams(currentGrid) : null,
            currentRouting,
            currentTrialMap?.TrialName);
    }

    private static GridGenerator.GridParams? GridToParams(PlotGrid g) =>
        new()
        {
            Origin           = g.Origin,
            HeadingDegrees   = g.HeadingDegrees,
            Rows             = g.Rows,
            Columns          = g.Columns,
            PlotWidthMeters  = g.PlotWidthMeters,
            PlotLengthMeters = g.PlotLengthMeters,
            BufferWidthMeters  = g.BufferWidthMeters,
            BufferLengthMeters = g.BufferLengthMeters,
        };

    private void AutoSaveSession(
        GridGenerator.GridParams? gp,
        HardwareRouting? routing,
        string? trialName)
    {
        if (gp == null && routing == null) return;
        try
        {
            var session = new FieldSession
            {
                SessionName = trialName ?? "Session",
                TrialName   = trialName,
            };
            if (gp != null) session.SetFromGridParams(gp);
            if (routing != null) session.SetFromRouting(routing);

            _lastSavedSessionPath = _sessionSvc.Save(session);
            _plotLogger?.Info("Session", $"Session auto-saved: {_lastSavedSessionPath}");
        }
        catch (Exception ex)
        {
            _plotLogger?.Warn("Session", $"Auto-save failed: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Resume Session (loads FieldSession → skip wizard → launch monitor)
    // ════════════════════════════════════════════════════════════════════

    private void ResumeSession(string sessionPath)
    {
        FieldSession session;
        try
        {
            session = _sessionSvc.Load(sessionPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не вдалося завантажити сесію:\n{ex.Message}",
                "❌ Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Rebuild routing from session
        var routing = session.ToHardwareRouting();

        // Rebuild grid from session params
        PlotGrid? grid = null;
        try
        {
            var gp = session.ToGridParams();
            grid = new GridGenerator().Generate(gp);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не вдалося відновити grid:\n{ex.Message}",
                "⚠ Попередження", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Update wizard steps silently (so validation can pass)
        _routingStep.SetRouting(routing);
        if (grid != null)
            _placementStep.SetRestoredGrid(grid);

        // Ask whether to load a machine profile
        using var dlg = new OpenFileDialog
        {
            Filter = "Machine Profile (*.json)|*.json",
            Title   = "Завантажити профіль машини (або скасуйте для продовження без профілю)",
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var loaded = MachineProfile.LoadFromFile(dlg.FileName);
                _profileStep.SetProfile(loaded);
            }
            catch { /* ignore — proceed without profile */ }
        }

        // Skip wizard, go straight to PassMonitor
        _isOnWelcome = false;
        _navBar.Visible = false;
        SetStatus($"Відновлено сесію: {session.SessionName}");
        LaunchPassMonitor();
    }

    // ════════════════════════════════════════════════════════════════════
    // Status Strip
    // ════════════════════════════════════════════════════════════════════

    private void BuildStatusStrip()
    {
        _statusStrip = new StatusStrip
        {
            BackColor = AppTheme.BgSecondary,
            ForeColor = AppTheme.TextSecondary,
        };

        _statusLabel = new ToolStripStatusLabel("Готово")
        {
            Spring = true,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = AppTheme.TextSecondary,
        };
        _statusStrip.Items.Add(_statusLabel);

        _healthLabel = new ToolStripStatusLabel("✔ All services healthy")
        {
            ForeColor = AppTheme.AccentGreen,
            Font = AppTheme.FontSmall,
        };
        _statusStrip.Items.Add(_healthLabel);

        var btnViewLog = new ToolStripButton("📝 Log")
        {
            Alignment = ToolStripItemAlignment.Right,
            Font = AppTheme.FontSmall,
            ForeColor = AppTheme.TextSecondary,
        };
        btnViewLog.Click += (_, _) =>
        {
            if (_plotLogger?.FilePath != null && File.Exists(_plotLogger.FilePath))
                ShowLogViewer();
            else
                MessageBox.Show("Лог ще не створено.", "Журнал",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        _statusStrip.Items.Add(btnViewLog);
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    // ════════════════════════════════════════════════════════════════════
    // Health Polling
    // ════════════════════════════════════════════════════════════════════

    private void StartHealthPolling()
    {
        if (_healthPollTimer != null) return;
        _healthPollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _healthPollTimer.Tick += (_, _) => UpdateHealthStatus();
        _healthPollTimer.Start();
    }

    private void UpdateHealthStatus()
    {
        if (_sensorHub == null && _aogClient == null) return;

        var sensorHealth = _sensorHub?.Health ?? ServiceHealth.Healthy;
        var aogHealth = _aogClient?.Health ?? ServiceHealth.Healthy;
        bool estop = _sensorHub?.LatestSnapshot.IsEstop == true;
        bool telemetryStale = _sensorHub?.LatestSnapshot.IsStale == true;

        if (estop && _sectionController != null && !_sectionController.EmergencyStopActive)
        {
            _sectionController.ActivateEmergencyStop();
            _plotLogger?.Error("UI",
                "Teensy E-STOP detected via telemetry — activating SectionController E-STOP");
        }

        ServiceHealth worst = ServiceHealth.Healthy;
        if (sensorHealth == ServiceHealth.Failed || aogHealth == ServiceHealth.Failed)
            worst = ServiceHealth.Failed;
        else if (sensorHealth == ServiceHealth.Degraded || aogHealth == ServiceHealth.Degraded)
            worst = ServiceHealth.Degraded;

        if (estop)
        {
            _healthLabel.Text = "🛑 E-STOP ACTIVE";
            _healthLabel.ForeColor = AppTheme.AccentRed;
        }
        else if (worst == ServiceHealth.Failed)
        {
            _healthLabel.Text = $"❌ Service failed (SEN:{sensorHealth} UDP:{aogHealth})";
            _healthLabel.ForeColor = AppTheme.AccentRed;
        }
        else if (worst == ServiceHealth.Degraded || telemetryStale)
        {
            string staleText = telemetryStale ? " Telemetry stale" : "";
            _healthLabel.Text = $"⚠ Degraded (SEN:{sensorHealth} UDP:{aogHealth}){staleText}";
            _healthLabel.ForeColor = AppTheme.AccentOrange;
        }
        else
        {
            _healthLabel.Text = $"✔ Healthy (SEN:OK UDP:OK) Log:{_plotLogger?.EntryCount ?? 0}";
            _healthLabel.ForeColor = AppTheme.AccentGreen;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Log Viewer
    // ════════════════════════════════════════════════════════════════════

    private void ShowLogViewer()
    {
        if (_logViewerForm != null && !_logViewerForm.IsDisposed)
        {
            _logViewerForm.BringToFront();
            return;
        }

        string? logPath = _plotLogger?.FilePath;
        if (string.IsNullOrEmpty(logPath)) return;

        _logViewerForm = new Form
        {
            Text = $"📝 Diagnostic Log — {Path.GetFileName(logPath)}",
            Size = new Size(900, 600),
            StartPosition = FormStartPosition.CenterParent,
        };
        AppTheme.StyleForm(_logViewerForm);

        var txtLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = AppTheme.BgSecondary,
            ForeColor = AppTheme.TextPrimary,
            Font = AppTheme.FontMono,
            WordWrap = false,
            BorderStyle = BorderStyle.None,
        };

        if (File.Exists(logPath))
            txtLog.Text = File.ReadAllText(logPath);

        var btnRefresh = new Button
        {
            Text = "🔄 Оновити",
            Dock = DockStyle.Bottom,
            Height = 36,
        };
        AppTheme.StyleButton(btnRefresh, AppTheme.AccentBlue);
        string capturedLogPath = logPath;
        btnRefresh.Click += (_, _) =>
        {
            if (File.Exists(capturedLogPath))
            {
                txtLog.Text = File.ReadAllText(capturedLogPath);
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
        };

        _logViewerForm.Controls.Add(txtLog);
        _logViewerForm.Controls.Add(btnRefresh);
        _logViewerForm.Show(this);
    }

    // ════════════════════════════════════════════════════════════════════
    // Cleanup
    // ════════════════════════════════════════════════════════════════════

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_passMonitorForm != null && !_passMonitorForm.IsDisposed)
            _passMonitorForm.Close();
        _passMonitorForm = null;

        if (_logViewerForm != null && !_logViewerForm.IsDisposed)
            _logViewerForm.Close();
        _logViewerForm = null;

        _plotController?.Dispose();
        _sensorHub?.Dispose();
        _aogClient?.Dispose();
        _cleanController?.Dispose();
        _trialLogger?.Dispose();
        _autoWeather?.Dispose();
        _healthPollTimer?.Stop();
        _healthPollTimer?.Dispose();
        _plotLogger?.StopSession();
    }
}
