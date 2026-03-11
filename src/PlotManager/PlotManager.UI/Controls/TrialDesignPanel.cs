using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using PlotManager.Core.Models;
using PlotManager.Core.Services;

namespace PlotManager.UI.Controls;

public class TrialDesignSaveState
{
    public decimal PlotWidth { get; set; }
    public decimal PlotLength { get; set; }
    public decimal BufferWidth { get; set; }
    public decimal BufferLength { get; set; }
    public decimal Rows { get; set; }
    public decimal Columns { get; set; }
    public int DesignTypeIndex { get; set; }
    public string TreatmentsText { get; set; } = string.Empty;
}

/// <summary>
/// Wizard Step 2 — Logical Trial Design.
/// Combines Grid Dimensions and Plot Randomization into a single logical step.
/// Generates a TrialMap with assignments without concerning physical map coordinates.
/// </summary>
public sealed class TrialDesignPanel : UserControl
{
    private readonly GridGenerator _gridGenerator = new();
    private readonly ExperimentDesigner _experimentDesigner = new();

    // Input fields - Grid
    private NumericUpDown _nudPlotWidth = null!;
    private NumericUpDown _nudPlotLength = null!;
    private NumericUpDown _nudBufferWidth = null!;
    private NumericUpDown _nudBufferLength = null!;
    private NumericUpDown _nudRows = null!;
    private NumericUpDown _nudColumns = null!;

    // Input fields - Randomization
    private ComboBox _cmbDesignType = null!;
    private TextBox _txtTreatments = null!;

    private Button _btnGenerate = null!;
    private Label _lblGridInfo = null!;
    private PlotGridPreview _gridPreview = null!;

    /// <summary>The generated logical grid.</summary>
    public PlotGrid? CurrentGrid { get; private set; }

    /// <summary>The generated trial map with product assignments.</summary>
    public TrialMap? CurrentTrialMap { get; private set; }

    /// <summary>Whether a valid trial map has been generated.</summary>
    public bool IsValid => CurrentGrid != null && CurrentTrialMap != null;

    /// <summary>Fires when the logical design changes.</summary>
    public event EventHandler? DesignChanged;

