using System.Windows.Forms;

namespace PlotManager.UI.Forms;

using PlotManager.Core.Models;

/// <summary>
/// Machine Profile editor — 5-tab dialog for all calibration parameters.
/// Modal: returns DialogResult.OK with populated Profile.
/// </summary>
public class FormMachineProfile : Form
{
    private readonly MachineProfile _profile;

    // ── Tab 1: Identity + Fluid ──
    private TextBox _txtProfileName = null!;
    private TextBox _txtNotes = null!;
    private ComboBox _cmbFluidType = null!;
    private NumericUpDown _nudPressure = null!;
    private NumericUpDown _nudAntennaHeight = null!;

    // ── Tab 2: Delays + Distances ──
    private NumericUpDown _nudActivationDelay = null!;
    private NumericUpDown _nudDeactivationDelay = null!;
    private NumericUpDown _nudPreActivation = null!;
    private NumericUpDown _nudPreDeactivation = null!;

    // ── Tab 3: Speed + GPS ──
    private NumericUpDown _nudTargetSpeed = null!;
    private NumericUpDown _nudSpeedTolerance = null!;
    private NumericUpDown _nudCogThreshold = null!;
    private NumericUpDown _nudRtkTimeout = null!;
    private NumericUpDown _nudGpsHz = null!;

    // ── Tab 4: Booms ──
    private DataGridView _dgvBooms = null!;

