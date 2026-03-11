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
    private readonly NozzleCatalog _catalog;

    // ── Tab 1: Nozzle + Calculator ──
    // Inputs: speed + target rate → outputs: pressure + flow/min + nozzle suggestion
    private ComboBox _cmbNozzle = null!;
    private ComboBox _cmbNozzleType = null!;
    private NumericUpDown _nudSprayAngle = null!;
    private NumericUpDown _nudFlowRate = null!;
    private TextBox _txtNozzleColor = null!;
    private NumericUpDown _nudTargetRate = null!;
    private NumericUpDown _nudCalcSpeed = null!;
    private NumericUpDown _nudCalcSwath = null!;
    private NumericUpDown _nudCalcNozzlesPerBoom = null!;
    private Label _lblCalcPressure = null!;
    private Label _lblCalcFlowPerNozzle = null!;
    private Label _lblCalcActualRate = null!;
    private Label _lblCalcNozzleSuggestion = null!;
    private Label _lblCalcWarnings = null!;

    // ── Tab 2: Booms ──
    private DataGridView _dgvBooms = null!;

    // ── Tab 3: Delays ──
    private NumericUpDown _nudActivationDelay = null!;
    private NumericUpDown _nudDeactivationDelay = null!;
    private NumericUpDown _nudPreActivation = null!;
    private NumericUpDown _nudPreDeactivation = null!;

    // ── Tab 4: Speed + GPS ──
    private NumericUpDown _nudTargetSpeed = null!;
    private NumericUpDown _nudSpeedTolerance = null!;
    private NumericUpDown _nudCogThreshold = null!;
    private NumericUpDown _nudRtkTimeout = null!;
    private NumericUpDown _nudGpsHz = null!;

    // ── Tab 5: Connections ──
    private TextBox _txtTeensyPort = null!;
    private NumericUpDown _nudBaudRate = null!;
    private TextBox _txtWeatherPort = null!;
    private NumericUpDown _nudAogListenPort = null!;
    private NumericUpDown _nudAogSendPort = null!;
    private TextBox _txtAogHost = null!;

    // ── Tab 6: Identity ──
    private TextBox _txtProfileName = null!;
    private TextBox _txtNotes = null!;
    private ComboBox _cmbFluidType = null!;
    private NumericUpDown _nudPressure = null!;
    private NumericUpDown _nudAntennaHeight = null!;

    /// <summary>The configured profile (valid after DialogResult.OK).</summary>
    public MachineProfile Profile => _profile;

    public FormMachineProfile(MachineProfile? existingProfile = null)
    {
        _profile = existingProfile ?? MachineProfile.CreateDefault();
        _catalog = NozzleCatalog.CreateDefault();
        InitializeComponents();
        PopulateFromProfile();
    }

    private void InitializeComponents()
    {
        Text = "⚙ Machine Profile — Профіль Обладнання";
        Size = new System.Drawing.Size(900, 780);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
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

        // Nozzle first — this determines speed and flow rates
        tabControl.TabPages.Add(BuildTabNozzle());
        tabControl.TabPages.Add(BuildTabBooms());
        tabControl.TabPages.Add(BuildTabDelays());
        tabControl.TabPages.Add(BuildTabSpeedGps());
        tabControl.TabPages.Add(BuildTabConnections());
        tabControl.TabPages.Add(BuildTabGeneral());

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
        btnOk.DialogResult = DialogResult.OK;
        var btnCancel = AppTheme.MakeButton("Скасувати", 100);
        btnCancel.DialogResult = DialogResult.Cancel;
        var btnSaveFile = AppTheme.MakeButton("💾 У файл", 110);
        var btnLoadFile = AppTheme.MakeButton("📂 З файлу", 110);

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
    // Tab 1: Nozzle + Calculator
    // User inputs: speed + target rate → outputs: pressure + flow/min + nozzle
    // ════════════════════════════════════════════════════════════════════
    private TabPage BuildTabNozzle()
    {
        var tab = new TabPage("💧 Форсунка");
        var p = MakePanel();

        // ── Section 1: What do you want? ──
        AddSectionHeader(p, "📝 Що потрібно? (задайте параметри)", ref _row);
        AddInfoLabel(p,
            "Задайте бажану швидкість та норму внесення — " +
            "калькулятор підкаже тиск та яку форсунку поставити.");

        _nudCalcSpeed = AddNumericRow(p, "🚜 Швидкість (км/год):", 1, 20, 0.5m, 6.0m);
        _nudTargetRate = AddNumericRow(p, "💧 Норма внесення (л/га):", 10, 1000, 10, 200);
        _nudCalcSwath = AddNumericRow(p, "📏 Ширина захвату (м):", 0.1m, 50, 0.01m, 2.80m);
        _nudCalcNozzlesPerBoom = AddNumericRow(p, "🔢 Форсунок на штангу:", 1, 100, 1, 1);

        // ── Section 2: Type filter ──
        AddSectionHeader(p, "🔍 Тип форсунки", ref _row);
        _cmbNozzleType = AddComboRow(p, "Тип:",
            new[] { "Всі", "Щілинні (Slit)", "Інжекторні (Injector)" });

        // ── Section 3: Current nozzle (manual or from catalog) ──
        AddSectionHeader(p, "📦 Обрана форсунка", ref _row);
        _cmbNozzle = AddComboRow(p, "Форсунка:",
            _catalog.Nozzles.Select(n => n.ToString()).ToArray());

        _nudSprayAngle = AddNumericRow(p, "Кут розпилу (°):", 60, 180, 5, 110);
        _nudSprayAngle.ReadOnly = true;
        _nudSprayAngle.BackColor = AppTheme.BgCard;

        _nudFlowRate = AddNumericRow(p, "Вилив (л/хв) [з каталогу]:", 0.1m, 10, 0.01m, 1.14m);
        _nudFlowRate.ReadOnly = true;
        _nudFlowRate.BackColor = AppTheme.BgCard;

        _txtNozzleColor = AddTextRow(p, "Колір (ISO):");
        _txtNozzleColor.ReadOnly = true;
        _txtNozzleColor.BackColor = AppTheme.BgCard;

        // ── Section 4: Calculator output ──
        AddSectionHeader(p, "📊 Результат розрахунку", ref _row);
        _lblCalcPressure = AddResultLabel(p, "");
        _lblCalcFlowPerNozzle = AddResultLabel(p, "");
        _lblCalcActualRate = AddResultLabel(p, "");
        _lblCalcNozzleSuggestion = AddResultLabel(p, "");
        _lblCalcNozzleSuggestion.MaximumSize = new System.Drawing.Size(800, 0);
        _lblCalcWarnings = AddResultLabel(p, "");
        _lblCalcWarnings.MaximumSize = new System.Drawing.Size(800, 0);

        // Wire up live recalculation on any input change
        _cmbNozzle.SelectedIndexChanged += OnNozzleSelectionChanged;
        _cmbNozzleType.SelectedIndexChanged += OnNozzleTypeFilterChanged;
        _nudCalcSpeed.ValueChanged += (_, _) => RecalcRate();
        _nudTargetRate.ValueChanged += (_, _) => RecalcRate();
        _nudCalcSwath.ValueChanged += (_, _) => RecalcRate();
        _nudCalcNozzlesPerBoom.ValueChanged += (_, _) => RecalcRate();
        _nudFlowRate.ValueChanged += (_, _) => RecalcRate();

        tab.Controls.Add(p);
        return tab;
    }

    /// <summary>Filter nozzle dropdown by type (slit, injector, or all).</summary>
    private void OnNozzleTypeFilterChanged(object? sender, EventArgs e)
    {
        int filterIdx = _cmbNozzleType.SelectedIndex;

        _cmbNozzle.Items.Clear();
        var filtered = filterIdx switch
        {
            1 => _catalog.Nozzles.Where(n => n.Type == NozzleType.Slit),
            2 => _catalog.Nozzles.Where(n => n.Type == NozzleType.Injector),
            _ => _catalog.Nozzles.AsEnumerable(),
        };
        foreach (var n in filtered)
            _cmbNozzle.Items.Add(n.ToString());

        if (_cmbNozzle.Items.Count > 0) _cmbNozzle.SelectedIndex = 0;
    }

    /// <summary>When a nozzle is selected from dropdown, auto-fill spec fields.</summary>
    private void OnNozzleSelectionChanged(object? sender, EventArgs e)
    {
        // Find selected nozzle in the full catalog by matching the display text
        string selectedText = _cmbNozzle.SelectedItem?.ToString() ?? "";
        NozzleDefinition? selected = _catalog.Nozzles
            .FirstOrDefault(n => n.ToString() == selectedText);

        if (selected == null) return;

        _nudSprayAngle.Value = selected.SprayAngleDegrees;
        _nudFlowRate.Value = (decimal)selected.FlowRateLPerMinAtRef;
        _txtNozzleColor.Text = selected.IsoColorCode;

        RecalcRate();
    }

    /// <summary>
    /// Inverse calculator: speed + rate → pressure + flow/min + nozzle suggestion.
    /// This is the main calculation the user asked for.
    /// </summary>
    private void RecalcRate()
    {
        try
        {
            double speed = (double)_nudCalcSpeed.Value;
            double targetRate = (double)_nudTargetRate.Value;
            double swath = (double)_nudCalcSwath.Value;
            int nozzlesPerBoom = (int)_nudCalcNozzlesPerBoom.Value;

            // Build current nozzle from UI fields
            var nozzle = new NozzleDefinition
            {
                Model = _cmbNozzle.Text,
                FlowRateLPerMinAtRef = (double)_nudFlowRate.Value,
                ReferencePressureBar = 2.76,
                SprayAngleDegrees = (int)_nudSprayAngle.Value,
            };

            // ── Core inverse calculation ──
            // Required flow per nozzle: Q = Rate × Speed × Swath / (600 × n)
            double requiredFlowPerNozzle = targetRate * speed * swath
                / (600.0 * nozzlesPerBoom);

            // Required pressure for this flow rate
            double requiredPressure = nozzle.GetPressureForFlowRate(requiredFlowPerNozzle);

            // Actual rate at computed pressure (verification)
            double actualRate = RateCalculator.CalculateRateLPerHa(
                nozzle, requiredPressure, speed, swath, nozzlesPerBoom);

            // ── Display results ──
            _lblCalcPressure.Text = $"🔧 Потрібний тиск: {requiredPressure:F2} бар";
            bool pressureOk = nozzle.IsPressureInRange(requiredPressure);
            _lblCalcPressure.ForeColor = pressureOk ? AppTheme.Success : AppTheme.Error;

            _lblCalcFlowPerNozzle.Text =
                $"💧 Вилив на форсунку: {requiredFlowPerNozzle:F2} л/хв";
            _lblCalcFlowPerNozzle.ForeColor = AppTheme.Accent;

            _lblCalcActualRate.Text = $"📊 Фактична норма: {actualRate:F1} л/га";
            _lblCalcActualRate.ForeColor = AppTheme.Accent;

            // ── Nozzle suggestions from catalog ──
            // Find all nozzles that can work at a comfortable pressure (1-6 bar)
            var suggestions = new List<string>();
            foreach (var candidate in _catalog.Nozzles)
            {
                double candPressure = candidate.GetPressureForFlowRate(requiredFlowPerNozzle);
                if (candidate.IsPressureInRange(candPressure))
                {
                    double candFlow = candidate.GetFlowRateAtPressure(candPressure);
                    string typeTag = candidate.Type == NozzleType.Injector ? "інж" : "щіл";
                    suggestions.Add(
                        $"  ✅ {candidate.Model} [{typeTag}] → " +
                        $"{candPressure:F1} бар, {candFlow:F2} л/хв");
                }
            }

            _lblCalcNozzleSuggestion.Text = suggestions.Count > 0
                ? "🔎 Підходящі форсунки:\n" + string.Join("\n", suggestions.Take(5))
                : "❌ Жодна форсунка з каталогу не підходить!";
            _lblCalcNozzleSuggestion.ForeColor = suggestions.Count > 0
                ? AppTheme.Success : AppTheme.Error;

            // ── Warnings ──
            var msgs = new List<string>();
            if (!pressureOk)
                msgs.Add($"⚠ Тиск {requiredPressure:F1} бар поза діапазоном " +
                    $"({nozzle.MinPressureBar:F1}–{nozzle.MaxPressureBar:F1}) — " +
                    $"оберіть іншу форсунку зі списку ☝");
            if (speed < 2) msgs.Add("⚠ Швидкість < 2 км/год — дуже повільно.");
            if (speed > 10) msgs.Add("⚠ Швидкість > 10 км/год — можливий знос.");
            if (requiredPressure > 6) msgs.Add("💡 Спробуйте форсунку з більшим виливом (наприклад -04, -05).");
            if (requiredPressure < 1) msgs.Add("💡 Спробуйте форсунку з меншим виливом (наприклад -01, -02).");

            _lblCalcWarnings.Text = msgs.Count > 0
                ? string.Join("\n", msgs)
                : "✅ Параметри в нормі";
            _lblCalcWarnings.ForeColor = msgs.Count > 0
                ? AppTheme.Warning : AppTheme.Success;
        }
        catch
        {
            // Silently ignore calculation errors during editing
            _lblCalcPressure.Text = "";
            _lblCalcFlowPerNozzle.Text = "";
            _lblCalcActualRate.Text = "";
            _lblCalcNozzleSuggestion.Text = "";
            _lblCalcWarnings.Text = "";
        }
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
                nextId, $"Boom {nextId + 1}", nextId,
                "0,00", "0,00", "0,25", "70", "30", "-1", "-1", "0,00", true);
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
            new DataGridViewTextBoxColumn { HeaderText = "Шланг(м)", Name = "HoseLen", Width = 55 },
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
    // Tab 4: Speed + GPS
    // ════════════════════════════════════════════════════════════════════
    private TabPage BuildTabSpeedGps()
    {
        var tab = new TabPage("🚜 Швидкість / GPS");
        var p = MakePanel();

        AddSectionHeader(p, "Швидкісний коридор", ref _row);
        _nudTargetSpeed = AddNumericRow(p,
            "Цільова швидкість (км/год):", 1, 20, 0.5m, 5.0m);
        _nudSpeedTolerance = AddNumericRow(p,
            "Допуск ± (км/год):", 0.1m, 5, 0.1m, 1.0m);

        AddSectionHeader(p, "GPS / RTK", ref _row);
        _nudRtkTimeout = AddNumericRow(p,
            "Таймаут втрати RTK (с):", 0, 30, 0.5m, 2.0m);
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

        AddSectionHeader(p, "Метеостанція", ref _row);
        _txtWeatherPort = AddTextRow(p, "COM-порт (пусто = вимк):");

        tab.Controls.Add(p);
        return tab;
    }

    // ════════════════════════════════════════════════════════════════════
    // Tab 6: General identity (moved to last — rarely changed)
    // ════════════════════════════════════════════════════════════════════
    private TabPage BuildTabGeneral()
    {
        var tab = new TabPage("📋 Профіль");
        var p = MakePanel();

        AddSectionHeader(p, "Ідентифікація", ref _row);
        _txtProfileName = AddTextRow(p, "Ім'я профілю:");
        _txtNotes = AddTextRow(p, "Нотатки:");

        AddSectionHeader(p, "Рідина та тиск", ref _row);
        _cmbFluidType = AddComboRow(p, "Тип рідини:",
            Enum.GetNames(typeof(FluidType)));
        _nudPressure = AddNumericRow(p, "Робочий тиск (бар):", 0.5m, 10, 0.5m, 3.0m);

        AddSectionHeader(p, "Геометрія", ref _row);
        _nudAntennaHeight = AddNumericRow(p, "Висота антени (м):", 0.5m, 5, 0.01m, 2.50m);

        tab.Controls.Add(p);
        return tab;
    }

    // ════════════════════════════════════════════════════════════════════
    // Populate / Collect
    // ════════════════════════════════════════════════════════════════════

    private void PopulateFromProfile()
    {
        // Tab 6: Identity
        _txtProfileName.Text = _profile.ProfileName;
        _txtNotes.Text = _profile.Notes;
        _cmbFluidType.SelectedIndex = (int)_profile.FluidType;
        _nudPressure.Value = (decimal)_profile.OperatingPressureBar;
        _nudAntennaHeight.Value = (decimal)_profile.AntennaHeightMeters;

        // Tab 3: Delays
        _nudActivationDelay.Value = (decimal)_profile.SystemActivationDelayMs;
        _nudDeactivationDelay.Value = (decimal)_profile.SystemDeactivationDelayMs;
        _nudPreActivation.Value = (decimal)_profile.PreActivationMeters;
        _nudPreDeactivation.Value = (decimal)_profile.PreDeactivationMeters;

        // Tab 4: Speed
        _nudTargetSpeed.Value = (decimal)_profile.TargetSpeedKmh;
        _nudSpeedTolerance.Value = (decimal)_profile.SpeedToleranceKmh;
        _nudRtkTimeout.Value = (decimal)_profile.RtkLossTimeoutSeconds;
        _nudGpsHz.Value = _profile.GpsUpdateRateHz;
        _nudCogThreshold.Value = (decimal)_profile.CogHeadingThresholdDegrees;

        // Tab 2: Booms (centimeter precision)
        _dgvBooms.Rows.Clear();
        foreach (BoomProfile bp in _profile.Booms)
        {
            _dgvBooms.Rows.Add(
                bp.BoomId,
                bp.Name,
                bp.ValveChannel,
                bp.YOffsetMeters.ToString("F2"),
                bp.XOffsetMeters.ToString("F2"),
                bp.SprayWidthMeters.ToString("F2"),
                bp.ActivationOverlapPercent.ToString("F0"),
                bp.DeactivationOverlapPercent.ToString("F0"),
                bp.ActivationDelayOverrideMs.ToString("F0"),
                bp.DeactivationDelayOverrideMs.ToString("F0"),
                bp.HoseLengthMeters.ToString("F2"),
                bp.Enabled);
        }

        // Tab 5: Connections
        _txtTeensyPort.Text = _profile.Connection.TeensyComPort;
        _nudBaudRate.Value = _profile.Connection.TeensyBaudRate;
        _txtAogHost.Text = _profile.Connection.AogHost;
        _nudAogListenPort.Value = _profile.Connection.AogUdpListenPort;
        _nudAogSendPort.Value = _profile.Connection.AogUdpSendPort;
        _txtWeatherPort.Text = _profile.Connection.WeatherComPort;

        // Tab 1: Nozzle — try to match from catalog by model name
        _nudSprayAngle.Value = _profile.Nozzle.SprayAngleDegrees;
        _nudFlowRate.Value = (decimal)_profile.Nozzle.FlowRateLPerMin;
        _txtNozzleColor.Text = _profile.Nozzle.ColorCode;
        _nudTargetRate.Value = (decimal)_profile.TargetRateLPerHa;
        _nudCalcSpeed.Value = (decimal)_profile.TargetSpeedKmh;

        // Auto-select catalog nozzle if model matches
        int matchIdx = _catalog.Nozzles
            .FindIndex(n => n.Model.Equals(
                _profile.Nozzle.Model, StringComparison.OrdinalIgnoreCase));
        _cmbNozzle.SelectedIndex = matchIdx >= 0 ? matchIdx : 0;

        RecalcRate();
    }

    private void CollectToProfile()
    {
        // Tab 6: Identity
        _profile.ProfileName = _txtProfileName.Text;
        _profile.Notes = _txtNotes.Text;
        _profile.FluidType = (FluidType)_cmbFluidType.SelectedIndex;
        _profile.OperatingPressureBar = (double)_nudPressure.Value;
        _profile.AntennaHeightMeters = (double)_nudAntennaHeight.Value;

        // Tab 3: Delays
        _profile.SystemActivationDelayMs = (double)_nudActivationDelay.Value;
        _profile.SystemDeactivationDelayMs = (double)_nudDeactivationDelay.Value;
        _profile.PreActivationMeters = (double)_nudPreActivation.Value;
        _profile.PreDeactivationMeters = (double)_nudPreDeactivation.Value;

        // Tab 4: Speed
        _profile.TargetSpeedKmh = (double)_nudTargetSpeed.Value;
        _profile.SpeedToleranceKmh = (double)_nudSpeedTolerance.Value;
        _profile.RtkLossTimeoutSeconds = (double)_nudRtkTimeout.Value;
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
            DialogResult = DialogResult.None;
            return;
        }

        // Tab 5: Connections
        _profile.Connection.TeensyComPort = _txtTeensyPort.Text;
        _profile.Connection.TeensyBaudRate = (int)_nudBaudRate.Value;
        _profile.Connection.AogHost = _txtAogHost.Text;
        _profile.Connection.AogUdpListenPort = (int)_nudAogListenPort.Value;
        _profile.Connection.AogUdpSendPort = (int)_nudAogSendPort.Value;
        _profile.Connection.WeatherComPort = _txtWeatherPort.Text;

        // Tab 1: Nozzle
        int nozzleIdx = _cmbNozzle.SelectedIndex;
        _profile.Nozzle.Model = nozzleIdx >= 0 && nozzleIdx < _catalog.Nozzles.Count
            ? _catalog.Nozzles[nozzleIdx].Model
            : _cmbNozzle.Text;
        _profile.Nozzle.SprayAngleDegrees = (int)_nudSprayAngle.Value;
        _profile.Nozzle.FlowRateLPerMin = (double)_nudFlowRate.Value;
        _profile.Nozzle.ColorCode = _txtNozzleColor.Text;
        _profile.TargetRateLPerHa = (double)_nudTargetRate.Value;

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
            DialogResult = DialogResult.None;
        }
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
            HoseLengthMeters = b.HoseLengthMeters,
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
