// Workflow: UI Modernization | Task: TrialMapPanel
using System.Drawing;
using System.Windows.Forms;
using PlotManager.Core.Models;
using PlotManager.Core.Services;

namespace PlotManager.UI.Controls;

/// <summary>
/// Wizard Step 3 — CSV import for trial map with dark-themed DataGridView.
/// Extracted from MainForm Tab 2.
/// </summary>
public sealed class TrialMapPanel : UserControl
{
    private DataGridView _dgv = null!;
    private Label _lblStatus = null!;
    private Label _lblFilePath = null!;

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
        // ── Top: header + buttons ──
        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 130,
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
            Text = "Імпортуйте CSV-файл з призначенням продуктів делянкам.\n" +
                   "Формат: PlotId, Product (напр. R1C1, Гербіцид_А)",
            Font = AppTheme.FontSmall,
            ForeColor = AppTheme.TextDim,
            AutoSize = true,
            MaximumSize = new Size(800, 0),
            Location = new Point(20, 44),
        };
        topPanel.Controls.Add(subtitle);

        var btnImport = new Button
        {
            Text = "📂  Імпортувати CSV...",
            Size = new Size(200, 40),
            Location = new Point(20, 80),
        };
        AppTheme.StyleButton(btnImport, AppTheme.AccentGreen);
        btnImport.Click += OnImportCsv;
        topPanel.Controls.Add(btnImport);

        _lblFilePath = new Label
        {
            Text = "Файл не завантажено",
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = AppTheme.TextDim,
            AutoSize = true,
            Location = new Point(240, 82),
        };
        topPanel.Controls.Add(_lblFilePath);

        _lblStatus = new Label
        {
            Text = "",
            Font = AppTheme.FontSmall,
            ForeColor = AppTheme.AccentGreen,
            AutoSize = true,
            Location = new Point(240, 102),
        };
        topPanel.Controls.Add(_lblStatus);

        // ── Bottom: DataGridView ──
        _dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
        };
        AppTheme.StyleDataGrid(_dgv);
        _dgv.ReadOnly = true;

        _dgv.Columns.Add("Row", "Рядок");
        _dgv.Columns.Add("Column", "Колонка");
        _dgv.Columns.Add("PlotId", "ID Делянки");
        _dgv.Columns.Add("Product", "Продукт");

        _dgv.Columns["Row"]!.Width = 70;
        _dgv.Columns["Column"]!.Width = 70;
        _dgv.Columns["PlotId"]!.Width = 120;

        Controls.Add(_dgv);
        Controls.Add(topPanel);
    }

    private void OnImportCsv(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Імпорт Trial Map CSV",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
        };

        if (ofd.ShowDialog() != DialogResult.OK) return;

        try
        {
            CurrentTrialMap = TrialMapParser.Parse(ofd.FileName);

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

            int plotCount = CurrentTrialMap.PlotAssignments.Count;
            int productCount = CurrentTrialMap.Products.Count;
            _lblFilePath.Text = ofd.FileName;
            _lblFilePath.ForeColor = AppTheme.TextSecondary;
            _lblStatus.Text =
                $"✅ {plotCount} делянок, {productCount} продуктів: " +
                string.Join(", ", CurrentTrialMap.Products.OrderBy(p => p));

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
