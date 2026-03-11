// Workflow: UI Modernization | Task: RoutingPanel
using System.Drawing;
using System.Windows.Forms;
using PlotManager.Core.Models;

namespace PlotManager.UI.Controls;

/// <summary>
/// Wizard Step 4 — Product → Boom Section assignment with dark-themed grid.
/// Extracted from MainForm Tab 3.
/// </summary>
public sealed class RoutingPanel : UserControl
{
    private DataGridView _dgv = null!;
    private Label _lblStatus = null!;
    private Button _btnSave = null!;

    /// <summary>The saved routing, if any.</summary>
    public HardwareRouting? CurrentRouting { get; private set; }

    /// <summary>Whether routing is complete and valid.</summary>
    public bool IsValid => CurrentRouting != null;

    /// <summary>Fires when routing is saved.</summary>
    public event EventHandler? RoutingChanged;

    /// <summary>The trial map that provides the product list.</summary>
    public TrialMap? TrialMap { get; private set; }

    public RoutingPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.BgPrimary;
        BuildLayout();
    }

    private void BuildLayout()
    {
        // ── Header with detailed explanation ──
        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 180,
            BackColor = AppTheme.BgPrimary,
            Padding = new Padding(20, 16, 20, 8),
        };

        var header = new Label
        {
            Text = "Крок 4: Маршрутизація продуктів → Секції штанги",
            Font = AppTheme.FontHeading,
            ForeColor = AppTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(20, 12),
        };
        topPanel.Controls.Add(header);

        var subtitle = new Label
        {
            Text = "⚙ ЩО ЦЕ ТАКЕ:\n" +
                   "Кожен продукт із Trial Map потрібно призначити конкретній " +
                   "фізичній секції штанги (1–14).\n" +
                   "Секція = один каністр/канал клапана на обприскувачі.\n\n" +
                   "📋 ЯК НАЛАШТУВАТИ:\n" +
                   "1. Оберіть номер секції для кожного продукту зі списку\n" +
                   "2. Один продукт = одна секція (дублікати заборонені)\n" +
                   "3. Натисніть 💾 Зберегти для підтвердження",
            Font = AppTheme.FontSmall,
            ForeColor = AppTheme.TextSecondary,
            AutoSize = true,
            MaximumSize = new Size(800, 0),
            Location = new Point(20, 40),
        };
        topPanel.Controls.Add(subtitle);

        _btnSave = new Button
        {
            Text = "💾  Зберегти маршрутизацію",
            Size = new Size(220, 40),
            Location = new Point(20, 132),
            Enabled = false,
        };
        AppTheme.StyleButton(_btnSave, AppTheme.AccentOrange);
        _btnSave.Click += OnSaveRouting;
        topPanel.Controls.Add(_btnSave);

        _lblStatus = new Label
        {
            Text = "⏳ Спочатку імпортуйте Trial Map (Крок 3)",
            Font = AppTheme.FontSmall,
            ForeColor = AppTheme.TextDim,
            AutoSize = true,
            Location = new Point(260, 140),
        };
        topPanel.Controls.Add(_lblStatus);

        // ── DataGridView ──
        _dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
        };
        AppTheme.StyleDataGrid(_dgv);
        
        // Prevent default DataError dialog boxes on ComboBox cells
        _dgv.DataError += (s, ev) => ev.ThrowException = false;

        var productCol = new DataGridViewTextBoxColumn
        {
            Name = "Product",
            HeaderText = "Продукт",
            ReadOnly = true,
            FillWeight = 60,
        };
        _dgv.Columns.Add(productCol);

        var sectionCol = new DataGridViewComboBoxColumn
        {
            Name = "Section",
            HeaderText = "Секція штанги (1–14)",
            FillWeight = 40,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
        };
        for (int i = 1; i <= HardwareRouting.TotalSections; i++)
            sectionCol.Items.Add(i);
        _dgv.Columns.Add(sectionCol);

        Controls.Add(_dgv);
        Controls.Add(topPanel);
    }

    /// <summary>Populate the grid with products from the trial map.</summary>
    public void SetTrialMap(TrialMap trialMap)
    {
        TrialMap = trialMap;
        _dgv.Rows.Clear();

        foreach (string product in trialMap.Products.OrderBy(p => p))
        {
            int rowIdx = _dgv.Rows.Add(product, null);
            _dgv.Rows[rowIdx].Cells["Product"].ReadOnly = true;
        }

        _btnSave.Enabled = true;
        _lblStatus.Text = $"{trialMap.Products.Count} продуктів потребують призначення";
        _lblStatus.ForeColor = AppTheme.AccentOrange;
    }

    private void OnSaveRouting(object? sender, EventArgs e)
    {
        if (TrialMap == null) return;

        var productToSections = new Dictionary<string, List<int>>();
        var sectionToProduct = new Dictionary<int, string>();
        var errors = new List<string>();

        foreach (DataGridViewRow row in _dgv.Rows)
        {
            string? product = row.Cells["Product"].Value?.ToString();
            object? sectionVal = row.Cells["Section"].Value;

            if (string.IsNullOrEmpty(product)) continue;

            if (sectionVal == null || sectionVal == DBNull.Value)
            {
                errors.Add($"Продукт '{product}' не має призначеної секції.");
                continue;
            }

            int section = Convert.ToInt32(sectionVal);
            int sectionIdx = section - 1;

            if (sectionToProduct.ContainsKey(sectionIdx))
            {
                errors.Add(
                    $"Секція {section} призначена і '{sectionToProduct[sectionIdx]}', і '{product}'.");
                continue;
            }

            productToSections[product] = new List<int> { sectionIdx };
            sectionToProduct[sectionIdx] = product;
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(
                "Помилки маршрутизації:\n\n" +
                string.Join("\n", errors.Select(e => $"• {e}")),
                "⚠ Валідація", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        CurrentRouting = new HardwareRouting
        {
            ProductToSections = productToSections,
            SectionToProduct = sectionToProduct,
        };

        // Validate against trial map
        var validationErrors = CurrentRouting.Validate(TrialMap);
        if (validationErrors.Count > 0)
        {
            MessageBox.Show(
                "Не всі продукти призначені:\n\n" +
                string.Join("\n", validationErrors.Select(e => $"• {e}")),
                "⚠ Незавершено", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _lblStatus.Text =
            $"✅ Маршрутизація збережена — {sectionToProduct.Count} секцій призначено";
        _lblStatus.ForeColor = AppTheme.AccentGreen;

        RoutingChanged?.Invoke(this, EventArgs.Empty);
    }
}