    public TrialDesignPanel()
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
            Width = 340,
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
                new ColumnStyle(SizeType.Percent, 50),
                new ColumnStyle(SizeType.Percent, 50),
            },
        };

        int row = 0;

        // Header
        var header = new Label
        {
            Text = "Крок 2: Дизайн Досліду",
            Font = AppTheme.FontHeading,
            ForeColor = AppTheme.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };
        inputLayout.SetColumnSpan(header, 2);
        inputLayout.Controls.Add(header, 0, row++);

        var infoLabel = new Label
        {
            Text = "Задайте логічні розміри та препарати",
            Font = AppTheme.FontBody,
            ForeColor = AppTheme.TextSecondary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16),
        };
        inputLayout.SetColumnSpan(infoLabel, 2);
        inputLayout.Controls.Add(infoLabel, 0, row++);

        // Plot Dimensions
        AddSectionLabel(inputLayout, "Розміри делянки", ref row);
        _nudPlotWidth = AddNumericRow(inputLayout, "Ширина (м):", 2.80m, 0.01m, 100m, 2, ref row);
        _nudPlotLength = AddNumericRow(inputLayout, "Довжина (м):", 10.24m, 0.01m, 500m, 2, ref row);

        // Buffers
        AddSectionLabel(inputLayout, "Алеї / Буфери", ref row);
        _nudBufferWidth = AddNumericRow(inputLayout, "Бокова (м):", 0.00m, 0.00m, 10m, 2, ref row);
        _nudBufferLength = AddNumericRow(inputLayout, "Торцева (м):", 4.00m, 0.00m, 20m, 2, ref row);

        // Grid size
        AddSectionLabel(inputLayout, "Решітка поля", ref row);
        _nudRows = AddNumericRow(inputLayout, "Рядки (Проходи):", 10m, 1m, 100m, 0, ref row);
        _nudColumns = AddNumericRow(inputLayout, "Колонки:", 20m, 1m, 50m, 0, ref row);

        // Randomization Design
        AddSectionLabel(inputLayout, "Схема рандомізації", ref row);
        
        _cmbDesignType = AddComboRow(inputLayout, "Тип дизайну:",
            new[] { "RCBD (Блоки)", "CRD (Повний рандом)", "Latin Square" },
            ref row);

        var lblTreatments = new Label
        {
            Text = "Препарати (1 на рядок):",
            Font = AppTheme.FontBody,
            ForeColor = AppTheme.TextSecondary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 8, 0, 4),
        };
        inputLayout.SetColumnSpan(lblTreatments, 2);
        inputLayout.Controls.Add(lblTreatments, 0, row++);

        _txtTreatments = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            Height = 100,
            BackColor = AppTheme.BgInput,
            ForeColor = AppTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Text = "Гербіцид А\nДобриво Б\nКонтроль\nГербіцид С",
            Margin = new Padding(0, 0, 8, 8),
        };
        inputLayout.SetColumnSpan(_txtTreatments, 2);
        inputLayout.Controls.Add(_txtTreatments, 0, row++);

        // Actions panel
        var actionsLayout = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 3,
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 20, 8, 8),
        };
        actionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _btnGenerate = new Button { Text = "🔲 Згенерувати Схему", Dock = DockStyle.Fill, Height = 44, Margin = new Padding(0, 0, 0, 8) };
        AppTheme.StyleButton(_btnGenerate, AppTheme.AccentBlue);
        _btnGenerate.Click += OnGenerate;
        actionsLayout.SetColumnSpan(_btnGenerate, 2);
        actionsLayout.Controls.Add(_btnGenerate, 0, 0);

        var btnExportExcel = new Button { Text = "📊 Експорт (Excel)", Dock = DockStyle.Fill, Height = 36, Margin = new Padding(0, 0, 4, 4) };
        AppTheme.StyleButton(btnExportExcel, AppTheme.AccentGreen);
        btnExportExcel.Click += OnExportExcel;
        
        var btnSaveJson = new Button { Text = "💾 Зберегти", Dock = DockStyle.Fill, Height = 36, Margin = new Padding(4, 0, 0, 4) };
        AppTheme.StyleButton(btnSaveJson, AppTheme.BgSecondary);
        btnSaveJson.Click += OnSaveJson;

        var btnLoadJson = new Button { Text = "📂 Завантажити", Dock = DockStyle.Fill, Height = 36, Margin = new Padding(0, 4, 4, 0) };
        AppTheme.StyleButton(btnLoadJson, AppTheme.BgSecondary);
        btnLoadJson.Click += OnLoadJson;

        var btnClear = new Button { Text = "📄 Новий Дизайн", Dock = DockStyle.Fill, Height = 36, Margin = new Padding(4, 4, 0, 0) };
        AppTheme.StyleButton(btnClear, AppTheme.BgSecondary);
        btnClear.Click += OnClear;

        actionsLayout.Controls.Add(btnExportExcel, 0, 1);
        actionsLayout.Controls.Add(btnSaveJson, 1, 1);
        actionsLayout.Controls.Add(btnLoadJson, 0, 2);
        actionsLayout.Controls.Add(btnClear, 1, 2);

        inputLayout.SetColumnSpan(actionsLayout, 2);
        inputLayout.Controls.Add(actionsLayout, 0, row++);

        // Info label
        _lblGridInfo = new Label
        {
            Text = "",
            Font = AppTheme.FontSmall,
            ForeColor = AppTheme.TextSecondary,
            AutoSize = true,
            MaximumSize = new Size(300, 0),
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
            // 1. Parse treatments
            var treatments = _txtTreatments.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (treatments.Count < 2)
            {
                MessageBox.Show("Потрібно задати мінімум 2 препарати.", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 2. Generate logical grid (using dummy coordinates for Origin/Heading)
            var parameters = new GridGenerator.GridParams
            {
                Rows = (int)_nudRows.Value,
                Columns = (int)_nudColumns.Value,
                PlotWidthMeters = (double)_nudPlotWidth.Value,
                PlotLengthMeters = (double)_nudPlotLength.Value,
                BufferWidthMeters = (double)_nudBufferWidth.Value,
                BufferLengthMeters = (double)_nudBufferLength.Value,
                Origin = new GeoPoint(0, 0), // Dummy origin
                HeadingDegrees = 0.0,        // Dummy heading
            };

            CurrentGrid = _gridGenerator.Generate(parameters);

            // 3. Randomize treatments
            ExperimentalDesignType designType = _cmbDesignType.SelectedIndex switch
            {
                0 => ExperimentalDesignType.RCBD,
                1 => ExperimentalDesignType.CRD,
                2 => ExperimentalDesignType.LatinSquare,
                _ => ExperimentalDesignType.RCBD
            };

            CurrentTrialMap = _experimentDesigner.GenerateDesign(CurrentGrid, treatments, designType);

            // 4. Update preview
            _gridPreview.SetGrid(CurrentGrid);
            _gridPreview.SetTrialMap(CurrentTrialMap);

            _lblGridInfo.Text =
                $"✅ Схема готова: {CurrentGrid.Rows} × {CurrentGrid.Columns} = {CurrentGrid.TotalPlots} делянок\n" +
                $"Дизайн: {designType}\n" +
                $"Препаратів: {treatments.Count}";
            _lblGridInfo.ForeColor = AppTheme.AccentGreen;

            DesignChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Помилка генерації",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    
    // ════════════════════════════════════════════════════════════════════
    // Actions
    // ════════════════════════════════════════════════════════════════════

    private void OnExportExcel(object? sender, EventArgs e)
    {
        if (!IsValid)
        {
            MessageBox.Show("Спочатку згенеруйте схему!", "Увага", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files|*.xlsx",
            Title = "Експорт Схеми в Excel",
            FileName = "TrialMap.xlsx"
        };

        if (sfd.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var exporter = new TrialExcelExporter();
                exporter.ExportToExcel(CurrentTrialMap!, CurrentGrid, sfd.FileName);
                MessageBox.Show("Схему успішно експортовано!", "Успіх", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка експорту: {ex.Message}", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void OnSaveJson(object? sender, EventArgs e)
    {
        using var sfd = new SaveFileDialog
        {
            Filter = "JSON Files|*.json",
            Title = "Зберегти Дизайн Досліду",
            FileName = "TrialDesign.json"
        };

        if (sfd.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var state = new TrialDesignSaveState
                {
                    PlotWidth = _nudPlotWidth.Value,
                    PlotLength = _nudPlotLength.Value,
                    BufferWidth = _nudBufferWidth.Value,
                    BufferLength = _nudBufferLength.Value,
                    Rows = _nudRows.Value,
                    Columns = _nudColumns.Value,
                    DesignTypeIndex = _cmbDesignType.SelectedIndex,
                    TreatmentsText = _txtTreatments.Text
                };

                string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(sfd.FileName, json);
                MessageBox.Show("Дизайн успішно збережено!", "Успіх", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка збереження: {ex.Message}", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void OnLoadJson(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "JSON Files|*.json",
            Title = "Завантажити Дизайн Досліду"
        };

        if (ofd.ShowDialog() == DialogResult.OK)
        {
            try
            {
                string json = File.ReadAllText(ofd.FileName);
                var state = JsonSerializer.Deserialize<TrialDesignSaveState>(json);
                if (state != null)
                {
                    // Clamp all values to prevent ArgumentOutOfRangeException
                    // if the saved file was created with different NUD bounds.
                    static decimal Clamp(decimal v, NumericUpDown n) =>
                        Math.Clamp(v, n.Minimum, n.Maximum);

                    _nudPlotWidth.Value   = Clamp(state.PlotWidth,   _nudPlotWidth);
                    _nudPlotLength.Value  = Clamp(state.PlotLength,  _nudPlotLength);
                    _nudBufferWidth.Value = Clamp(state.BufferWidth, _nudBufferWidth);
                    _nudBufferLength.Value= Clamp(state.BufferLength,_nudBufferLength);
                    _nudRows.Value        = Clamp(state.Rows,        _nudRows);
                    _nudColumns.Value     = Clamp(state.Columns,     _nudColumns);

                    if (state.DesignTypeIndex >= 0 && state.DesignTypeIndex < _cmbDesignType.Items.Count)
                        _cmbDesignType.SelectedIndex = state.DesignTypeIndex;

                    _txtTreatments.Text = state.TreatmentsText;

                    MessageBox.Show("Дизайн успішно завантажено. Натисніть 'Згенерувати Схему', щоб відтворити сітку.", "Успіх", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка завантаження: {ex.Message}", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void OnClear(object? sender, EventArgs e)
    {
        if (MessageBox.Show("Очистити поточний дизайн та сітку?", "Підтвердження", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            CurrentGrid = null;
            CurrentTrialMap = null;
            _gridPreview.SetGrid(null);
            _gridPreview.SetTrialMap(null);
            _lblGridInfo.Text = "";
            DesignChanged?.Invoke(this, EventArgs.Empty);
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
            Margin = new Padding(0, 0, 8, 4),
        };
        p.Controls.Add(nud, 1, row++);
        return nud;
    }

    private static ComboBox AddComboRow(
        TableLayoutPanel p, string label, string[] items, ref int row)
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

        var cmb = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = AppTheme.BgInput,
            ForeColor = AppTheme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 8, 4),
        };
        cmb.Items.AddRange(items);
        cmb.SelectedIndex = 0;
        p.Controls.Add(cmb, 1, row++);
        return cmb;
    }
}
