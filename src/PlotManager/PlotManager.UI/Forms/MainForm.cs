using PlotManager.Core.Models;
using PlotManager.Core.Protocol;
using PlotManager.Core.Services;
using PlotManager.UI.Controls;

namespace PlotManager.UI.Forms;

/// <summary>
/// Main application window with TabControl for:
///   Tab 1: Grid Setup & Preview
///   Tab 2: Trial Map CSV Import
///   Tab 3: Hardware Routing (Product → Section Mapping)
/// </summary>
public partial class MainForm : Form
{
    // ── Core services ──
    private readonly GridGenerator _gridGenerator = new();
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

    // C1 FIX: Shared logger injected into all services
    private PlotLogger? _plotLogger;

    // UI4: Health polling timer for StatusStrip
    private System.Windows.Forms.Timer? _healthPollTimer;

    // ── State ──
    private PlotGrid? _currentGrid;
    private TrialMap? _currentTrialMap;
    private HardwareRouting? _currentRouting;
    private MachineProfile? _machineProfile;

    // ── Tab 1: Grid Setup Controls ──
    private TabControl _tabControl = null!;
    private NumericUpDown _nudPlotWidth = null!;
    private NumericUpDown _nudPlotLength = null!;
    private NumericUpDown _nudBufferWidth = null!;
    private NumericUpDown _nudBufferLength = null!;
    private NumericUpDown _nudRows = null!;
    private NumericUpDown _nudColumns = null!;
    private NumericUpDown _nudLatitude = null!;
    private NumericUpDown _nudLongitude = null!;
    private NumericUpDown _nudHeading = null!;
    private Button _btnGenerate = null!;
    private PlotGridPreview _gridPreview = null!;
    private Label _lblGridInfo = null!;

    // ── Tab 2: Trial Map Controls ──
    private Button _btnImportCsv = null!;
    private DataGridView _dgvTrialMap = null!;
    private Label _lblTrialInfo = null!;
    private Label _lblCsvFilePath = null!;

    // ── Tab 3: Hardware Routing Controls ──
    private DataGridView _dgvRouting = null!;
    private Button _btnSaveRouting = null!;
    private Label _lblRoutingStatus = null!;

    // ── Status bar ──
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    // U1 FIX: health + log viewer
    private ToolStripStatusLabel _healthLabel = null!;

