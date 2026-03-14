using System.Windows.Forms;

namespace PlotManager.UI.Forms;

using PlotManager.Core.Models;
using PlotManager.Core.Services;

/// <summary>
/// Machine Profile editor — 6-tab dialog (nozzle-first) with dark theme.
/// Integrates NozzleCatalog dropdown and live RateCalculator panel.
/// Modal: returns DialogResult.OK with populated Profile.
/// </summary>
public class FormMachineProfile : Form
{
    private readonly MachineProfile _profile;

    // ── Tab 1: Booms ──
    private DataGridView _dgvBooms = null!;

    // ── Tab 3: Delays ──
    private NumericUpDown _nudActivationDelay = null!;
    private NumericUpDown _nudDeactivationDelay = null!;
    private NumericUpDown _nudPreActivation = null!;
    private NumericUpDown _nudPreDeactivation = null!;

    // ── Tab 4: GPS ──
    private NumericUpDown _nudCogThreshold = null!;
    private NumericUpDown _nudGpsHz = null!;

    // ── Tab 5: Connections ──
    private TextBox _txtTeensyPort = null!;
    private NumericUpDown _nudBaudRate = null!;
    private NumericUpDown _nudAogListenPort = null!;
    private NumericUpDown _nudAogSendPort = null!;
    private TextBox _txtAogHost = null!;

    // ── Tab 6: Identity ──
    private TextBox _txtProfileName = null!;
    private TextBox _txtNotes = null!;

    /// <summary>The configured profile (valid after DialogResult.OK).</summary>
    public MachineProfile Profile => _profile;

    public FormMachineProfile(MachineProfile? existingProfile = null)
    {
        _profile = existingProfile ?? MachineProfile.CreateDefault();
        InitializeComponents();
        PopulateFromProfile();
    }

