// Workflow: UI Modernization | Task: ProfileStepPanel
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PlotManager.Core.Models;
using PlotManager.UI.Forms;

namespace PlotManager.UI.Controls;

/// <summary>
/// Wizard Step 1 — shows the current machine profile summary
/// and provides buttons to create/edit/load a profile.
/// </summary>
public sealed class ProfileStepPanel : UserControl
{
    private MachineProfile? _profile;
    private Panel _summaryCard = null!;
    private Label _lblProfileName = null!;
    private Label _lblBoomCount = null!;
    private Label _lblNozzle = null!;
    private Label _lblSpeed = null!;
    private Label _lblConnections = null!;
    private Label _lblStatus = null!;

    /// <summary>The currently loaded profile, if any.</summary>
    public MachineProfile? Profile => _profile;

    /// <summary>Whether a valid profile is loaded.</summary>
    public bool IsValid => _profile != null;

    /// <summary>Fires when the profile changes.</summary>
    public event EventHandler? ProfileChanged;

    public ProfileStepPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.BgPrimary;
        BuildLayout();
    }

    private void BuildLayout()
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(40, 30, 40, 20),
        };
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // ── Header ──
        var header = new Label
        {
            Text = "Крок 1: Профіль машини",
            Font = AppTheme.FontHeading,
            ForeColor = AppTheme.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };
        container.Controls.Add(header, 0, 0);

        var subtitle = AppTheme.CreateHelpText(
            "Налаштуйте параметри обприскувача: штанги, форсунки, затримки, з'єднання.\n" +
            "Профіль можна зберегти у файл та завантажити пізніше.");
        container.Controls.Add(subtitle, 0, 0);

        // ── Summary card ──
        _summaryCard = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 12),
        };
        AppTheme.StyleCard(_summaryCard);
        BuildSummaryContent();
        container.Controls.Add(_summaryCard, 0, 1);

        // ── Action buttons ──
        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 50,
            AutoSize = true,
        };
        btnRow.BackColor = AppTheme.BgPrimary;

        var btnEdit = new Button { Text = "✏  Редагувати профіль", Width = 200 };
        AppTheme.StyleButton(btnEdit, AppTheme.AccentBlue);
        btnEdit.Click += OnEditProfile;
        btnRow.Controls.Add(btnEdit);

        var btnLoad = new Button { Text = "📂  Завантажити з файлу", Width = 200 };
        AppTheme.StyleButtonOutline(btnLoad);
        btnLoad.Click += OnLoadFromFile;
        btnLoad.Margin = new Padding(12, 0, 0, 0);
        btnRow.Controls.Add(btnLoad);

        var btnNew = new Button { Text = "🆕  Новий профіль", Width = 180 };
        AppTheme.StyleButtonOutline(btnNew);
        btnNew.Click += OnNewProfile;
        btnNew.Margin = new Padding(12, 0, 0, 0);
        btnRow.Controls.Add(btnNew);

        container.Controls.Add(btnRow, 0, 2);

        Controls.Add(container);
    }

    private void BuildSummaryContent()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 50),
                new ColumnStyle(SizeType.Percent, 50),
            },
            Padding = new Padding(8),
        };

        _lblStatus = new Label
        {
            Text = "⏳ Профіль не завантажено",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = AppTheme.AccentOrange,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16),
        };
        layout.SetColumnSpan(_lblStatus, 2);
        layout.Controls.Add(_lblStatus, 0, 0);

        _lblProfileName = AddInfoRow(layout, "📋 Назва:", "—", 1);
        _lblBoomCount = AddInfoRow(layout, "🔧 Штанги:", "—", 2);
        _lblNozzle = AddInfoRow(layout, "💧 Форсунка:", "—", 3);
        _lblSpeed = AddInfoRow(layout, "🚜 Швидкість:", "—", 4);
        _lblConnections = AddInfoRow(layout, "🔌 З'єднання:", "—", 5);

        _summaryCard.Controls.Add(layout);
    }

    private static Label AddInfoRow(TableLayoutPanel parent, string label, string value, int row)
    {
        parent.Controls.Add(new Label
        {
            Text = label,
            Font = AppTheme.FontBody,
            ForeColor = AppTheme.TextSecondary,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 4),
        }, 0, row);

        var lbl = new Label
        {
            Text = value,
            Font = AppTheme.FontBody,
            ForeColor = AppTheme.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 4),
        };
        parent.Controls.Add(lbl, 1, row);
        return lbl;
    }

    /// <summary>Set the profile and update the summary display.</summary>
    public void SetProfile(MachineProfile profile)
    {
        _profile = profile;
        UpdateSummary();
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSummary()
    {
        if (_profile == null)
        {
            _lblStatus.Text = "⏳ Профіль не завантажено";
            _lblStatus.ForeColor = AppTheme.AccentOrange;
            _lblProfileName.Text = "—";
            _lblBoomCount.Text = "—";
            _lblNozzle.Text = "—";
            _lblSpeed.Text = "—";
            _lblConnections.Text = "—";
            return;
        }

        _lblStatus.Text = "✅ Профіль завантажено";
        _lblStatus.ForeColor = AppTheme.AccentGreen;
        _lblProfileName.Text = _profile.ProfileName;

        int enabled = _profile.Booms.Count(b => b.Enabled);
        _lblBoomCount.Text = $"{_profile.Booms.Count} шт. ({enabled} активних)";

        _lblNozzle.Text = string.IsNullOrEmpty(_profile.Nozzle.Model)
            ? "Не задано"
            : $"{_profile.Nozzle.Model} ({_profile.Nozzle.ColorCode}), " +
              $"{_profile.Nozzle.FlowRateLPerMin:F1} л/хв, " +
              $"норма {_profile.TargetRateLPerHa:F0} л/га";

        _lblSpeed.Text = $"{_profile.TargetSpeedKmh:F1} ± {_profile.SpeedToleranceKmh:F1} км/год";

        _lblConnections.Text =
            $"Teensy: {_profile.Connection.TeensyComPort}, " +
            $"AOG: {_profile.Connection.AogHost}:" +
            $"{_profile.Connection.AogUdpListenPort}/{_profile.Connection.AogUdpSendPort}";
    }

    private void OnEditProfile(object? sender, EventArgs e)
    {
        // Ensure we have a profile to edit (create default if none)
        _profile ??= MachineProfile.CreateDefault();

        using var form = new FormMachineProfile(_profile);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            SetProfile(form.Profile);
        }
    }

    private void OnNewProfile(object? sender, EventArgs e)
    {
        _profile = MachineProfile.CreateDefault();
        using var form = new FormMachineProfile(_profile);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            SetProfile(form.Profile);
        }
    }

    private void OnLoadFromFile(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Machine Profile (*.json)|*.json",
            Title = "Завантажити профіль машини",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            var loaded = MachineProfile.LoadFromFile(dlg.FileName);
            SetProfile(loaded);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Помилка завантаження:\n{ex.Message}",
                "❌ Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
