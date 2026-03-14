using System;
using System.Drawing;
using System.Windows.Forms;
using PlotManager.Core.Models;
using PlotManager.Core.Services;

namespace PlotManager.UI.Controls;

/// <summary>
/// Wizard Step 3 — Physical Field Placement.
/// Takes the logical Trial Design (from Step 2) and places it on the Earth
/// using GPS coordinates and heading.
/// </summary>
public sealed class FieldPlacementPanel : UserControl
{
    private readonly GridGenerator _gridGenerator = new();

    // Input fields - Coordinates
    private NumericUpDown _nudLatitude = null!;
    private NumericUpDown _nudLongitude = null!;
    private NumericUpDown _nudHeading = null!;

    private Button _btnApply = null!;
    private Label _lblStatus = null!;

    // Logical design state (from step 2)
    private PlotGrid? _logicalGrid;
    private TrialMap? _logicalTrialMap;

    /// <summary>The final physically placed trial grid.</summary>
    public PlotGrid? PlacedGrid { get; private set; }

    /// <summary>The final physically placed trial map.</summary>
    public TrialMap? PlacedTrialMap { get; private set; }

    /// <summary>Whether coordinates have been successfully applied.</summary>
    public bool IsValid => PlacedTrialMap != null && PlacedGrid != null;

    /// <summary>Fires when physical placement changes.</summary>
    public event EventHandler? PlacementChanged;

    /// <summary>
    /// Restores a pre-built grid (e.g. loaded from a saved session),
    /// bypassing the GPS coordinate entry UI.
    /// Triggers PlacementChanged so MainForm wires the routing step.
    /// </summary>
    public void SetRestoredGrid(PlotGrid grid)
    {
        _logicalGrid = grid;
        PlacedGrid = grid;
        // If there is an existing logicalTrialMap, use it; otherwise create a minimal placeholder.
        PlacedTrialMap = _logicalTrialMap ?? new PlotManager.Core.Models.TrialMap
        {
            TrialName = "Restored",
        };
        PlacementChanged?.Invoke(this, EventArgs.Empty);
    }

