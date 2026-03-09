using PlotManager.Core.Models;
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

    // ── State ──
    private PlotGrid? _currentGrid;
    private TrialMap? _currentTrialMap;
    private HardwareRouting? _currentRouting;

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
