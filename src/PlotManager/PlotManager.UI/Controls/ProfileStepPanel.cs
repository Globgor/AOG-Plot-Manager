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
    private Label _lblConnections = null!;
    private Label _lblStatus = null!;

    /// <summary>The currently loaded profile, if any.</summary>
    public MachineProfile? Profile => _profile;

    /// <summary>Whether a valid profile is loaded.</summary>
    public bool IsValid => _profile != null;

    /// <summary>Fires when the profile changes.</summary>
    public event EventHandler? ProfileChanged;

    private static readonly string LastProfilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AOGPlotManager", "last_profile.txt");

    public ProfileStepPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.BgPrimary;
        BuildLayout();
        TryLoadLastProfile();
    }

    private void BuildLayout()
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(40, 30, 40, 20),
        };
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Row 0: header
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Row 1: summary card
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Row 2: spacer
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Row 3: buttons

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

        // ── Summary card (fixed height, appears right after header) ──
        _summaryCard = new Panel
        {
            Dock = DockStyle.Top,
            Height = 220,
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

        var btnManager = new Button { Text = "📋  Менеджер профілів", Width = 200 };
        AppTheme.StyleButtonOutline(btnManager);
        btnManager.Click += OnOpenProfileManager;
        btnManager.Margin = new Padding(12, 0, 0, 0);
        btnRow.Controls.Add(btnManager);

        var btnLoad = new Button { Text = "📂  З файлу", Width = 140 };
        AppTheme.StyleButtonOutline(btnLoad);
        btnLoad.Click += OnLoadFromFile;
        btnLoad.Margin = new Padding(12, 0, 0, 0);
        btnRow.Controls.Add(btnLoad);

        var btnNew = new Button { Text = "🆕  Новий", Width = 120 };
        AppTheme.StyleButtonOutline(btnNew);
        btnNew.Click += OnNewProfile;
        btnNew.Margin = new Padding(12, 0, 0, 0);
        btnRow.Controls.Add(btnNew);

        container.Controls.Add(btnRow, 0, 3);

        Controls.Add(container);
    }

    private void BuildSummaryContent()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 35),
                new ColumnStyle(SizeType.Percent, 65),
            },
            // Explicitly inherit card background so labels are visible
            BackColor = AppTheme.BgCard,
            ForeColor = AppTheme.TextPrimary,
            Padding = new Padding(16, 12, 16, 12),
        };
        // Each row auto-sizes to label content
        for (int i = 0; i < 4; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblStatus = new Label
        {
            Text = "⏳ Профіль не завантажено",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = AppTheme.AccentOrange,
            BackColor = AppTheme.BgCard,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };
        layout.SetColumnSpan(_lblStatus, 2);
        layout.Controls.Add(_lblStatus, 0, 0);

        _lblProfileName = AddInfoRow(layout, "📋 Назва:", "—", 1);
        _lblBoomCount = AddInfoRow(layout, "🔧 Штанги:", "—", 2);
        _lblConnections = AddInfoRow(layout, "🔌 З'єднання:", "—", 3);

        _summaryCard.Controls.Add(layout);
    }

    private static Label AddInfoRow(TableLayoutPanel parent, string label, string value, int row)
    {
        parent.Controls.Add(new Label
        {
            Text = label,
            Font = AppTheme.FontBody,
            ForeColor = AppTheme.TextSecondary,
            BackColor = AppTheme.BgCard,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 4),
        }, 0, row);

        var lbl = new Label
        {
            Text = value,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = AppTheme.TextPrimary,
            BackColor = AppTheme.BgCard,
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
        _summaryCard.Refresh();
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
            _lblConnections.Text = "—";
            return;
        }

        _lblStatus.Text = "✅ Профіль завантажено";
        _lblStatus.ForeColor = AppTheme.AccentGreen;
        _lblProfileName.Text = _profile.ProfileName;

        int enabled = _profile.Booms.Count(b => b.Enabled);
        _lblBoomCount.Text = $"{_profile.Booms.Count} шт. ({enabled} активних)";

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
        if (form.ShowDialog(FindForm()) == DialogResult.OK)
        {
            SetProfile(form.Profile);
            FormProfileManager.AutoSaveProfile(form.Profile);
            SaveLastProfilePath(form.Profile.ProfileName);
        }
    }

    private void OnNewProfile(object? sender, EventArgs e)
    {
        _profile = MachineProfile.CreateDefault();
        using var form = new FormMachineProfile(_profile);
        if (form.ShowDialog(FindForm()) == DialogResult.OK)
        {
            SetProfile(form.Profile);
            FormProfileManager.AutoSaveProfile(form.Profile);
            SaveLastProfilePath(form.Profile.ProfileName);
        }
    }

    private void OnOpenProfileManager(object? sender, EventArgs e)
    {
        using var mgr = new FormProfileManager();
        if (mgr.ShowDialog(FindForm()) == DialogResult.OK && mgr.SelectedProfile != null)
        {
            SetProfile(mgr.SelectedProfile);
            SaveLastProfilePath(mgr.SelectedProfile.ProfileName);
        }
    }

    private void OnLoadFromFile(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Machine Profile (*.json)|*.json",
            Title = "Завантажити профіль машини",
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var loaded = MachineProfile.LoadFromFile(dlg.FileName);
            SetProfile(loaded);
            SaveLastProfilePath(loaded.ProfileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Помилка завантаження:\n{ex.Message}",
                "❌ Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Saves the name of the last used profile for auto-load on next start.</summary>
    private static void SaveLastProfilePath(string profileName)
    {
        try
        {
            var dir = Path.GetDirectoryName(LastProfilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(LastProfilePath, profileName);
        }
        catch
        {
            // Non-critical — ignore silently
        }
    }

    /// <summary>Tries to auto-load the last used profile on panel initialization.</summary>
    private void TryLoadLastProfile()
    {
        try
        {
            if (!File.Exists(LastProfilePath)) return;

            var profileName = File.ReadAllText(LastProfilePath).Trim();
            if (string.IsNullOrEmpty(profileName)) return;

            // Search for matching profile in the profiles directory
            var profilesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AOGPlotManager", "profiles");

            if (!Directory.Exists(profilesDir)) return;

            foreach (var file in Directory.GetFiles(profilesDir, "*.json"))
            {
                try
                {
                    var p = MachineProfile.LoadFromFile(file);
                    if (p.ProfileName == profileName)
                    {
                        SetProfile(p);
                        return;
                    }
                }
                catch
                {
                    // Skip corrupt files
                }
            }
        }
        catch
        {
            // Non-critical — ignore silently
        }
    }
}