    public FieldPlacementPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.BgPrimary;
        BuildLayout();
    }

    private void BuildLayout()
    {
        var inputPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(40, 24, 40, 24),
        };

        var inputLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 40),
                new ColumnStyle(SizeType.Percent, 60),
            },
            MaximumSize = new Size(500, 0),
        };

        int row = 0;

        // Header
        var header = new Label
        {
            Text = "Крок 3: Розміщення на Полі (Field Placement)",
            Font = AppTheme.FontHeading,
            ForeColor = AppTheme.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };
        inputLayout.SetColumnSpan(header, 2);
        inputLayout.Controls.Add(header, 0, row++);

        var infoLabel = new Label
        {
            Text = "Задайте географічні координати для старту обприскування.\n" +
                   "Схема досліду буде зорієнтована відносно цих координат.",
            Font = AppTheme.FontBody,
            ForeColor = AppTheme.TextSecondary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 24),
        };
        inputLayout.SetColumnSpan(infoLabel, 2);
        inputLayout.Controls.Add(infoLabel, 0, row++);

        // Coordinates (sub-meter precision: 8 decimals)
        AddSectionLabel(inputLayout, "GPS Координати", ref row);
        _nudLatitude = AddNumericRow(inputLayout, "Початкова широта (Lat):", 50.00000000m, -90m, 90m, 8, ref row);
        _nudLongitude = AddNumericRow(inputLayout, "Початкова довгота (Lon):", 30.00000000m, -180m, 180m, 8, ref row);
        _nudHeading = AddNumericRow(inputLayout, "Курс руху (° Heading):", 0m, 0m, 360m, 1, ref row);

        // Map placeholder
        var mapPlaceholder = new Label
        {
            Text = "🗺️ Інтерактивна супутникова карта (Leaflet / WebView2)\nбуде додана у наступних версіях.",
            Font = new Font("Segoe UI", 10, FontStyle.Italic),
            ForeColor = AppTheme.TextDim,
            BackColor = AppTheme.BgCard,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Height = 120,
            Margin = new Padding(0, 20, 0, 20),
            BorderStyle = BorderStyle.FixedSingle,
        };
        inputLayout.SetColumnSpan(mapPlaceholder, 2);
        inputLayout.Controls.Add(mapPlaceholder, 0, row++);

        // Generate button
        _btnApply = new Button
        {
            Text = "📍 Прив'язати до Поля",
            Dock = DockStyle.Top,
            Height = 44,
            Margin = new Padding(0, 0, 0, 8),
        };
        AppTheme.StyleButton(_btnApply, AppTheme.AccentBlue);
        _btnApply.Click += OnApplyCoordinates;
        inputLayout.SetColumnSpan(_btnApply, 2);
        inputLayout.Controls.Add(_btnApply, 0, row++);

        // Status label
        _lblStatus = new Label
        {
            Text = "Очікування координат...",
            Font = AppTheme.FontSmall,
            ForeColor = AppTheme.TextSecondary,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0),
        };
        inputLayout.SetColumnSpan(_lblStatus, 2);
        inputLayout.Controls.Add(_lblStatus, 0, row++);

        inputPanel.Controls.Add(inputLayout);
        Controls.Add(inputPanel);
    }

    /// <summary>
    /// Loads the logical trial map generated in Step 2.
    /// </summary>
    public void SetLogicalTrialMap(PlotGrid? logicalGrid, TrialMap? logicalTrialMap)
    {
        _logicalTrialMap = logicalTrialMap;
        _logicalGrid = logicalGrid;
        
        // Reset state
        PlacedTrialMap = null;
        _lblStatus.Text = "Оновіть фізичні координати для завантаженого дизайну.";
        _lblStatus.ForeColor = AppTheme.TextSecondary;
        AppTheme.StyleButton(_btnApply, AppTheme.AccentBlue);
    }

    private void OnApplyCoordinates(object? sender, EventArgs e)
    {
        if (_logicalGrid == null || _logicalTrialMap == null)
        {
            MessageBox.Show("Спочатку створіть Дизайн Досліду на кроці 2.", "Помилка", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            // 1. Re-run GridGenerator with the same logical dimensions but new physical origin
            var parameters = new GridGenerator.GridParams
            {
                Rows = _logicalGrid.Rows,
                Columns = _logicalGrid.Columns,
                PlotWidthMeters = _logicalGrid.PlotWidthMeters,
                PlotLengthMeters = _logicalGrid.PlotLengthMeters,
                BufferWidthMeters = _logicalGrid.BufferWidthMeters,
                BufferLengthMeters = _logicalGrid.BufferLengthMeters,
                Origin = new GeoPoint(
                    (double)_nudLatitude.Value,
                    (double)_nudLongitude.Value),
                HeadingDegrees = (double)_nudHeading.Value,
            };

            var physicalGrid = _gridGenerator.Generate(parameters);

            // 2. Clone the logical TrialMap
            // We use standard dictionary to copy the plot assignments
            var newAssignments = new Dictionary<string, string>(_logicalTrialMap.PlotAssignments);
            
            PlacedGrid = physicalGrid;
            PlacedTrialMap = new TrialMap 
            { 
                TrialName = _logicalTrialMap.TrialName,
                PlotAssignments = newAssignments
            };

            _lblStatus.Text =
                $"✅ Схема успішно прив'язана до ({parameters.Origin.Latitude:F6}, {parameters.Origin.Longitude:F6})";
            _lblStatus.ForeColor = AppTheme.AccentGreen;
            AppTheme.StyleButton(_btnApply, AppTheme.AccentGreen);

            PlacementChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Помилка прив'язки",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }


    // ════════════════════════════════════════════════════════════════════
    // Layout helpers
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
            BackColor = AppTheme.BgInput,
            ForeColor = AppTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 0, 4),
        };
        p.Controls.Add(nud, 1, row++);
        return nud;
    }
}