    public MainForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        // ── Form properties ──
        Text = "AOG Plot Manager v0.1.0";
        Size = new Size(1280, 850);
        MinimumSize = new Size(900, 650);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9);

        // ── Status Strip ──
        _statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("Ready — Generate a grid to get started")
        {
            Spring = true,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };
        _statusStrip.Items.Add(_statusLabel);

        // U1 FIX: Service health indicator
        _healthLabel = new ToolStripStatusLabel("✔ All services healthy")
        {
            ForeColor = Color.FromArgb(76, 175, 80),
            Font = new Font("Segoe UI", 8f)
        };
        _statusStrip.Items.Add(_healthLabel);

        // U1 FIX: View Log button
        var btnViewLog = new ToolStripButton("📝 Log")
        {
            Alignment = ToolStripItemAlignment.Right,
            Font = new Font("Segoe UI", 8f)
        };
        btnViewLog.Click += (_, _) =>
        {
            if (_plotLogger?.FilePath != null && File.Exists(_plotLogger.FilePath))
            {
                ShowLogViewer();
            }
            else
            {
                MessageBox.Show("Лог ещё не создан.", "Журнал", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        _statusStrip.Items.Add(btnViewLog);

        Controls.Add(_statusStrip);

        // ── Tab Control ──
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5f)
        };

        _tabControl.TabPages.Add(CreateGridSetupTab());
        _tabControl.TabPages.Add(CreateTrialMapTab());
        _tabControl.TabPages.Add(CreateHardwareRoutingTab());

        Controls.Add(_tabControl);

        // ── Pass Monitor button (top-right) ──
        var btnPassMonitor = new Button
        {
            Text = "▶  Pass Monitor",
            Size = new Size(160, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(156, 39, 176),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        btnPassMonitor.FlatAppearance.BorderSize = 0;
        btnPassMonitor.Location = new Point(ClientSize.Width - btnPassMonitor.Width - 12, 4);
        btnPassMonitor.Click += BtnPassMonitor_Click;
        Controls.Add(btnPassMonitor);
        btnPassMonitor.BringToFront();

        FormClosing += MainForm_FormClosing;

        ResumeLayout(true);
    }

    // ========================================================================
    // TAB 1: GRID SETUP & PREVIEW
    // ========================================================================

    private TabPage CreateGridSetupTab()
    {
        var tab = new TabPage("📐 Grid Setup")
        {
            Padding = new Padding(8)
        };

        // Left panel: input fields
        var inputPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = 280,
            Padding = new Padding(8),
            AutoScroll = true
        };

        // Right panel: preview
        var previewPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };

        var splitter = new Splitter
        {
            Dock = DockStyle.Left,
            Width = 4,
            BackColor = Color.FromArgb(220, 220, 220)
        };

        // ── Input fields ──
        int y = 8;

        AddSectionHeader(inputPanel, "Plot Dimensions", ref y);
        _nudPlotWidth = AddNumericField(inputPanel, "Plot Width (m):", 3.0m, 0.1m, 100m, 1, ref y);
        _nudPlotLength = AddNumericField(inputPanel, "Plot Length (m):", 10.0m, 0.1m, 500m, 1, ref y);

        y += 8;
        AddSectionHeader(inputPanel, "Alleys / Buffers", ref y);
        _nudBufferWidth = AddNumericField(inputPanel, "Side Alley (m):", 0.5m, 0.0m, 10m, 1, ref y);
        _nudBufferLength = AddNumericField(inputPanel, "End Alley (m):", 1.0m, 0.0m, 20m, 1, ref y);

        y += 8;
        AddSectionHeader(inputPanel, "Grid Size", ref y);
        _nudRows = AddNumericField(inputPanel, "Rows:", 4m, 1m, 100m, 0, ref y);
        _nudColumns = AddNumericField(inputPanel, "Columns:", 3m, 1m, 50m, 0, ref y);

        y += 8;
        AddSectionHeader(inputPanel, "Coordinates", ref y);
        _nudLatitude = AddNumericField(inputPanel, "Latitude:", 50.000000m, -90m, 90m, 6, ref y);
        _nudLongitude = AddNumericField(inputPanel, "Longitude:", 30.000000m, -180m, 180m, 6, ref y);
        _nudHeading = AddNumericField(inputPanel, "Heading (°):", 0m, 0m, 360m, 1, ref y);

        y += 16;

        // ── Generate button ──
        _btnGenerate = new Button
        {
            Text = "🔲  Generate Grid",
            Location = new Point(12, y),
            Size = new Size(240, 40),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(33, 150, 243),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _btnGenerate.FlatAppearance.BorderSize = 0;
        _btnGenerate.Click += BtnGenerate_Click;
        inputPanel.Controls.Add(_btnGenerate);

        y += 52;

        // ── Grid Info label ──
        _lblGridInfo = new Label
        {
            Location = new Point(12, y),
            Size = new Size(240, 60),
            ForeColor = Color.FromArgb(100, 100, 100),
            Font = new Font("Segoe UI", 8.5f),
            Text = string.Empty
        };
        inputPanel.Controls.Add(_lblGridInfo);

        // ── Preview ──
        _gridPreview = new PlotGridPreview
        {
            Dock = DockStyle.Fill,
        };
        previewPanel.Controls.Add(_gridPreview);

        // Compose layout (order matters for Dock)
        tab.Controls.Add(previewPanel);
        tab.Controls.Add(splitter);
        tab.Controls.Add(inputPanel);

        return tab;
    }

    private void BtnGenerate_Click(object? sender, EventArgs e)
    {
        try
        {
            var parameters = new GridGenerator.GridParams
            {
                Rows = (int)_nudRows.Value,
                Columns = (int)_nudColumns.Value,
                PlotWidthMeters = (double)_nudPlotWidth.Value,
                PlotLengthMeters = (double)_nudPlotLength.Value,
                BufferWidthMeters = (double)_nudBufferWidth.Value,
                BufferLengthMeters = (double)_nudBufferLength.Value,
                Origin = new GeoPoint((double)_nudLatitude.Value, (double)_nudLongitude.Value),
                HeadingDegrees = (double)_nudHeading.Value,
            };

            _currentGrid = _gridGenerator.Generate(parameters);
            _gridPreview.SetGrid(_currentGrid);

            // Update trial map colors if loaded
            if (_currentTrialMap != null)
            {
                _gridPreview.SetTrialMap(_currentTrialMap);
            }

            _lblGridInfo.Text = $"✅ Grid generated\n" +
                                $"   {_currentGrid.Rows} × {_currentGrid.Columns} = {_currentGrid.TotalPlots} plots\n" +
                                $"   Plot: {_currentGrid.PlotWidthMeters:F1} × {_currentGrid.PlotLengthMeters:F1} m";

            SetStatus($"Grid generated: {_currentGrid.TotalPlots} plots " +
                      $"({_currentGrid.Rows}R × {_currentGrid.Columns}C)");
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "Invalid Parameters", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ========================================================================
    // TAB 2: TRIAL MAP CSV IMPORT
    // ========================================================================

    private TabPage CreateTrialMapTab()
    {
        var tab = new TabPage("📋 Trial Map")
        {
            Padding = new Padding(12)
        };

        // ── Top toolbar ──
        var toolPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            Padding = new Padding(4)
        };

        _btnImportCsv = new Button
        {
            Text = "📂  Import CSV...",
            Location = new Point(4, 8),
            Size = new Size(160, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(76, 175, 80),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _btnImportCsv.FlatAppearance.BorderSize = 0;
        _btnImportCsv.Click += BtnImportCsv_Click;
        toolPanel.Controls.Add(_btnImportCsv);

        _lblCsvFilePath = new Label
        {
            Location = new Point(176, 16),
            Size = new Size(500, 20),
            ForeColor = Color.FromArgb(100, 100, 100),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
            Text = "No CSV file loaded"
        };
        toolPanel.Controls.Add(_lblCsvFilePath);

        _lblTrialInfo = new Label
        {
            Location = new Point(176, 36),
            Size = new Size(500, 18),
            ForeColor = Color.FromArgb(60, 60, 60),
            Font = new Font("Segoe UI", 8.5f),
            Text = string.Empty
        };
        toolPanel.Controls.Add(_lblTrialInfo);

        // ── DataGridView ──
        _dgvTrialMap = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(245, 245, 245),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(4)
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font("Segoe UI", 9),
                Padding = new Padding(4)
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(250, 250, 250)
            }
        };

        // Define columns
        _dgvTrialMap.Columns.Add("Row", "Row");
        _dgvTrialMap.Columns.Add("Column", "Column");
        _dgvTrialMap.Columns.Add("PlotId", "Plot ID");
        _dgvTrialMap.Columns.Add("Product", "Product");

        _dgvTrialMap.Columns["Row"]!.Width = 60;
        _dgvTrialMap.Columns["Column"]!.Width = 60;
        _dgvTrialMap.Columns["PlotId"]!.Width = 100;

        // Layout
        tab.Controls.Add(_dgvTrialMap);
        tab.Controls.Add(toolPanel);

        return tab;
    }

    private void BtnImportCsv_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Import Trial Map CSV",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            FilterIndex = 1,
        };

        if (ofd.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            _currentTrialMap = TrialMapParser.Parse(ofd.FileName);

            // Populate DataGridView
            _dgvTrialMap.Rows.Clear();
            foreach (var (plotId, product) in _currentTrialMap.PlotAssignments.OrderBy(kv => kv.Key))
            {
                // Parse row/column from PlotId (format: R{n}C{n})
                string id = plotId;
                int row = 0, col = 0;
                if (id.StartsWith("R", StringComparison.OrdinalIgnoreCase))
                {
                    int cIdx = id.IndexOf('C', StringComparison.OrdinalIgnoreCase);
                    if (cIdx > 0)
                    {
                        int.TryParse(id[1..cIdx], out row);
                        int.TryParse(id[(cIdx + 1)..], out col);
                    }
                }

                _dgvTrialMap.Rows.Add(row, col, plotId, product);
            }

            // Update info
            int plotCount = _currentTrialMap.PlotAssignments.Count;
            int productCount = _currentTrialMap.Products.Count;
            _lblCsvFilePath.Text = ofd.FileName;
            _lblTrialInfo.Text = $"✅ {plotCount} plots, {productCount} unique products: " +
                                 string.Join(", ", _currentTrialMap.Products.OrderBy(p => p));

            // Update preview colors
            _gridPreview.SetTrialMap(_currentTrialMap);

            // Populate hardware routing tab
            PopulateRoutingTab();

            SetStatus($"Trial map loaded: {plotCount} plots, {productCount} products");
        }
        catch (FileNotFoundException ex)
        {
            MessageBox.Show(ex.Message, "File Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (FormatException ex)
        {
            MessageBox.Show($"CSV format error:\n{ex.Message}", "Parse Error",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading CSV:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ========================================================================
    // TAB 3: HARDWARE ROUTING
    // ========================================================================

    private TabPage CreateHardwareRoutingTab()
    {
        var tab = new TabPage("🔧 Hardware Routing")
        {
            Padding = new Padding(12)
        };

        // ── Header ──
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
            Padding = new Padding(4)
        };

        var headerLabel = new Label
        {
            Text = "Assign each product to a physical boom section (1–14).\n" +
                   "Each section corresponds to one canister on the sprayer boom.",
            Location = new Point(4, 4),
            Size = new Size(600, 36),
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(80, 80, 80)
        };
        headerPanel.Controls.Add(headerLabel);

        _btnSaveRouting = new Button
        {
            Text = "💾  Save Routing",
            Location = new Point(4, 40),
            Size = new Size(140, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(255, 152, 0),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Enabled = false
        };
        _btnSaveRouting.FlatAppearance.BorderSize = 0;
        _btnSaveRouting.Click += BtnSaveRouting_Click;
        headerPanel.Controls.Add(_btnSaveRouting);

        _lblRoutingStatus = new Label
        {
            Location = new Point(154, 46),
            Size = new Size(600, 20),
            ForeColor = Color.FromArgb(100, 100, 100),
            Font = new Font("Segoe UI", 8.5f),
            Text = "Import a trial map CSV first"
        };
        headerPanel.Controls.Add(_lblRoutingStatus);

        // ── DataGridView ──
        _dgvRouting = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(245, 245, 245),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(4)
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font("Segoe UI", 9),
                Padding = new Padding(4)
            }
        };

        // Product column (read-only)
        var productColumn = new DataGridViewTextBoxColumn
        {
            Name = "Product",
            HeaderText = "Product Name",
            ReadOnly = true,
            FillWeight = 60
        };
        _dgvRouting.Columns.Add(productColumn);

        // Section column (editable ComboBox 1-14)
        var sectionColumn = new DataGridViewComboBoxColumn
        {
            Name = "Section",
            HeaderText = "Boom Section (1–14)",
            FillWeight = 40,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        };
        for (int i = 1; i <= HardwareRouting.TotalSections; i++)
        {
            sectionColumn.Items.Add(i);
        }
        _dgvRouting.Columns.Add(sectionColumn);

        // Layout
        tab.Controls.Add(_dgvRouting);
        tab.Controls.Add(headerPanel);

        return tab;
    }

    private void PopulateRoutingTab()
    {
        _dgvRouting.Rows.Clear();

        if (_currentTrialMap == null) return;

        foreach (string product in _currentTrialMap.Products.OrderBy(p => p))
        {
            int rowIdx = _dgvRouting.Rows.Add(product, null);
            _dgvRouting.Rows[rowIdx].Cells["Product"].ReadOnly = true;
        }

        _btnSaveRouting.Enabled = true;
        _lblRoutingStatus.Text = $"{_currentTrialMap.Products.Count} products need section assignments";
    }

    private void BtnSaveRouting_Click(object? sender, EventArgs e)
    {
        if (_currentTrialMap == null) return;

        var productToSections = new Dictionary<string, List<int>>();
        var sectionToProduct = new Dictionary<int, string>();
        var errors = new List<string>();

        foreach (DataGridViewRow row in _dgvRouting.Rows)
        {
            string? product = row.Cells["Product"].Value?.ToString();
            object? sectionVal = row.Cells["Section"].Value;

            if (string.IsNullOrEmpty(product))
                continue;

            if (sectionVal == null || sectionVal == DBNull.Value)
            {
                errors.Add($"Product '{product}' has no section assigned.");
                continue;
            }

            int section = Convert.ToInt32(sectionVal);
            int sectionIdx = section - 1; // Convert to 0-based

            // Check for duplicate section assignment
            if (sectionToProduct.ContainsKey(sectionIdx))
            {
                errors.Add($"Section {section} is assigned to both '{sectionToProduct[sectionIdx]}' and '{product}'.");
                continue;
            }

            productToSections[product] = new List<int> { sectionIdx };
            sectionToProduct[sectionIdx] = product;
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(
                "Routing errors:\n\n" + string.Join("\n", errors.Select(e => $"• {e}")),
                "Routing Validation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _currentRouting = new HardwareRouting
        {
            ProductToSections = productToSections,
            SectionToProduct = sectionToProduct,
        };

        // Validate against trial map
        List<string> validationErrors = _currentRouting.Validate(_currentTrialMap);
        if (validationErrors.Count > 0)
        {
            MessageBox.Show(
                "Missing assignments:\n\n" + string.Join("\n", validationErrors.Select(e => $"• {e}")),
                "Routing Incomplete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _gridPreview.SetRouting(_currentRouting);
        _lblRoutingStatus.Text = $"✅ Routing saved — {sectionToProduct.Count} sections assigned";
        SetStatus($"Hardware routing saved: {sectionToProduct.Count} product→section mappings");

        MessageBox.Show(
            $"Routing saved successfully!\n\n" +
            string.Join("\n", sectionToProduct.OrderBy(kv => kv.Key)
                .Select(kv => $"  Section {kv.Key + 1} → {kv.Value}")),
            "Routing Saved",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // ========================================================================
    // PASS MONITOR
    // ========================================================================

    private void BtnPassMonitor_Click(object? sender, EventArgs e)
    {
        // Reuse existing window if still open
        if (_passMonitorForm != null && !_passMonitorForm.IsDisposed)
        {
            _passMonitorForm.BringToFront();
            return;
        }

        // Lazy-init Core services
        // C1 FIX: Create shared PlotLogger and inject into all services
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
            _plotController = new PlotModeController(_spatialEngine, _sectionController, _aogClient, logger: _plotLogger);
            _plotController.WireInterlocks(_sensorHub);

            // P4-1 FIX: Wire per-boom evaluation when MachineProfile is loaded
            if (_machineProfile != null)
            {
                var hwSetup = _machineProfile.ToHardwareSetup();
                var delayProvider = _machineProfile.CreateBoomDelayProvider();
                _plotController.SetHardwareSetup(hwSetup, delayProvider);
                _machineProfile.ApplyToSpatialEngine(_spatialEngine);
                _machineProfile.ApplyToSectionController(_sectionController);
            }
        }

        // Configure SpatialEngine if grid + trial + routing are ready
        if (_currentGrid != null && _currentTrialMap != null && _currentRouting != null)
        {
            _spatialEngine.Configure(_currentGrid, _currentTrialMap, _currentRouting);
            _passTracker.Configure(_currentGrid);
        }

        // ── Phase 5: Init operational services ──
        _primeController ??= new PrimeController(logger: _plotLogger);
        _cleanController ??= new CleanController(logger: _plotLogger);
        _autoWeather ??= new AutoWeatherService();
        _trialLogger ??= new TrialLogger();

        // Wire transport for Prime/Clean (same serial transport as PlotController)
        if (_plotController.Transport != null)
        {
            _primeController.SetTransport(_plotController.Transport);
            _cleanController.SetTransport(_plotController.Transport);
        }

        _passMonitorForm = new FormPassMonitor(
            _plotController, _sensorHub, _sectionController,
            _passTracker, _aogClient, _currentGrid, _currentTrialMap,
            _primeController, _cleanController, _autoWeather, _trialLogger,
            _plotLogger);

        _passMonitorForm.Show();
        SetStatus("Pass Monitor opened");

        // UI4: Start health polling after services are initialized
        StartHealthPolling();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        // C4 FIX: Close PassMonitor first so it unsubscribes from events
        // before we dispose the services it references
        if (_passMonitorForm != null && !_passMonitorForm.IsDisposed)
        {
            _passMonitorForm.Close();
        }
        _passMonitorForm = null;

        // F6 FIX: Close log viewer if open
        if (_logViewerForm != null && !_logViewerForm.IsDisposed)
        {
            _logViewerForm.Close();
        }
        _logViewerForm = null;

        // Now safe to dispose Core services
        _plotController?.Dispose();
        _sensorHub?.Dispose();
        _aogClient?.Dispose();
        _cleanController?.Dispose();
        _trialLogger?.Dispose();
        _autoWeather?.Dispose();

        // UI4: Stop health polling
        _healthPollTimer?.Stop();
        _healthPollTimer?.Dispose();

        // C1 FIX: Stop logger session
        _plotLogger?.StopSession();
    }

    // ════════════════════════════════════════════════════════════════════
    // UI4: Health polling + Estop wiring
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Starts a 1-second health polling timer that updates the StatusStrip
    /// with live service health + Estop status.
    /// Called once after services are initialized.
    /// </summary>
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

        // Estop reaction: if Teensy reports E-STOP, activate in SectionController
        if (estop && _sectionController != null && !_sectionController.EmergencyStopActive)
        {
            _sectionController.ActivateEmergencyStop();
            _plotLogger?.Error("UI", "Teensy E-STOP detected via telemetry — activating SectionController E-STOP");
        }

        // Determine worst health
        ServiceHealth worst = ServiceHealth.Healthy;
        if (sensorHealth == ServiceHealth.Failed || aogHealth == ServiceHealth.Failed)
            worst = ServiceHealth.Failed;
        else if (sensorHealth == ServiceHealth.Degraded || aogHealth == ServiceHealth.Degraded)
            worst = ServiceHealth.Degraded;

        // Update StatusStrip label
        if (estop)
        {
            _healthLabel.Text = "🛑 E-STOP ACTIVE";
            _healthLabel.ForeColor = Color.FromArgb(244, 67, 54);
        }
        else if (worst == ServiceHealth.Failed)
        {
            _healthLabel.Text = $"❌ Service failed (SEN:{sensorHealth} UDP:{aogHealth})";
            _healthLabel.ForeColor = Color.FromArgb(244, 67, 54);
        }
        else if (worst == ServiceHealth.Degraded || telemetryStale)
        {
            string staleText = telemetryStale ? " Telemetry stale" : "";
            _healthLabel.Text = $"⚠ Degraded (SEN:{sensorHealth} UDP:{aogHealth}){staleText}";
            _healthLabel.ForeColor = Color.FromArgb(255, 193, 7);
        }
        else
        {
            _healthLabel.Text = $"✔ Healthy (SEN:OK UDP:OK) Log:{_plotLogger?.EntryCount ?? 0}";
            _healthLabel.ForeColor = Color.FromArgb(76, 175, 80);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // UI4: In-app Log Viewer
    // ════════════════════════════════════════════════════════════════════

    private Form? _logViewerForm;

    private void ShowLogViewer()
    {
        if (_logViewerForm != null && !_logViewerForm.IsDisposed)
        {
            _logViewerForm.BringToFront();
            return;
        }

        string? logPath = _plotLogger?.FilePath;
        if (string.IsNullOrEmpty(logPath))
        {
            MessageBox.Show("Лог ещё не создан.", "Журнал", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _logViewerForm = new Form
        {
            Text = $"📝 Diagnostic Log — {Path.GetFileName(logPath)}",
            Size = new Size(900, 600),
            StartPosition = FormStartPosition.CenterParent,
            Font = new Font("Consolas", 9.5f),
        };

        var txtLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(25, 25, 30),
            ForeColor = Color.FromArgb(200, 200, 200),
            Font = new Font("Consolas", 9.5f),
            WordWrap = false,
            BorderStyle = BorderStyle.None,
        };

        // Load current content
        if (File.Exists(logPath))
            txtLog.Text = File.ReadAllText(logPath);

        // Auto-refresh button
        var btnRefresh = new Button
        {
            Text = "🔄 Refresh",
            Dock = DockStyle.Bottom,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(33, 150, 243),
            ForeColor = Color.White,
        };
        string capturedLogPath = logPath; // capture for lambda
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

    // ========================================================================
    // HELPERS
    // ========================================================================

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
    }

    private static void AddSectionHeader(Panel parent, string text, ref int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(8, y),
            Size = new Size(260, 18),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(33, 150, 243)
        };
        parent.Controls.Add(label);
        y += 22;
    }

    private static NumericUpDown AddNumericField(
        Panel parent, string labelText,
        decimal defaultValue, decimal min, decimal max,
        int decimalPlaces, ref int y)
    {
        var label = new Label
        {
            Text = labelText,
            Location = new Point(12, y + 3),
            Size = new Size(130, 20),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(60, 60, 60)
        };
        parent.Controls.Add(label);

        var nud = new NumericUpDown
        {
            Location = new Point(148, y),
            Size = new Size(110, 26),
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimalPlaces,
            Value = defaultValue,
            Increment = decimalPlaces > 0 ? 0.1m : 1m,
            Font = new Font("Segoe UI", 9),
            BorderStyle = BorderStyle.FixedSingle,
        };
        parent.Controls.Add(nud);

        y += 30;
        return nud;
    }
}