    // ── Tab 5: Connections + Nozzle ──
    private TextBox _txtTeensyPort = null!;
    private NumericUpDown _nudBaudRate = null!;
    private TextBox _txtWeatherPort = null!;
    private NumericUpDown _nudAogListenPort = null!;
    private NumericUpDown _nudAogSendPort = null!;
    private TextBox _txtAogHost = null!;
    private TextBox _txtNozzleModel = null!;
    private NumericUpDown _nudSprayAngle = null!;
    private NumericUpDown _nudFlowRate = null!;
    private TextBox _txtNozzleColor = null!;
    private NumericUpDown _nudTargetRate = null!;

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
        Text = "⚙ Machine Profile — Профиль Оборудования";
        Size = new System.Drawing.Size(820, 680);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.5f);

        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new System.Drawing.Point(12, 6),
        };

        tabControl.TabPages.Add(BuildTabGeneral());
        tabControl.TabPages.Add(BuildTabDelays());
        tabControl.TabPages.Add(BuildTabSpeedGps());
        tabControl.TabPages.Add(BuildTabBooms());
        tabControl.TabPages.Add(BuildTabConnections());

        // ── Bottom buttons ──
        var panelButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 50,
            Padding = new Padding(8),
        };

        var btnOk = new Button
        {
            Text = "✅ Сохранить", Width = 120, Height = 32,
            DialogResult = DialogResult.OK,
        };
        var btnCancel = new Button
        {
            Text = "Отмена", Width = 100, Height = 32,
            DialogResult = DialogResult.Cancel,
        };
        var btnSaveFile = new Button { Text = "💾 В файл", Width = 110, Height = 32 };
        var btnLoadFile = new Button { Text = "📂 Из файла", Width = 110, Height = 32 };

        btnOk.Click += (_, _) => CollectToProfile();
        btnSaveFile.Click += OnSaveToFile;
        btnLoadFile.Click += OnLoadFromFile;

        panelButtons.Controls.AddRange(new Control[] { btnOk, btnCancel, btnSaveFile, btnLoadFile });

        Controls.Add(tabControl);
        Controls.Add(panelButtons);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    // ════════════════════════════════════════════════════════════════════
    // Tab 1: General — Профиль, жидкость, геометрия
    // ════════════════════════════════════════════════════════════════════
    private TabPage BuildTabGeneral()
    {
        var tab = new TabPage("📋 Профиль");
        var p = MakePanel();

        AddSectionHeader(p, "Идентификация", ref _row);
        _txtProfileName = AddTextRow(p, "Имя профиля:");
        _txtNotes = AddTextRow(p, "Заметки:");

        AddSectionHeader(p, "Жидкость и давление", ref _row);
        _cmbFluidType = AddComboRow(p, "Тип жидкости:",
            Enum.GetNames(typeof(FluidType)));
        _nudPressure = AddNumericRow(p, "Рабочее давление (бар):", 0.5m, 10, 0.5m, 3.0m);

        AddSectionHeader(p, "Геометрия", ref _row);
        _nudAntennaHeight = AddNumericRow(p, "Высота антенны (м):", 0.5m, 5, 0.1m, 2.5m);

        tab.Controls.Add(p);
        return tab;
    }

    // ════════════════════════════════════════════════════════════════════
    // Tab 2: Delays — Задержки и отступы
    // ════════════════════════════════════════════════════════════════════
    private TabPage BuildTabDelays()
    {
        var tab = new TabPage("⏱ Задержки");
        var p = MakePanel();

        AddSectionHeader(p, "Гидравлические задержки (глобальные)", ref _row);
        _nudActivationDelay = AddNumericRow(p,
            "Задержка активации (мс):", 0, 2000, 10, 300);
        _nudDeactivationDelay = AddNumericRow(p,
            "Задержка деактивации (мс):", 0, 2000, 10, 150);

        AddInfoLabel(p,
            "⚡ Если длины шлангов отличаются — задайте per-boom " +
            "переопределения на вкладке «Штанги» (колонки Акт.мс / Деакт.мс, " +
            "-1 = использовать глобальное).");

        AddSectionHeader(p, "Пространственные отступы", ref _row);
        _nudPreActivation = AddNumericRow(p,
            "Преактивация (м):", 0, 5, 0.05m, 0.5m);
        _nudPreDeactivation = AddNumericRow(p,
            "Предеактивация (м):", 0, 5, 0.05m, 0.2m);

        tab.Controls.Add(p);
        return tab;
    }

    // ════════════════════════════════════════════════════════════════════
    // Tab 3: Speed + GPS
    // ════════════════════════════════════════════════════════════════════
    private TabPage BuildTabSpeedGps()
    {
        var tab = new TabPage("🚜 Скорость / GPS");
        var p = MakePanel();

        AddSectionHeader(p, "Скоростной коридор", ref _row);
        _nudTargetSpeed = AddNumericRow(p,
            "Целевая скорость (км/ч):", 1, 20, 0.5m, 5.0m);
        _nudSpeedTolerance = AddNumericRow(p,
            "Допуск ± (км/ч):", 0.1m, 5, 0.1m, 1.0m);

        AddSectionHeader(p, "GPS / RTK", ref _row);
        _nudRtkTimeout = AddNumericRow(p,
            "Таймаут потери RTK (с):", 0, 30, 0.5m, 2.0m);
        _nudGpsHz = AddNumericRow(p,
            "Частота GPS (Гц):", 1, 20, 1, 10);

        AddSectionHeader(p, "Крабовый ход (COG)", ref _row);
        _nudCogThreshold = AddNumericRow(p,
            "Порог Heading/COG (°):", 0.5m, 15, 0.5m, 3.0m);
        AddInfoLabel(p,
            "Если разница Heading − COG > порога → задние штанги " +
            "проецируются по COG (компенсация бокового сноса на склоне).");

        tab.Controls.Add(p);
        return tab;
    }

    // ════════════════════════════════════════════════════════════════════
    // Tab 4: Booms
    // ════════════════════════════════════════════════════════════════════
    private TabPage BuildTabBooms()
    {
        var tab = new TabPage("🔧 Штанги");
        _dgvBooms = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            RowTemplate = { Height = 28 },
            ColumnHeadersHeight = 32,
            DefaultCellStyle = { Padding = new Padding(4, 2, 4, 2) },
        };

        _dgvBooms.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { HeaderText = "#", Name = "BoomId", ReadOnly = true, Width = 30 },
            new DataGridViewTextBoxColumn { HeaderText = "Имя", Name = "Name", MinimumWidth = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "Канал", Name = "ValveChannel", Width = 45 },
            new DataGridViewTextBoxColumn { HeaderText = "Y (м)", Name = "YOffset", Width = 50 },
            new DataGridViewTextBoxColumn { HeaderText = "X (м)", Name = "XOffset", Width = 50 },
            new DataGridViewTextBoxColumn { HeaderText = "Шир.", Name = "SprayWidth", Width = 45 },
            new DataGridViewTextBoxColumn { HeaderText = "Акт.%", Name = "ActOverlap", Width = 45 },
            new DataGridViewTextBoxColumn { HeaderText = "Деакт.%", Name = "DeactOverlap", Width = 50 },
            new DataGridViewTextBoxColumn { HeaderText = "Акт.мс", Name = "ActDelay", Width = 50,
                ToolTipText = "-1 = глобальное значение" },
            new DataGridViewTextBoxColumn { HeaderText = "Деакт.мс", Name = "DeactDelay", Width = 55,
                ToolTipText = "-1 = глобальное значение" },
            new DataGridViewTextBoxColumn { HeaderText = "Шланг(м)", Name = "HoseLen", Width = 55 },
            new DataGridViewCheckBoxColumn { HeaderText = "Вкл", Name = "Enabled", Width = 35 },
        });

        tab.Controls.Add(_dgvBooms);
        return tab;
    }

    // ════════════════════════════════════════════════════════════════════
    // Tab 5: Connections + Nozzle
    // ════════════════════════════════════════════════════════════════════
    private TabPage BuildTabConnections()
    {
        var tab = new TabPage("🔌 Связь / Форсунка");
        var p = MakePanel();

        AddSectionHeader(p, "Teensy", ref _row);
        _txtTeensyPort = AddTextRow(p, "COM-порт:");
        _nudBaudRate = AddNumericRow(p, "Baud rate:", 9600, 1000000, 100, 115200);

        AddSectionHeader(p, "AgOpenGPS (UDP)", ref _row);
        _txtAogHost = AddTextRow(p, "AOG Host:");
        _nudAogListenPort = AddNumericRow(p, "Порт приёма:", 1024, 65535, 1, 9999);
        _nudAogSendPort = AddNumericRow(p, "Порт отправки:", 1024, 65535, 1, 9998);

        AddSectionHeader(p, "Метеостанция", ref _row);
        _txtWeatherPort = AddTextRow(p, "COM-порт (пусто = выкл):");

        AddSectionHeader(p, "Форсунка (для отчётов)", ref _row);
        _txtNozzleModel = AddTextRow(p, "Модель:");
        _nudSprayAngle = AddNumericRow(p, "Угол распыла (°):", 60, 180, 5, 110);
        _nudFlowRate = AddNumericRow(p, "Расход (л/мин):", 0.1m, 10, 0.1m, 1.2m);
        _txtNozzleColor = AddTextRow(p, "Цвет (ISO):");
        _nudTargetRate = AddNumericRow(p, "Норма (л/га):", 10, 1000, 10, 200);

        tab.Controls.Add(p);
        return tab;
    }

    // ════════════════════════════════════════════════════════════════════
    // Populate / Collect
    // ════════════════════════════════════════════════════════════════════

    private void PopulateFromProfile()
    {
        // Tab 1
        _txtProfileName.Text = _profile.ProfileName;
        _txtNotes.Text = _profile.Notes;
        _cmbFluidType.SelectedIndex = (int)_profile.FluidType;
        _nudPressure.Value = (decimal)_profile.OperatingPressureBar;
        _nudAntennaHeight.Value = (decimal)_profile.AntennaHeightMeters;

        // Tab 2
        _nudActivationDelay.Value = (decimal)_profile.SystemActivationDelayMs;
        _nudDeactivationDelay.Value = (decimal)_profile.SystemDeactivationDelayMs;
        _nudPreActivation.Value = (decimal)_profile.PreActivationMeters;
        _nudPreDeactivation.Value = (decimal)_profile.PreDeactivationMeters;

        // Tab 3
        _nudTargetSpeed.Value = (decimal)_profile.TargetSpeedKmh;
        _nudSpeedTolerance.Value = (decimal)_profile.SpeedToleranceKmh;
        _nudRtkTimeout.Value = (decimal)_profile.RtkLossTimeoutSeconds;
        _nudGpsHz.Value = _profile.GpsUpdateRateHz;
        _nudCogThreshold.Value = (decimal)_profile.CogHeadingThresholdDegrees;

        // Tab 4 — booms grid
        _dgvBooms.Rows.Clear();
        foreach (BoomProfile bp in _profile.Booms)
        {
            _dgvBooms.Rows.Add(
                bp.BoomId,
                bp.Name,
                bp.ValveChannel,
                bp.YOffsetMeters.ToString("F3"),
                bp.XOffsetMeters.ToString("F3"),
                bp.SprayWidthMeters.ToString("F2"),
                bp.ActivationOverlapPercent.ToString("F0"),
                bp.DeactivationOverlapPercent.ToString("F0"),
                bp.ActivationDelayOverrideMs.ToString("F0"),
                bp.DeactivationDelayOverrideMs.ToString("F0"),
                bp.HoseLengthMeters.ToString("F2"),
                bp.Enabled);
        }

        // Tab 5
        _txtTeensyPort.Text = _profile.Connection.TeensyComPort;
        _nudBaudRate.Value = _profile.Connection.TeensyBaudRate;
        _txtAogHost.Text = _profile.Connection.AogHost;
        _nudAogListenPort.Value = _profile.Connection.AogUdpListenPort;
        _nudAogSendPort.Value = _profile.Connection.AogUdpSendPort;
        _txtWeatherPort.Text = _profile.Connection.WeatherComPort;
        _txtNozzleModel.Text = _profile.Nozzle.Model;
        _nudSprayAngle.Value = _profile.Nozzle.SprayAngleDegrees;
        _nudFlowRate.Value = (decimal)_profile.Nozzle.FlowRateLPerMin;
        _txtNozzleColor.Text = _profile.Nozzle.ColorCode;
        _nudTargetRate.Value = (decimal)_profile.TargetRateLPerHa;
    }

    private void CollectToProfile()
    {
        // Tab 1
        _profile.ProfileName = _txtProfileName.Text;
        _profile.Notes = _txtNotes.Text;
        _profile.FluidType = (FluidType)_cmbFluidType.SelectedIndex;
        _profile.OperatingPressureBar = (double)_nudPressure.Value;
        _profile.AntennaHeightMeters = (double)_nudAntennaHeight.Value;

        // Tab 2
        _profile.SystemActivationDelayMs = (double)_nudActivationDelay.Value;
        _profile.SystemDeactivationDelayMs = (double)_nudDeactivationDelay.Value;
        _profile.PreActivationMeters = (double)_nudPreActivation.Value;
        _profile.PreDeactivationMeters = (double)_nudPreDeactivation.Value;

        // Tab 3
        _profile.TargetSpeedKmh = (double)_nudTargetSpeed.Value;
        _profile.SpeedToleranceKmh = (double)_nudSpeedTolerance.Value;
        _profile.RtkLossTimeoutSeconds = (double)_nudRtkTimeout.Value;
        _profile.GpsUpdateRateHz = (int)_nudGpsHz.Value;
        _profile.CogHeadingThresholdDegrees = (double)_nudCogThreshold.Value;

        // Tab 4 — booms
        _profile.Booms.Clear();
        foreach (DataGridViewRow r in _dgvBooms.Rows)
        {
            _profile.Booms.Add(new BoomProfile
            {
                BoomId = Convert.ToInt32(r.Cells["BoomId"].Value),
                Name = r.Cells["Name"].Value?.ToString() ?? "",
                ValveChannel = Convert.ToInt32(r.Cells["ValveChannel"].Value),
                YOffsetMeters = Convert.ToDouble(r.Cells["YOffset"].Value),
                XOffsetMeters = Convert.ToDouble(r.Cells["XOffset"].Value),
                SprayWidthMeters = Convert.ToDouble(r.Cells["SprayWidth"].Value),
                ActivationOverlapPercent = Convert.ToDouble(r.Cells["ActOverlap"].Value),
                DeactivationOverlapPercent = Convert.ToDouble(r.Cells["DeactOverlap"].Value),
                ActivationDelayOverrideMs = Convert.ToDouble(r.Cells["ActDelay"].Value),
                DeactivationDelayOverrideMs = Convert.ToDouble(r.Cells["DeactDelay"].Value),
                HoseLengthMeters = Convert.ToDouble(r.Cells["HoseLen"].Value),
                Enabled = Convert.ToBoolean(r.Cells["Enabled"].Value),
            });
        }

        // Tab 5
        _profile.Connection.TeensyComPort = _txtTeensyPort.Text;
        _profile.Connection.TeensyBaudRate = (int)_nudBaudRate.Value;
        _profile.Connection.AogHost = _txtAogHost.Text;
        _profile.Connection.AogUdpListenPort = (int)_nudAogListenPort.Value;
        _profile.Connection.AogUdpSendPort = (int)_nudAogSendPort.Value;
        _profile.Connection.WeatherComPort = _txtWeatherPort.Text;
        _profile.Nozzle.Model = _txtNozzleModel.Text;
        _profile.Nozzle.SprayAngleDegrees = (int)_nudSprayAngle.Value;
        _profile.Nozzle.FlowRateLPerMin = (double)_nudFlowRate.Value;
        _profile.Nozzle.ColorCode = _txtNozzleColor.Text;
        _profile.TargetRateLPerHa = (double)_nudTargetRate.Value;

        _profile.LastModifiedUtc = DateTime.UtcNow;
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
            CollectToProfile();
            _profile.SaveToFile(dlg.FileName);
            MessageBox.Show($"Профиль сохранён:\n{dlg.FileName}", "✅ Сохранено",
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
                MessageBox.Show($"Ошибка загрузки:\n{ex.Message}", "❌ Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private static void CopyProfile(MachineProfile src, MachineProfile dst)
    {
        dst.ProfileName = src.ProfileName;
        dst.Notes = src.Notes;
        dst.FluidType = src.FluidType;
        dst.OperatingPressureBar = src.OperatingPressureBar;
        dst.AntennaHeightMeters = src.AntennaHeightMeters;
        dst.TrackWidthMeters = src.TrackWidthMeters;
        dst.FluidTemperatureCelsius = src.FluidTemperatureCelsius;
        dst.SystemActivationDelayMs = src.SystemActivationDelayMs;
        dst.SystemDeactivationDelayMs = src.SystemDeactivationDelayMs;
        dst.PreActivationMeters = src.PreActivationMeters;
        dst.PreDeactivationMeters = src.PreDeactivationMeters;
        dst.TargetSpeedKmh = src.TargetSpeedKmh;
        dst.SpeedToleranceKmh = src.SpeedToleranceKmh;
        dst.CogHeadingThresholdDegrees = src.CogHeadingThresholdDegrees;
        dst.RtkLossTimeoutSeconds = src.RtkLossTimeoutSeconds;
        dst.GpsUpdateRateHz = src.GpsUpdateRateHz;
        dst.Connection = src.Connection;
        dst.Nozzle = src.Nozzle;
        dst.TargetRateLPerHa = src.TargetRateLPerHa;
        dst.Booms = src.Booms;
    }

    // ════════════════════════════════════════════════════════════════════
    // Layout helpers — proper spacing
    // ════════════════════════════════════════════════════════════════════

    private int _row;

    private TableLayoutPanel MakePanel()
    {
        _row = 0;
        return new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 45),
                new ColumnStyle(SizeType.Percent, 55),
            },
            AutoScroll = true,
            Padding = new Padding(16, 12, 16, 12),
        };
    }

    private static void AddSectionHeader(TableLayoutPanel p, string text, ref int row)
    {
        var lbl = new Label
        {
            Text = text,
            Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold),
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
            ForeColor = System.Drawing.Color.FromArgb(100, 100, 100),
            Font = new System.Drawing.Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Italic),
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(700, 0),
            Margin = new Padding(4, 2, 4, 8),
        };
        p.SetColumnSpan(lbl, 2);
        p.Controls.Add(lbl, 0, _row++);
    }

    private TextBox AddTextRow(TableLayoutPanel p, string label)
    {
        p.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 8, 4),
        }, 0, _row);
        var tb = new TextBox
        {
            Dock = DockStyle.Fill,
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
            Margin = new Padding(0, 0, 8, 4),
        }, 0, _row);
        var nud = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = min, Maximum = max,
            Increment = increment, Value = defaultVal,
            DecimalPlaces = increment < 1 ? 2 : 0,
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
            Margin = new Padding(0, 0, 8, 4),
        }, 0, _row);
        var cmb = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 2, 0, 4),
        };
        cmb.Items.AddRange(items);
        if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        p.Controls.Add(cmb, 1, _row++);
        return cmb;
    }
}