    private void InitializeComponents()
    {
        Text = "⚙ Machine Profile — Профіль Обладнання";
        Size = new System.Drawing.Size(900, 1000);
        MinimumSize = new System.Drawing.Size(800, 700);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.5f);
        BackColor = AppTheme.BgPrimary;
        ForeColor = AppTheme.FgPrimary;

        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new System.Drawing.Point(12, 6),
        };
        AppTheme.StyleTabControl(tabControl);

        // Identity first — name the profile before configuring
        tabControl.TabPages.Add(BuildTabGeneral());
        tabControl.TabPages.Add(BuildTabBooms());
        tabControl.TabPages.Add(BuildTabDelays());
        tabControl.TabPages.Add(BuildTabSpeedGps());
        tabControl.TabPages.Add(BuildTabConnections());

        // ── Bottom buttons ──
        var panelButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 50,
            Padding = new Padding(8),
            BackColor = AppTheme.BgSecondary,
        };

        var btnOk = AppTheme.MakeButton("✅", 120, AppTheme.Accent);
        // No DialogResult on button — CollectToProfile sets it after validation
        var btnCancel = AppTheme.MakeButton("Скасувати", 100);
        btnCancel.DialogResult = DialogResult.Cancel;
        var btnSaveFile = AppTheme.MakeButton("💾 У файл", 110);
        var btnLoadFile = AppTheme.MakeButton("📂 З файлу", 110);

        btnOk.Click += (_, _) =>
        {
            if (TryCollectToProfile())
                DialogResult = DialogResult.OK;
        };
        btnSaveFile.Click += OnSaveToFile;
        btnLoadFile.Click += OnLoadFromFile;

        panelButtons.Controls.AddRange(new Control[] { btnOk, btnCancel, btnSaveFile, btnLoadFile });

        Controls.Add(tabControl);
        Controls.Add(panelButtons);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    // ════════════════════════════════════════════════════════════════════
    // Tab 2: Booms — with add/remove buttons
    // ════════════════════════════════════════════════════════════════════
    private TabPage BuildTabBooms()
    {
        var tab = new TabPage("🔧 Штанги");

        // Toolbar with add/remove buttons
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(4),
            BackColor = AppTheme.BgSecondary,
        };

        var btnAdd = AppTheme.MakeButton("➕ Додати штангу", 160, AppTheme.Success);
        var btnRemove = AppTheme.MakeButton("➖ Видалити обрані", 160, AppTheme.Error);
        var lblCount = new Label
        {
            Text = "",
            ForeColor = AppTheme.FgSecondary,
            AutoSize = true,
            Margin = new Padding(12, 8, 0, 0),
        };

        btnAdd.Click += (_, _) =>
        {
            int nextId = _dgvBooms.Rows.Count;
            _dgvBooms.Rows.Add(
                nextId + 1, $"Boom {nextId + 1}", nextId,
                "0,00", "0,00", "0,25", "70", "30", "-1", "-1", true);
            lblCount.Text = $"Штанг: {_dgvBooms.Rows.Count}";
        };

        btnRemove.Click += (_, _) =>
        {
            if (_dgvBooms.SelectedRows.Count == 0)
            {
                MessageBox.Show("Оберіть рядки для видалення.", "⚠",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            foreach (DataGridViewRow row in _dgvBooms.SelectedRows
                .Cast<DataGridViewRow>().OrderByDescending(r => r.Index))
            {
                _dgvBooms.Rows.RemoveAt(row.Index);
            }
            lblCount.Text = $"Штанг: {_dgvBooms.Rows.Count}";
        };

        toolbar.Controls.AddRange(new Control[] { btnAdd, btnRemove, lblCount });

        _dgvBooms = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowTemplate = { Height = 28 },
            ColumnHeadersHeight = 32,
            DefaultCellStyle = { Padding = new Padding(4, 2, 4, 2) },
        };
        AppTheme.StyleDataGrid(_dgvBooms);

        _dgvBooms.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { HeaderText = "#", Name = "BoomId", ReadOnly = true, Width = 30 },
            new DataGridViewTextBoxColumn { HeaderText = "Ім'я", Name = "Name", MinimumWidth = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "Канал", Name = "ValveChannel", Width = 45 },
            new DataGridViewTextBoxColumn { HeaderText = "Y (м)", Name = "YOffset", Width = 55,
                ToolTipText = "Зміщення від антени (- = позаду)" },
            new DataGridViewTextBoxColumn { HeaderText = "X (м)", Name = "XOffset", Width = 55,
                ToolTipText = "Зміщення від осі (+ = вправо)" },
            new DataGridViewTextBoxColumn { HeaderText = "Шир.(м)", Name = "SprayWidth", Width = 55 },
            new DataGridViewTextBoxColumn { HeaderText = "Акт.%", Name = "ActOverlap", Width = 45 },
            new DataGridViewTextBoxColumn { HeaderText = "Деакт.%", Name = "DeactOverlap", Width = 50 },
            new DataGridViewTextBoxColumn { HeaderText = "Акт.мс", Name = "ActDelay", Width = 50,
                ToolTipText = "-1 = глобальне значення" },
            new DataGridViewTextBoxColumn { HeaderText = "Деакт.мс", Name = "DeactDelay", Width = 55,
                ToolTipText = "-1 = глобальне значення" },
            new DataGridViewCheckBoxColumn { HeaderText = "Вкл", Name = "Enabled", Width = 35 },
        });

        tab.Controls.Add(_dgvBooms);
        tab.Controls.Add(toolbar);
        return tab;
    }

    // ════════════════════════════════════════════════════════════════════
    // Tab 3: Delays
    // ════════════════════════════════════════════════════════════════════
    private TabPage BuildTabDelays()
    {
        var tab = new TabPage("⏱ Затримки");
        var p = MakePanel();

        AddSectionHeader(p, "Гідравлічні затримки (глобальні)", ref _row);
        _nudActivationDelay = AddNumericRow(p,
            "Затримка активації (мс):", 0, 2000, 10, 300);
        _nudDeactivationDelay = AddNumericRow(p,
            "Затримка деактивації (мс):", 0, 2000, 10, 150);

        AddInfoLabel(p,
            "⚡ Якщо довжини шлангів відрізняються — задайте per-boom " +
            "перевизначення на вкладці «Штанги» (колонки Акт.мс / Деакт.мс, " +
            "-1 = використовувати глобальне).");

        AddSectionHeader(p, "Просторові відступи", ref _row);
        _nudPreActivation = AddNumericRow(p,
            "Преактивація (м):", 0, 5, 0.01m, 0.50m);
        _nudPreDeactivation = AddNumericRow(p,
            "Предеактивація (м):", 0, 5, 0.01m, 0.20m);

        tab.Controls.Add(p);
        return tab;
    }

    // ════════════════════════════════════════════════════════════════════
    // Tab 4: GPS
    // ════════════════════════════════════════════════════════════════════
    private TabPage BuildTabSpeedGps()
    {
        var tab = new TabPage("📡 GPS");
        var p = MakePanel();

        AddSectionHeader(p, "GPS", ref _row);
        _nudGpsHz = AddNumericRow(p,
            "Частота GPS (Гц):", 1, 20, 1, 10);

        AddSectionHeader(p, "Крабовий хід (COG)", ref _row);
        _nudCogThreshold = AddNumericRow(p,
            "Поріг Heading/COG (°):", 0.5m, 15, 0.5m, 3.0m);
        AddInfoLabel(p,
            "Якщо різниця Heading − COG > порога → задні штанги " +
            "проєкуються по COG (компенсація бокового зносу на схилі).");

        tab.Controls.Add(p);
        return tab;
    }

    // ════════════════════════════════════════════════════════════════════
    // Tab 5: Connections (serial + UDP)
    // ════════════════════════════════════════════════════════════════════
    private TabPage BuildTabConnections()
    {
        var tab = new TabPage("🔌 Зв'язок");
        var p = MakePanel();

        AddSectionHeader(p, "Teensy", ref _row);
        _txtTeensyPort = AddTextRow(p, "COM-порт:");
        _nudBaudRate = AddNumericRow(p, "Baud rate:", 9600, 1000000, 100, 115200);

        AddSectionHeader(p, "AgOpenGPS (UDP)", ref _row);
        _txtAogHost = AddTextRow(p, "AOG Host:");
        _nudAogListenPort = AddNumericRow(p, "Порт прийому:", 1024, 65535, 1, 8888);
        _nudAogSendPort = AddNumericRow(p, "Порт відправки:", 1024, 65535, 1, 9999);

        tab.Controls.Add(p);
        return tab;
    }

    // ════════════════════════════════════════════════════════════════════
    // Tab 6: General identity
    // ════════════════════════════════════════════════════════════════════
    private TabPage BuildTabGeneral()
    {
        var tab = new TabPage("📋 Профіль");
        var p = MakePanel();

        AddSectionHeader(p, "Ідентифікація", ref _row);
        _txtProfileName = AddTextRow(p, "Ім'я профілю:");
        _txtNotes = AddTextRow(p, "Нотатки:");

        tab.Controls.Add(p);
        return tab;
    }

    // ════════════════════════════════════════════════════════════════════
    // Populate / Collect
    // ════════════════════════════════════════════════════════════════════

    private void PopulateFromProfile()
    {
        // Clamp before assigning to NumericUpDown.Value to avoid ArgumentOutOfRangeException
        // when a profile was saved with different NUD bounds.
        static decimal C(decimal v, NumericUpDown n) => Math.Clamp(v, n.Minimum, n.Maximum);

        // Tab 6: Identity
        _txtProfileName.Text = _profile.ProfileName;
        _txtNotes.Text = _profile.Notes;

        // Tab 3: Delays
        _nudActivationDelay.Value   = C((decimal)_profile.SystemActivationDelayMs,  _nudActivationDelay);
        _nudDeactivationDelay.Value = C((decimal)_profile.SystemDeactivationDelayMs,_nudDeactivationDelay);
        _nudPreActivation.Value     = C((decimal)_profile.PreActivationMeters,       _nudPreActivation);
        _nudPreDeactivation.Value   = C((decimal)_profile.PreDeactivationMeters,     _nudPreDeactivation);

        // Tab 4: GPS
        _nudGpsHz.Value          = C(_profile.GpsUpdateRateHz,                       _nudGpsHz);
        _nudCogThreshold.Value   = C((decimal)_profile.CogHeadingThresholdDegrees,  _nudCogThreshold);

        // Tab 2: Booms (centimeter precision)
        _dgvBooms.Rows.Clear();
        foreach (BoomProfile bp in _profile.Booms)
        {
            _dgvBooms.Rows.Add(
                bp.BoomId + 1,
                bp.Name,
                bp.ValveChannel,
                bp.YOffsetMeters.ToString("F2"),
                bp.XOffsetMeters.ToString("F2"),
                bp.SprayWidthMeters.ToString("F2"),
                bp.ActivationOverlapPercent.ToString("F0"),
                bp.DeactivationOverlapPercent.ToString("F0"),
                bp.ActivationDelayOverrideMs.ToString("F0"),
                bp.DeactivationDelayOverrideMs.ToString("F0"),
                bp.Enabled);
        }

        // Tab 5: Connections
        _txtTeensyPort.Text = _profile.Connection.TeensyComPort;
        _nudBaudRate.Value = C(_profile.Connection.TeensyBaudRate, _nudBaudRate);
        _txtAogHost.Text = _profile.Connection.AogHost;
        _nudAogListenPort.Value = C(_profile.Connection.AogUdpListenPort, _nudAogListenPort);
        _nudAogSendPort.Value   = C(_profile.Connection.AogUdpSendPort,   _nudAogSendPort);
    }

    /// <summary>Collects all form data into _profile. Returns false on validation error.</summary>
    private bool TryCollectToProfile()
    {
        // Tab 6: Identity
        _profile.ProfileName = _txtProfileName.Text;
        _profile.Notes = _txtNotes.Text;

        // Tab 3: Delays
        _profile.SystemActivationDelayMs = (double)_nudActivationDelay.Value;
        _profile.SystemDeactivationDelayMs = (double)_nudDeactivationDelay.Value;
        _profile.PreActivationMeters = (double)_nudPreActivation.Value;
        _profile.PreDeactivationMeters = (double)_nudPreDeactivation.Value;

        // Tab 4: GPS
        _profile.GpsUpdateRateHz = (int)_nudGpsHz.Value;
        _profile.CogHeadingThresholdDegrees = (double)_nudCogThreshold.Value;

        // Tab 2: Booms
        _profile.Booms.Clear();
        var boomErrors = new List<string>();
        foreach (DataGridViewRow r in _dgvBooms.Rows)
        {
            try
            {
                _profile.Booms.Add(new BoomProfile
                {
                    BoomId = Convert.ToInt32(r.Cells["BoomId"].Value) - 1,
                    Name = r.Cells["Name"].Value?.ToString() ?? "",
                    ValveChannel = Convert.ToInt32(r.Cells["ValveChannel"].Value),
                    YOffsetMeters = Convert.ToDouble(r.Cells["YOffset"].Value),
                    XOffsetMeters = Convert.ToDouble(r.Cells["XOffset"].Value),
                    SprayWidthMeters = Convert.ToDouble(r.Cells["SprayWidth"].Value),
                    ActivationOverlapPercent = Convert.ToDouble(r.Cells["ActOverlap"].Value),
                    DeactivationOverlapPercent = Convert.ToDouble(r.Cells["DeactOverlap"].Value),
                    ActivationDelayOverrideMs = Convert.ToDouble(r.Cells["ActDelay"].Value),
                    DeactivationDelayOverrideMs = Convert.ToDouble(r.Cells["DeactDelay"].Value),
                    Enabled = Convert.ToBoolean(r.Cells["Enabled"].Value),
                });
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                boomErrors.Add($"Boom row {r.Index + 1}: {ex.Message}");
            }
        }

        if (boomErrors.Count > 0)
        {
            MessageBox.Show(
                "Помилки в даних штанг:\n\n" + string.Join("\n", boomErrors.Select(e => $"• {e}")),
                "❌ Помилка валідації", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // Tab 5: Connections
        _profile.Connection.TeensyComPort = _txtTeensyPort.Text;
        _profile.Connection.TeensyBaudRate = (int)_nudBaudRate.Value;
        _profile.Connection.AogHost = _txtAogHost.Text;
        _profile.Connection.AogUdpListenPort = (int)_nudAogListenPort.Value;
        _profile.Connection.AogUdpSendPort = (int)_nudAogSendPort.Value;

        _profile.LastModifiedUtc = DateTime.UtcNow;

        // Validate before accepting
        try
        {
            _profile.Validate();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(
                $"Профіль невалідний:\n\n{ex.Message}",
                "❌ Помилка валідації", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    // ════════════════════════════════════════════════════════════════════
    // File IO
    // ════════════════════════════════════════════════════════════════════

    private void OnSaveToFile(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Machine Profile (*.json)|*.json",
            DefaultExt = ".json",
            FileName = $"{_profile.ProfileName}.json",
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            TryCollectToProfile();
            _profile.SaveToFile(dlg.FileName);
            MessageBox.Show($"Профіль збережено:\n{dlg.FileName}", "✅ Збережено",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OnLoadFromFile(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Machine Profile (*.json)|*.json",
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            try
            {
                MachineProfile loaded = MachineProfile.LoadFromFile(dlg.FileName);
                CopyProfile(loaded, _profile);
                PopulateFromProfile();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка завантаження:\n{ex.Message}", "❌ Помилка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private static void CopyProfile(MachineProfile src, MachineProfile dst)
    {
        dst.ProfileName = src.ProfileName;
        dst.Notes = src.Notes;
        dst.SystemActivationDelayMs = src.SystemActivationDelayMs;
        dst.SystemDeactivationDelayMs = src.SystemDeactivationDelayMs;
        dst.PreActivationMeters = src.PreActivationMeters;
        dst.PreDeactivationMeters = src.PreDeactivationMeters;
        dst.CogHeadingThresholdDegrees = src.CogHeadingThresholdDegrees;
        dst.GpsUpdateRateHz = src.GpsUpdateRateHz;
        dst.Connection = src.Connection;

        // Deep-copy Booms to prevent source mutation
        dst.Booms = src.Booms.Select(b => new BoomProfile
        {
            BoomId = b.BoomId,
            Name = b.Name,
            ValveChannel = b.ValveChannel,
            YOffsetMeters = b.YOffsetMeters,
            XOffsetMeters = b.XOffsetMeters,
            SprayWidthMeters = b.SprayWidthMeters,
            ActivationOverlapPercent = b.ActivationOverlapPercent,
            DeactivationOverlapPercent = b.DeactivationOverlapPercent,
            ActivationDelayOverrideMs = b.ActivationDelayOverrideMs,
            DeactivationDelayOverrideMs = b.DeactivationDelayOverrideMs,
            Enabled = b.Enabled,
        }).ToList();
    }

    // ════════════════════════════════════════════════════════════════════
    // Layout helpers — dark theme with proper spacing
    // ════════════════════════════════════════════════════════════════════

    private int _row;

    private TableLayoutPanel MakePanel()
    {
        _row = 0;
        var p = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 42),
                new ColumnStyle(SizeType.Percent, 58),
            },
            AutoScroll = true,
            Padding = new Padding(16, 12, 16, 12),
            BackColor = AppTheme.BgPrimary,
            ForeColor = AppTheme.FgPrimary,
        };
        return p;
    }

    private static void AddSectionHeader(TableLayoutPanel p, string text, ref int row)
    {
        var lbl = new Label
        {
            Text = text,
            Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold),
            ForeColor = AppTheme.Accent,
            AutoSize = true,
            Margin = new Padding(0, row == 0 ? 0 : 14, 0, 6),
        };
        p.SetColumnSpan(lbl, 2);
        p.Controls.Add(lbl, 0, row++);
    }

    private void AddInfoLabel(TableLayoutPanel p, string text)
    {
        var lbl = new Label
        {
            Text = text,
            ForeColor = AppTheme.FgSecondary,
            Font = new System.Drawing.Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Italic),
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(750, 0),
            Margin = new Padding(4, 2, 4, 8),
        };
        p.SetColumnSpan(lbl, 2);
        p.Controls.Add(lbl, 0, _row++);
    }

    /// <summary>Adds a result label spanning 2 columns (for calculator output).</summary>
    private Label AddResultLabel(TableLayoutPanel p, string text)
    {
        var lbl = new Label
        {
            Text = text,
            ForeColor = AppTheme.Accent,
            Font = new System.Drawing.Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold),
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(750, 0),
            Margin = new Padding(4, 4, 4, 2),
        };
        p.SetColumnSpan(lbl, 2);
        p.Controls.Add(lbl, 0, _row++);
        return lbl;
    }

    private TextBox AddTextRow(TableLayoutPanel p, string label)
    {
        p.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = AppTheme.FgPrimary,
            Margin = new Padding(0, 0, 8, 4),
        }, 0, _row);
        var tb = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.BgSecondary,
            ForeColor = AppTheme.FgPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 2, 0, 4),
        };
        p.Controls.Add(tb, 1, _row++);
        return tb;
    }

    private NumericUpDown AddNumericRow(TableLayoutPanel p, string label,
        decimal min, decimal max, decimal increment, decimal defaultVal)
    {
        p.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = AppTheme.FgPrimary,
            Margin = new Padding(0, 0, 8, 4),
        }, 0, _row);
        var nud = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = min, Maximum = max,
            Increment = increment, Value = defaultVal,
            DecimalPlaces = increment < 1 ? 2 : 0,
            BackColor = AppTheme.BgSecondary,
            ForeColor = AppTheme.FgPrimary,
            Margin = new Padding(0, 2, 0, 4),
        };
        p.Controls.Add(nud, 1, _row++);
        return nud;
    }

    private ComboBox AddComboRow(TableLayoutPanel p, string label, string[] items)
    {
        p.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = AppTheme.FgPrimary,
            Margin = new Padding(0, 0, 8, 4),
        }, 0, _row);
        var cmb = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = AppTheme.BgSecondary,
            ForeColor = AppTheme.FgPrimary,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 2, 0, 4),
        };
        cmb.Items.AddRange(items);
        if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        p.Controls.Add(cmb, 1, _row++);
        return cmb;
    }
}
