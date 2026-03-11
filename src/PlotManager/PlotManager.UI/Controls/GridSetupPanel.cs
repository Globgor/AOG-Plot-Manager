// Workflow: UI Modernization | Task: GridSetupPanel
using System.Drawing;
using System.Windows.Forms;
using PlotManager.Core.Models;
using PlotManager.Core.Services;

namespace PlotManager.UI.Controls;

/// <summary>
/// Wizard Step 2 — Grid dimensions, coordinates, and preview.
/// Extracted from MainForm Tab 1 with dark theme and improved spacing.
/// </summary>
public sealed class GridSetupPanel : UserControl
{
    private readonly GridGenerator _gridGenerator = new();

    // Input fields
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
    private Label _lblGridInfo = null!;
    private PlotGridPreview _gridPreview = null!;

    /// <summary>The generated grid, if any.</summary>
    public PlotGrid? CurrentGrid { get; private set; }

    /// <summary>Whether a grid has been generated.</summary>
    public bool IsValid => CurrentGrid != null;

    /// <summary>Fires when the grid changes.</summary>
    public event EventHandler? GridChanged;

    public GridSetupPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.BgPrimary;
        BuildLayout();
    }

    private void BuildLayout()
    {
        // ── Left side: input form ──
        var inputPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = 320,
            AutoScroll = true,
            BackColor = AppTheme.BgPrimary,
            Padding = new Padding(20, 16, 12, 16),
        };

        var inputLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 48),
                new ColumnStyle(SizeType.Percent, 52),
            },
        };

        int row = 0;

        // Header
        var header = new Label
        {
            Text = "Крок 2: Сітка делянок",
            Font = AppTheme.FontHeading,
            ForeColor = AppTheme.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };
        inputLayout.SetColumnSpan(header, 2);
        inputLayout.Controls.Add(header, 0, row++);

        // Plot Dimensions
        AddSectionLabel(inputLayout, "Розміри делянки", ref row);
        _nudPlotWidth = AddNumericRow(inputLayout, "Ширина (м):", 3.0m, 0.1m, 100m, 1, ref row);
        _nudPlotLength = AddNumericRow(inputLayout, "Довжина (м):", 10.0m, 0.1m, 500m, 1, ref row);

        // Buffers
        AddSectionLabel(inputLayout, "Алеї / Буфери", ref row);
        _nudBufferWidth = AddNumericRow(inputLayout, "Бокова (м):", 0.5m, 0m, 10m, 1, ref row);
        _nudBufferLength = AddNumericRow(inputLayout, "Торцева (м):", 1.0m, 0m, 20m, 1, ref row);

        // Grid size
        AddSectionLabel(inputLayout, "Розмір сітки", ref row);
        _nudRows = AddNumericRow(inputLayout, "Рядки:", 4m, 1m, 100m, 0, ref row);
        _nudColumns = AddNumericRow(inputLayout, "Колонки:", 3m, 1m, 50m, 0, ref row);

        // Coordinates
        AddSectionLabel(inputLayout, "Координати", ref row);
        _nudLatitude = AddNumericRow(inputLayout, "Широта:", 50.000000m, -90m, 90m, 6, ref row);
        _nudLongitude = AddNumericRow(inputLayout, "Довгота:", 30.000000m, -180m, 180m, 6, ref row);
        _nudHeading = AddNumericRow(inputLayout, "Курс (°):", 0m, 0m, 360m, 1, ref row);

        // Generate button
        _btnGenerate = new Button
        {
            Text = "🔲  Генерувати сітку",
            Dock = DockStyle.Top,
            Height = 44,
            Margin = new Padding(0, 20, 0, 8),
        };
        AppTheme.StyleButton(_btnGenerate, AppTheme.AccentBlue);
        _btnGenerate.Click += OnGenerate;
        inputLayout.SetColumnSpan(_btnGenerate, 2);
        inputLayout.Controls.Add(_btnGenerate, 0, row++);

        // Info label
        _lblGridInfo = new Label
        {
            Text = "",
            Font = AppTheme.FontSmall,
            ForeColor = AppTheme.TextSecondary,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0),
        };
        inputLayout.SetColumnSpan(_lblGridInfo, 2);
        inputLayout.Controls.Add(_lblGridInfo, 0, row++);

        inputPanel.Controls.Add(inputLayout);

        // ── Right side: preview ──
        var previewPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.BgSecondary,
            Padding = new Padding(8),
        };

        _gridPreview = new PlotGridPreview
        {
            Dock = DockStyle.Fill,
        };
        previewPanel.Controls.Add(_gridPreview);

        // ── Splitter ──
        var splitter = new Splitter
        {
            Dock = DockStyle.Left,
            Width = 3,
            BackColor = AppTheme.Border,
        };

        // Layout order matters for Dock
        Controls.Add(previewPanel);
        Controls.Add(splitter);
        Controls.Add(inputPanel);
    }

    private void OnGenerate(object? sender, EventArgs e)
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
                Origin = new GeoPoint(
                    (double)_nudLatitude.Value,
                    (double)_nudLongitude.Value),
                HeadingDegrees = (double)_nudHeading.Value,
            };

            CurrentGrid = _gridGenerator.Generate(parameters);
            _gridPreview.SetGrid(CurrentGrid);

            _lblGridInfo.Text =
                $"✅ Згенеровано: {CurrentGrid.Rows} × {CurrentGrid.Columns} " +
                $"= {CurrentGrid.TotalPlots} делянок\n" +
                $"    Розмір: {CurrentGrid.PlotWidthMeters:F1} × " +
                $"{CurrentGrid.PlotLengthMeters:F1} м";
            _lblGridInfo.ForeColor = AppTheme.AccentGreen;

            GridChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "Невірні параметри",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Apply trial map colors to the grid preview.</summary>
    public void ApplyTrialMap(TrialMap? map)
    {
        if (map != null) _gridPreview.SetTrialMap(map);
    }

    /// <summary>Apply routing to the grid preview.</summary>
    public void ApplyRouting(HardwareRouting? routing)
    {
        if (routing != null) _gridPreview.SetRouting(routing);
    }

    // ════════════════════════════════════════════════════════════════════
    // Layout helpers — dark-themed input rows
    // ════════════════════════════════════════════════════════════════════

    private static void AddSectionLabel(TableLayoutPanel p, string text, ref int row)
    {
        var lbl = AppTheme.CreateSectionHeader(text);
        p.SetColumnSpan(lbl, 2);
        p.Controls.Add(lbl, 0, row++);
    }

    private static NumericUpDown AddNumericRow(
        TableLayoutPanel p, string label,
        decimal defaultVal, decimal min, decimal max,
        int decimals, ref int row)
    {
        var lbl = new Label
        {
            Text = label,
            Font = AppTheme.FontBody,
            ForeColor = AppTheme.TextSecondary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 2, 8, 4),
        };
        p.Controls.Add(lbl, 0, row);

        var nud = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimals,
            Value = defaultVal,
            Increment = decimals > 0 ? 0.1m : 1m,
            Margin = new Padding(0, 2, 0, 4),
        };
        AppTheme.StyleNumeric(nud);
        p.Controls.Add(nud, 1, row++);

        return nud;
    }
}
