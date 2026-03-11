// Workflow: UI Modernization | Task: TrialMapPanel
using System.Drawing;
using System.Windows.Forms;
using PlotManager.Core.Models;
using PlotManager.Core.Services;

namespace PlotManager.UI.Controls;

/// <summary>
/// Wizard Step 3 — CSV import for trial map with dark-themed DataGridView.
/// Provides clear instructions and an option to auto-generate an example map.
/// </summary>
public sealed class TrialMapPanel : UserControl
{
    private DataGridView _dgv = null!;
    private Label _lblStatus = null!;
    private Label _lblFilePath = null!;
    private Panel _instructionPanel = null!;

    /// <summary>The loaded trial map, if any.</summary>
    public TrialMap? CurrentTrialMap { get; private set; }

    /// <summary>Whether a valid trial map is loaded.</summary>
    public bool IsValid => CurrentTrialMap != null;

    /// <summary>Fires when trial map changes.</summary>
    public event EventHandler? TrialMapChanged;

    public TrialMapPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.BgPrimary;
        BuildLayout();
    }

    private void BuildLayout()
    {
        // ── Top: header + instructions + buttons ──
        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 180,
            BackColor = AppTheme.BgPrimary,
            Padding = new Padding(20, 16, 20, 8),
        };

        var header = new Label
        {
            Text = "Крок 3: Карта досліду (Trial Map)",
            Font = AppTheme.FontHeading,
            ForeColor = AppTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(20, 16),
        };
        topPanel.Controls.Add(header);

        var subtitle = new Label
        {
            Text = "Призначте продукти (гербіциди, добрива) для кожної делянки.\n" +
                   "Можна імпортувати CSV-файл або згенерувати приклад автоматично.",
            Font = new Font("Segoe UI", 10),
            ForeColor = AppTheme.TextSecondary,
            AutoSize = true,
            MaximumSize = new Size(800, 0),
            Location = new Point(20, 46),
        };
        topPanel.Controls.Add(subtitle);

        // ── Buttons row ──
        int btnY = 90;

        var btnImport = new Button
        {
            Text = "📂  Імпортувати CSV",
            Size = new Size(200, 40),
            Location = new Point(20, btnY),
        };
        AppTheme.StyleButton(btnImport, AppTheme.AccentGreen);
        btnImport.Click += OnImportCsv;
        topPanel.Controls.Add(btnImport);

        var btnExample = new Button
        {
            Text = "⚡  Згенерувати приклад",
            Size = new Size(200, 40),
            Location = new Point(240, btnY),
        };
        AppTheme.StyleButton(btnExample, AppTheme.AccentBlue);
        btnExample.Click += OnGenerateExample;
        topPanel.Controls.Add(btnExample);

        _lblFilePath = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = AppTheme.TextDim,
            AutoSize = true,
            Location = new Point(460, btnY + 4),
        };
        topPanel.Controls.Add(_lblFilePath);

        _lblStatus = new Label
        {
            Text = "",
            Font = AppTheme.FontSmall,
            ForeColor = AppTheme.AccentGreen,
            AutoSize = true,
            Location = new Point(20, btnY + 48),
        };
        topPanel.Controls.Add(_lblStatus);

        // ── Instruction panel (shown when table is empty) ──
        _instructionPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.BgCard,
            Padding = new Padding(40),
        };

        var instructionText = new Label
        {
            Text = "📋  Як використовувати цей крок:\n\n" +
                   "1️⃣  Натисніть «Згенерувати приклад» — система створить карту\n" +
                   "     з продуктами A, B, C для кожної делянки вашої сітки.\n\n" +
                   "2️⃣  Або натисніть «Імпортувати CSV» якщо у вас є\n" +
                   "     готовий файл із призначеннями.\n\n" +
                   "📄  Формат CSV:\n" +
                   "     PlotId,Product\n" +
                   "     R1C1,Гербіцид_А\n" +
                   "     R1C2,Контроль\n" +
                   "     R2C1,Добриво_Б\n\n" +
                   "💡  Кожна делянка ідентифікується як RxCy\n" +
                   "     (R = рядок, C = колонка з кроку 2).",
            Font = new Font("Segoe UI", 11),
            ForeColor = AppTheme.TextSecondary,
            BackColor = AppTheme.BgCard,
            AutoSize = true,
            MaximumSize = new Size(700, 0),
            Location = new Point(40, 30),
        };
        _instructionPanel.Controls.Add(instructionText);

        // ── DataGridView (hidden initially behind instructions) ──
        _dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
            Visible = false,
        };
        AppTheme.StyleDataGrid(_dgv);
        _dgv.ReadOnly = true;

        _dgv.Columns.Add("Row", "Рядок");
        _dgv.Columns.Add("Column", "Колонка");
        _dgv.Columns.Add("PlotId", "ID Делянки");
        _dgv.Columns.Add("Product", "Продукт");

        _dgv.Columns["Row"]!.Width = 80;
        _dgv.Columns["Column"]!.Width = 80;
        _dgv.Columns["PlotId"]!.Width = 140;

        // Order matters: Fill controls added first render behind TopPanel
        Controls.Add(_dgv);
        Controls.Add(_instructionPanel);
        Controls.Add(topPanel);
    }

    /// <summary>Shows the table and hides instructions after data is loaded.</summary>
    private void ShowTable()
    {
        _instructionPanel.Visible = false;
        _dgv.Visible = true;
    }

    private void OnGenerateExample(object? sender, EventArgs e)
    {
        // Build a simple example map using the grid dimensions
        // Use products A, B, C cycled across plots
        string[] products = { "Продукт_А", "Продукт_Б", "Контроль" };
        var assignments = new Dictionary<string, string>();

        // Try to read grid size from previous step (default 10x20)
        int rows = 10;
        int cols = 20;

        // Find the GridStepPanel to get actual dimensions
        var mainForm = FindForm();
        if (mainForm != null)
        {
            foreach (Control c in mainForm.Controls)
            {
                var gridPanel = FindControlRecursive<GridSetupPanel>(c);
                if (gridPanel?.CurrentGrid != null)
                {
                    rows = gridPanel.CurrentGrid.Rows;
                    cols = gridPanel.CurrentGrid.Columns;
                    break;
                }
            }
        }

        int productIndex = 0;
        for (int r = 1; r <= rows; r++)
        {
            for (int c = 1; c <= cols; c++)
            {
                string plotId = $"R{r}C{c}";
                assignments[plotId] = products[productIndex % products.Length];
                productIndex++;
            }
        }

        CurrentTrialMap = new TrialMap { PlotAssignments = assignments };

        PopulateGrid();

        _lblFilePath.Text = "Згенеровано автоматично";
        _lblFilePath.ForeColor = AppTheme.AccentBlue;

        int plotCount = assignments.Count;
        int productCount = products.Length;
        _lblStatus.Text =
            $"✅ {plotCount} делянок, {productCount} продуктів: " +
            string.Join(", ", products);

        ShowTable();
        TrialMapChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Recursively finds a control of a specific type.</summary>
    private static T? FindControlRecursive<T>(Control parent) where T : Control
    {
        if (parent is T found) return found;
        foreach (Control child in parent.Controls)
        {
            var result = FindControlRecursive<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private void PopulateGrid()
    {
        if (CurrentTrialMap == null) return;

        _dgv.Rows.Clear();
        foreach (var (plotId, product) in
                 CurrentTrialMap.PlotAssignments.OrderBy(kv => kv.Key))
        {
            int row = 0, col = 0;
            if (plotId.StartsWith("R", StringComparison.OrdinalIgnoreCase))
            {
                int cIdx = plotId.IndexOf('C', StringComparison.OrdinalIgnoreCase);
                if (cIdx > 0)
                {
                    int.TryParse(plotId[1..cIdx], out row);
                    int.TryParse(plotId[(cIdx + 1)..], out col);
                }
            }
            _dgv.Rows.Add(row, col, plotId, product);
        }
    }

    private void OnImportCsv(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Імпорт Trial Map CSV",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
        };

        if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            CurrentTrialMap = TrialMapParser.Parse(ofd.FileName);
            PopulateGrid();

            int plotCount = CurrentTrialMap.PlotAssignments.Count;
            int productCount = CurrentTrialMap.Products.Count;
            _lblFilePath.Text = ofd.FileName;
            _lblFilePath.ForeColor = AppTheme.TextSecondary;
            _lblStatus.Text =
                $"✅ {plotCount} делянок, {productCount} продуктів: " +
                string.Join(", ", CurrentTrialMap.Products.OrderBy(p => p));

            ShowTable();
            TrialMapChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FormatException)
        {
            MessageBox.Show(
                $"Помилка завантаження CSV:\n{ex.Message}",
                "❌ Помилка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
