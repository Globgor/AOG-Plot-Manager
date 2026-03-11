using System.Drawing;
using System.Windows.Forms;

using PlotManager.Core.Models;
using PlotManager.Core.Services;
using PlotManager.UI.Controls;

namespace PlotManager.UI.Forms;

/// <summary>
/// Real-time pass monitor dashboard.
/// Assembles 5 panels: PlotMapControl (spatial map), BoomStatusPanel (valve LEDs),
/// TelemetryPanel (sensors), InterlockStatusBar (safety indicators),
/// OperationsToolbar (Prime, Clean, Trial controls).
///
/// All Core events are marshalled to the UI thread via BeginInvoke.
/// </summary>
public class FormPassMonitor : Form
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

    // ── UI controls ──
    private readonly PlotMapControl _mapControl;
    private readonly BoomStatusPanel _boomPanel;
    private readonly TelemetryPanel _telemetryPanel;
    private readonly InterlockStatusBar _interlockBar;

    // ── Phase 5: Operational controls ──
    private Button? _btnPrime;
    private Button? _btnClean;
    private Button? _btnTrial;
    private Button? _btnSettings;
    private Label? _lblTrialStatus;

    // ── Phase 6: Field context panel ──
    private readonly FieldContextPanel _fieldContextPanel;

    // ── Polling timer for interlock states ──
    private readonly System.Windows.Forms.Timer _pollTimer;

    // ── Track GPS age for AOG stale detection ──
    private DateTime _lastGpsTime = DateTime.MinValue;

    public FormPassMonitor(
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
        IPlotLogger? logger = null)
    {
        _plotController = plotController ?? throw new ArgumentNullException(nameof(plotController));
        _sensorHub = sensorHub ?? throw new ArgumentNullException(nameof(sensorHub));
        _sectionController = sectionController ?? throw new ArgumentNullException(nameof(sectionController));
        _passTracker = passTracker ?? throw new ArgumentNullException(nameof(passTracker));
        _aogClient = aogClient ?? throw new ArgumentNullException(nameof(aogClient));
        _grid = grid;
        _trialMap = trialMap;
        _primeController = primeController;
        _cleanController = cleanController;
        _autoWeather = autoWeather;
        _trialLogger = trialLogger;
        _logger = logger;

        // ── Form setup ──
        Text = "Pass Monitor — AOG Plot Manager";
        Size = new Size(1280, 800);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(25, 25, 30);
        ForeColor = Color.FromArgb(220, 220, 220);
        Font = new Font("Segoe UI", 9f);

        // ── Create controls ──
        _boomPanel = new BoomStatusPanel
        {
            Dock = DockStyle.Top,
            Height = 60,
        };

        _interlockBar = new InterlockStatusBar
        {
            Dock = DockStyle.Bottom,
            Height = 40,
        };

        _telemetryPanel = new TelemetryPanel
        {
            Dock = DockStyle.Right,
            Width = 280,
            MinSafePressureBar = _sectionController.MinSafeAirPressureBar,
        };

        _mapControl = new PlotMapControl
        {
            Dock = DockStyle.Fill,
        };

        // ── Phase 6: Field context panel ──
        _fieldContextPanel = new FieldContextPanel
        {
            Dock = DockStyle.Left,
            Width = 260,
        };

        // ── Phase 5: Operations toolbar ──
        var opsToolbar = CreateOperationsToolbar();

        // ── Assembly order matters for docking ──
        Controls.Add(_mapControl);       // Fill (added first, fills remaining)
        Controls.Add(_fieldContextPanel); // Left
        Controls.Add(_telemetryPanel);   // Right
        Controls.Add(opsToolbar);        // Top (below boom panel)
        Controls.Add(_boomPanel);        // Top
        Controls.Add(_interlockBar);     // Bottom

        // ── Configure map with grid + trial map ──
        if (_grid != null && _trialMap != null)
        {
            _mapControl.SetGridAndMap(_grid, _trialMap);
        }

        // ── Wire Core events ──
        _plotController.OnSpatialUpdate += HandleSpatialUpdate;
        _plotController.OnValveMaskSent += HandleValveMaskSent;
        _sensorHub.OnTelemetryUpdated += HandleTelemetryUpdated;

        // ── Phase 5: Wire AutoWeather events ──
        if (_autoWeather != null)
        {
            _autoWeather.OnWeatherFetchRequired += HandleWeatherFetchRequired;
        }

        // ── Phase 5: Wire Prime/Clean error display ──
        if (_primeController != null)
            _primeController.OnPrimeError += msg => BeginInvokeIfAlive(() =>
                MessageBox.Show(this, msg, "Prime Error", MessageBoxButtons.OK, MessageBoxIcon.Warning));
        if (_cleanController != null)
            _cleanController.OnCleanError += msg => BeginInvokeIfAlive(() =>
                MessageBox.Show(this, msg, "Clean Error", MessageBoxButtons.OK, MessageBoxIcon.Warning));

        // ── Polling timer for interlock states (10 Hz) ──
        _pollTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _pollTimer.Tick += PollInterlockStates;
        _pollTimer.Start();
    }

    // ════════════════════════════════════════════════════════════════════
    // Phase 5: Operations Toolbar
    // ════════════════════════════════════════════════════════════════════

    private Panel CreateOperationsToolbar()
    {
        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = Color.FromArgb(35, 35, 40),
            Padding = new Padding(6, 4, 6, 4),
        };

        int x = 8;

        // ── Prime button (hold-to-prime) ──
        _btnPrime = new Button
        {
            Text = "🚿 Prime",
            Location = new Point(x, 6),
            Size = new Size(100, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 136, 229),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Enabled = _primeController != null,
        };
        _btnPrime.FlatAppearance.BorderSize = 0;
        _btnPrime.MouseDown += (_, _) =>
        {
            if (_primeController == null) return;
            double speed = _sensorHub.LatestSnapshot.IsStale ? 0 : 0; // Speed comes from GPS
            bool plotMode = _plotController.PlotModeEnabled;
            _primeController.StartPrime(speed, plotMode);
        };
        _btnPrime.MouseUp += (_, _) => _primeController?.StopPrime();
        toolbar.Controls.Add(_btnPrime);
        x += 108;

        // ── Clean button ──
        _btnClean = new Button
        {
            Text = "🧹 Clean",
            Location = new Point(x, 6),
            Size = new Size(100, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(255, 152, 0),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Enabled = _cleanController != null,
        };
        _btnClean.FlatAppearance.BorderSize = 0;
        _btnClean.Click += (_, _) =>
        {
            if (_cleanController == null) return;
            if (_cleanController.IsCleaning)
            {
                _cleanController.StopClean();
                _btnClean.Text = "🧹 Clean";
                _btnClean.BackColor = Color.FromArgb(255, 152, 0);
            }
            else
            {
                // Clean all 10 boom channels
                bool plotMode = _plotController.PlotModeEnabled;
                var channels = Enumerable.Range(0, 10);
                if (_cleanController.StartClean(channels, 0, plotMode))
                {
                    _btnClean.Text = "⏹ Stop";
                    _btnClean.BackColor = Color.FromArgb(244, 67, 54);
                }
            }
        };
        toolbar.Controls.Add(_btnClean);
        x += 108;

        // ── Separator ──
        var sep = new Label
        {
            Text = "|",
            Location = new Point(x, 10),
            Size = new Size(10, 20),
            ForeColor = Color.FromArgb(80, 80, 80),
        };
        toolbar.Controls.Add(sep);
        x += 14;

        // ── Trial toggle button ──
        _btnTrial = new Button
        {
            Text = "📝 Start Trial",
            Location = new Point(x, 6),
            Size = new Size(130, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(76, 175, 80),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Enabled = _trialLogger != null,
        };
        _btnTrial.FlatAppearance.BorderSize = 0;
        _btnTrial.Click += HandleTrialToggle;
        toolbar.Controls.Add(_btnTrial);
        x += 138;

        // ── Trial status label ──
        _lblTrialStatus = new Label
        {
            Text = "Trial: idle",
            Location = new Point(x, 12),
            Size = new Size(350, 20),
            ForeColor = Color.FromArgb(150, 150, 150),
            Font = new Font("Segoe UI", 8.5f),
        };
        toolbar.Controls.Add(_lblTrialStatus);
        x += 260;

        // ── Settings button ──
        _btnSettings = new Button
        {
            Text = "⚙ Settings",
            Location = new Point(x, 6),
            Size = new Size(100, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(66, 66, 75),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9),
        };
        _btnSettings.FlatAppearance.BorderSize = 0;
        _btnSettings.Click += (_, _) =>
        {
            using var dlg = new FormOperationSettings(
                _sectionController, _primeController, _cleanController,
                _autoWeather, _trialLogger);
            dlg.ShowDialog(this);
        };
        toolbar.Controls.Add(_btnSettings);

        return toolbar;
    }

    // ════════════════════════════════════════════════════════════════════
    // Phase 5: Trial Start/Stop
    // ════════════════════════════════════════════════════════════════════

    private void HandleTrialToggle(object? sender, EventArgs e)
    {
        if (_trialLogger == null) return;

        if (_trialLogger.IsActive)
        {
            // Stop trial
            _trialLogger.StopSession();
            _btnTrial!.Text = "📝 Start Trial";
            _btnTrial.BackColor = Color.FromArgb(76, 175, 80);
            _lblTrialStatus!.Text = "Trial: stopped";
            _logger?.Info("UI", "Trial session stopped");
        }
        else
        {
            // Open weather dialog first
            using var weatherForm = new FormWeatherSnapshot();
            if (weatherForm.ShowDialog(this) != DialogResult.OK || weatherForm.Result == null)
                return;

            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AOGPlotManager", "trials");

            string trialName = _trialMap?.TrialName ?? "Unknown";
            _trialLogger.StartSession(logDir, trialName, weatherForm.Result);

            _btnTrial!.Text = "⏹ Stop Trial";
            _btnTrial.BackColor = Color.FromArgb(244, 67, 54);
            _lblTrialStatus!.Text = $"Trial: recording — {trialName}";
            _logger?.Info("UI", $"Trial session started: {trialName}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Event Handlers (marshalled to UI thread)
    // ════════════════════════════════════════════════════════════════════

    private void HandleSpatialUpdate(SpatialResult result)
    {
        if (IsDisposed || !IsHandleCreated) return;

        // Phase 5: Feed trial logger with spatial state (on background thread — thread-safe)
        if (_trialLogger?.IsActive == true)
        {
            var gps = _plotController.LastGps;
            string? plotId = result.ActivePlot != null
                ? $"R{result.ActivePlot.Row}C{result.ActivePlot.Column}"
                : null;
            _trialLogger.UpdateState(
                gps?.Latitude ?? 0, gps?.Longitude ?? 0,
                gps?.Heading ?? 0, gps?.SpeedKmh ?? 0,
                plotId, result.ActiveProduct,
                result.ValveMask, result.State,
                _sensorHub.LatestSnapshot);
        }

        // Phase 5: Feed speed to AutoWeather
        _autoWeather?.UpdateSpeed(_plotController.LastGps?.SpeedKmh ?? 0);

        BeginInvoke(() =>
        {
            // Update map
            if (_grid != null)
            {
                _mapControl.UpdateSprayer(result, _plotController.LastGps, _grid);
            }

            // Update boom panel
            _boomPanel.UpdateValveMask(result.ValveMask);
            if (result.ActiveProduct != null)
            {
                string rateText = _passTracker.GetStatusText();
                _boomPanel.UpdateProduct(result.ActiveProduct, rateText);
            }

            // Phase 6: Update field context panel
            var gpsData = _plotController.LastGps;
            double speed = gpsData?.SpeedKmh ?? 0;
            double targetSpeed = _sectionController.TargetSpeedKmh;
            _fieldContextPanel.UpdateSpatial(result, speed, targetSpeed);
            _fieldContextPanel.UpdateGps(
                gpsData?.Latitude ?? 0,
                gpsData?.Longitude ?? 0,
                gpsData?.Heading ?? 0);

            // Track GPS time for stale detection
            _lastGpsTime = DateTime.UtcNow;
        });
    }

    private void HandleValveMaskSent(ushort mask)
    {
        if (IsDisposed || !IsHandleCreated) return;

        BeginInvoke(() =>
        {
            _boomPanel.UpdateValveMask(mask);
        });
    }

    private void HandleTelemetryUpdated(SensorSnapshot snapshot)
    {
        if (IsDisposed || !IsHandleCreated) return;

        BeginInvoke(() =>
        {
            _telemetryPanel.UpdateSnapshot(snapshot);
        });
    }

    private void HandleWeatherFetchRequired()
    {
        BeginInvokeIfAlive(() =>
        {
            // AutoWeather triggered: open weather dialog
            using var weatherForm = new FormWeatherSnapshot();
            weatherForm.ShowDialog(this);
            if (weatherForm.Result != null)
            {
                _autoWeather?.ResetTrigger();
                _logger?.Info("AutoWeather", $"Weather captured: {weatherForm.Result.TemperatureC}°C, {weatherForm.Result.HumidityPercent}%");
            }
        });
    }

    private void PollInterlockStates(object? sender, EventArgs e)
    {
        // Update interlock status bar from SectionController properties
        _interlockBar.UpdateFromController(_sectionController);

        // Teensy status from SensorHub
        var snap = _sensorHub.LatestSnapshot;
        _interlockBar.UpdateTeensyStatus(snap.IsStale, false);

        // AOG GPS staleness (>2s since last GPS update)
        bool aogStale = (DateTime.UtcNow - _lastGpsTime).TotalSeconds > 2.0;
        _interlockBar.UpdateAogStatus(aogStale);

        // C2+U2 FIX: Update service health indicators
        _interlockBar.UpdateServiceHealth(_sensorHub.Health, _aogClient.Health);

        // Phase 5: Update trial status
        if (_trialLogger?.IsActive == true && _lblTrialStatus != null)
        {
            _lblTrialStatus.Text = $"📝 Recording: {_trialLogger.RecordCount} records";
        }

        // Phase 5: Update clean button state
        if (_cleanController != null && _btnClean != null)
        {
            if (_cleanController.IsCleaning)
                _btnClean.Text = $"⏹ Stop ({_cleanController.RemainingCycles})";
        }

        // Phase 6: Update field context panel (low-frequency data)
        _fieldContextPanel.UpdatePass(_passTracker.CurrentPass);

        // Trial/logger status
        string loggerStatus = _trialLogger?.IsActive == true ? "recording" : "idle";
        long logEntries = _logger is PlotLogger pl ? pl.EntryCount : 0;
        _fieldContextPanel.UpdateTrialStatus(
            _trialLogger?.IsActive ?? false,
            _trialMap?.TrialName,
            _trialLogger?.RecordCount ?? 0);
        _fieldContextPanel.UpdateLoggerStatus(loggerStatus, logEntries);

        // Next product look-ahead
        if (_trialMap != null && _passTracker.CurrentPass?.IsActive == true)
        {
            int nextCol = _passTracker.CurrentPass.ColumnIndex + 1;
            if (_grid != null && nextCol < _grid.Columns)
            {
                // Find first plot in next column and get its product
                string nextPlotId = $"R1C{nextCol + 1}";
                string? nextProduct = _trialMap.GetProduct(nextPlotId);
                _fieldContextPanel.SetNextProduct(nextProduct);
            }
            else
            {
                _fieldContextPanel.SetNextProduct(null);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    private void BeginInvokeIfAlive(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(action);
    }

    // ════════════════════════════════════════════════════════════════════
    // Cleanup
    // ════════════════════════════════════════════════════════════════════

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();

        // Unsubscribe from Core events
        _plotController.OnSpatialUpdate -= HandleSpatialUpdate;
        _plotController.OnValveMaskSent -= HandleValveMaskSent;
        _sensorHub.OnTelemetryUpdated -= HandleTelemetryUpdated;

        // Phase 5: Unsubscribe operational events
        if (_autoWeather != null)
            _autoWeather.OnWeatherFetchRequired -= HandleWeatherFetchRequired;

        // Phase 5: Stop trial if running
        if (_trialLogger?.IsActive == true)
            _trialLogger.StopSession();

        base.OnFormClosing(e);
    }
}
