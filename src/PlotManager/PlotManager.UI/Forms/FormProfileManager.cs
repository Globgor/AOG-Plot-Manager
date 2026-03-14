// Workflow: UI Modernization | Task: ProfileManager
using System.Drawing;
using System.Windows.Forms;
using PlotManager.Core.Models;

namespace PlotManager.UI.Forms;

/// <summary>
/// Dialog that lists saved machine profiles from the profiles directory,
/// allowing the user to select, delete, or create a new profile.
/// </summary>
public sealed class FormProfileManager : Form
{
    private readonly string _profilesDir;
    private ListBox _lstProfiles = null!;
    private Label _lblInfo = null!;
    private Label _lblDetails = null!;
    private MachineProfile? _selectedProfile;
    private string? _selectedPath;

    /// <summary>The profile chosen by the user (valid after DialogResult.OK).</summary>
    public MachineProfile? SelectedProfile => _selectedProfile;

    /// <summary>File path of the selected profile.</summary>
    public string? SelectedPath => _selectedPath;

    public FormProfileManager()
    {
        _profilesDir = GetProfilesDirectory();
        Directory.CreateDirectory(_profilesDir);
        BuildLayout();
        LoadProfileList();
    }

    /// <summary>Profiles live in user AppData for stability across rebuilds.</summary>
    public static string GetProfilesDirectory()
    {
        string appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "AOGPlotManager", "profiles");
    }

    /// <summary>
    /// Auto-save a profile to the profiles directory using its name as filename.
    /// </summary>
    public static void AutoSaveProfile(MachineProfile profile)
    {
        string dir = GetProfilesDirectory();
        Directory.CreateDirectory(dir);

        // Sanitize filename from profile name
        string safeName = SanitizeFileName(profile.ProfileName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "profile";

        string path = Path.Combine(dir, $"{safeName}.json");
        profile.SaveToFile(path);
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string result = new(name.Where(c => !invalid.Contains(c)).ToArray());
        return result.Trim();
    }

    private void BuildLayout()
    {
        Text = "📋 Менеджер профілів";
        Size = new Size(650, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.BgPrimary;
        ForeColor = AppTheme.FgPrimary;
        Font = new Font("Segoe UI", 10f);

        // ── Header ──
        var header = new Label
        {
            Text = "📋 Збережені профілі машин",
            Font = AppTheme.FontHeading,
            ForeColor = AppTheme.TextPrimary,
            Dock = DockStyle.Top,
            Height = 40,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 8, 0, 0),
        };

        _lblInfo = new Label
        {
            Text = "Оберіть профіль зі списку або створіть новий.",
            Font = AppTheme.FontBody,
            ForeColor = AppTheme.TextSecondary,
            Dock = DockStyle.Top,
            Height = 25,
            Padding = new Padding(14, 0, 0, 4),
        };

        // ── Profile list (owner-draw to force dark theme) ──
        _lstProfiles = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.BgCard,
            ForeColor = AppTheme.TextPrimary,
            Font = new Font("Segoe UI", 10.5f),
            BorderStyle = BorderStyle.None,
            ItemHeight = 32,
            DrawMode = DrawMode.OwnerDrawFixed,
        };
        _lstProfiles.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            e.DrawBackground();

            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var bgBrush = new SolidBrush(
                selected ? AppTheme.BgHover : AppTheme.BgCard);
            e.Graphics.FillRectangle(bgBrush, e.Bounds);

            using var fgBrush = new SolidBrush(AppTheme.TextPrimary);
            string text = _lstProfiles.Items[e.Index]?.ToString() ?? "";
            e.Graphics.DrawString(text, e.Font!, fgBrush,
                e.Bounds.X + 4, e.Bounds.Y + 4);
        };
        _lstProfiles.SelectedIndexChanged += OnSelectionChanged;
        _lstProfiles.DoubleClick += OnLoadSelected;

        // ── Details panel ──
        _lblDetails = new Label
        {
            Text = "",
            Dock = DockStyle.Bottom,
            Height = 80,
            BackColor = AppTheme.BgSecondary,
            ForeColor = AppTheme.TextSecondary,
            Font = AppTheme.FontBody,
            Padding = new Padding(14, 8, 14, 8),
        };

        // ── Buttons ──
        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 50,
            Padding = new Padding(8),
            BackColor = AppTheme.BgSecondary,
        };

        var btnLoad = AppTheme.MakeButton("Обрати", 130, AppTheme.AccentGreen);
        btnLoad.Click += OnLoadSelected;
        btnPanel.Controls.Add(btnLoad);

        var btnDelete = AppTheme.MakeButton("Видалити", 130, AppTheme.AccentRed);
        btnDelete.Click += OnDeleteSelected;
        btnDelete.Margin = new Padding(8, 0, 0, 0);
        btnPanel.Controls.Add(btnDelete);

        var btnNew = AppTheme.MakeButton("Новий", 130, AppTheme.AccentBlue);
        btnNew.Click += OnCreateNew;
        btnNew.Margin = new Padding(8, 0, 0, 0);
        btnPanel.Controls.Add(btnNew);

        var btnCancel = AppTheme.MakeButton("Закрити", 110);
        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Margin = new Padding(8, 0, 0, 0);
        btnPanel.Controls.Add(btnCancel);

        Controls.Add(_lstProfiles);
        Controls.Add(_lblDetails);
        Controls.Add(btnPanel);
        Controls.Add(_lblInfo);
        Controls.Add(header);

        CancelButton = btnCancel;
    }

    private void LoadProfileList()
    {
        _lstProfiles.Items.Clear();
        _selectedProfile = null;
        _selectedPath = null;

        if (!Directory.Exists(_profilesDir))
        {
            _lblInfo.Text = "📁 Папка профілів порожня. Створіть новий профіль.";
            return;
        }

        var files = Directory.GetFiles(_profilesDir, "*.json")
            .OrderByDescending(File.GetLastWriteTime)
            .ToArray();

        if (files.Length == 0)
        {
            _lblInfo.Text = "📁 Немає збережених профілів. Створіть новий.";
            return;
        }

        _lblInfo.Text = $"Знайдено {files.Length} профіль(ів).";

        foreach (string file in files)
        {
            try
            {
                var p = MachineProfile.LoadFromFile(file);
                string display = $"  {p.ProfileName}   " +
                    $"({p.Booms.Count} штанг)";
                _lstProfiles.Items.Add(new ProfileListItem(file, p, display));
            }
            catch
            {
                // Skip corrupted profile files
                _lstProfiles.Items.Add(new ProfileListItem(
                    file, null, $"  ⚠ {Path.GetFileName(file)} (пошкоджений)"));
            }
        }

        if (_lstProfiles.Items.Count > 0)
            _lstProfiles.SelectedIndex = 0;
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_lstProfiles.SelectedItem is not ProfileListItem item || item.Profile == null)
        {
            _lblDetails.Text = "";
            return;
        }

        var p = item.Profile;
        int enabled = p.Booms.Count(b => b.Enabled);
        _lblDetails.Text =
            $"📋 {p.ProfileName}   |   " +
            $"🔧 {p.Booms.Count} штанг ({enabled} акт.)\n" +
            $"📅 {File.GetLastWriteTime(item.FilePath):yyyy-MM-dd HH:mm}   |   " +
            $"📁 {Path.GetFileName(item.FilePath)}";
    }

    private void OnLoadSelected(object? sender, EventArgs e)
    {
        if (_lstProfiles.SelectedItem is not ProfileListItem item || item.Profile == null)
        {
            MessageBox.Show("Оберіть профіль зі списку.", "ℹ",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _selectedProfile = item.Profile;
        _selectedPath = item.FilePath;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnDeleteSelected(object? sender, EventArgs e)
    {
        if (_lstProfiles.SelectedItem is not ProfileListItem item)
        {
            MessageBox.Show("Оберіть профіль для видалення.", "ℹ",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Видалити профіль?\n\n{Path.GetFileName(item.FilePath)}",
            "🗑 Підтвердження",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes) return;

        try
        {
            File.Delete(item.FilePath);
            LoadProfileList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Помилка видалення:\n{ex.Message}", "❌",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnCreateNew(object? sender, EventArgs e)
    {
        var newProfile = MachineProfile.CreateDefault();
        using var form = new FormMachineProfile(newProfile);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            AutoSaveProfile(form.Profile);
            _selectedProfile = form.Profile;
            _selectedPath = null;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Wrapper to pair file path + loaded profile for ListBox.</summary>
    private sealed class ProfileListItem
    {
        public string FilePath { get; }
        public MachineProfile? Profile { get; }
        private readonly string _display;

        public ProfileListItem(string filePath, MachineProfile? profile, string display)
        {
            FilePath = filePath;
            Profile = profile;
            _display = display;
        }

        public override string ToString() => _display;
    }
}
