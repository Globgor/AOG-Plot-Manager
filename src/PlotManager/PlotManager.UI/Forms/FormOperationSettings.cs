using System.Drawing;
using System.Windows.Forms;

using PlotManager.Core.Services;

namespace PlotManager.UI.Forms;

/// <summary>
/// Modal settings dialog for operational parameters.
/// Groups: Speed, Hydraulics, Prime/Clean, AutoWeather, Trial logging.
/// Changes apply immediately to the live service instances.
/// </summary>
public class FormOperationSettings : Form
{
    private readonly SectionController _sectionController;
    private readonly PrimeController? _primeController;
    private readonly CleanController? _cleanController;
    private readonly AutoWeatherService? _autoWeather;
    private readonly TrialLogger? _trialLogger;

    public FormOperationSettings(
        SectionController sectionController,
        PrimeController? primeController = null,
        CleanController? cleanController = null,
        AutoWeatherService? autoWeather = null,
        TrialLogger? trialLogger = null)
    {
        _sectionController = sectionController ?? throw new ArgumentNullException(nameof(sectionController));
        _primeController = primeController;
        _cleanController = cleanController;
        _autoWeather = autoWeather;
        _trialLogger = trialLogger;

        InitializeComponent();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "⚙ Настройки опрыскивания";
        Size = new Size(480, 560);
        MinimumSize = new Size(440, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9);
        BackColor = Color.FromArgb(30, 30, 35);
        ForeColor = Color.FromArgb(220, 220, 220);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(35, 35, 40),
            ForeColor = Color.FromArgb(220, 220, 220),
        };

        // ── Tab 1: Speed ──
        tabs.TabPages.Add(CreateSpeedTab());

        // ── Tab 2: Prime / Clean ──
        tabs.TabPages.Add(CreatePrimeCleanTab());

        // ── Tab 3: AutoWeather ──
        tabs.TabPages.Add(CreateWeatherTab());

        // ── Tab 4: Trial ──
        tabs.TabPages.Add(CreateTrialTab());

        Controls.Add(tabs);

        // Apply button
        var btnApply = new Button
        {
            Text = "✅ Применить",
            Dock = DockStyle.Bottom,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(76, 175, 80),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
        };
        btnApply.FlatAppearance.BorderSize = 0;
        btnApply.Click += (_, _) => { ApplySettings(); DialogResult = DialogResult.OK; };
        Controls.Add(btnApply);

        ResumeLayout();
    }

    // ── Fields for controls ──
    private NumericUpDown _nudTargetSpeed = null!;
    private NumericUpDown _nudSpeedTolerance = null!;
    private NumericUpDown _nudMaxPrimeSpeed = null!;
    private NumericUpDown _nudPulseOnMs = null!;
    private NumericUpDown _nudPulseOffMs = null!;
    private NumericUpDown _nudCycleCount = null!;
    private NumericUpDown _nudWeatherThreshold = null!;
    private NumericUpDown _nudStoppedSpeedKmh = null!;
    private NumericUpDown _nudLogIntervalMs = null!;
    private CheckBox _chkLogAllStates = null!;

    private TabPage CreateSpeedTab()
    {
        var page = new TabPage("🏎 Скорость") { BackColor = Color.FromArgb(35, 35, 40), Padding = new Padding(16) };
        int y = 16;

        AddLabel(page, "Целевая скорость (км/ч):", 16, y);
        _nudTargetSpeed = AddNumeric(page, 260, y, 0.5m, 20, 0.5m, (decimal)_sectionController.TargetSpeedKmh);
        y += 36;

        AddLabel(page, "Допуск скорости (%):", 16, y);
        _nudSpeedTolerance = AddNumeric(page, 260, y, 1, 50, 1, (decimal)(_sectionController.SpeedToleranceFraction * 100));
        y += 36;

        AddLabel(page, "Мин: {0:F1}   Макс: {1:F1} км/ч", 16, y);
        return page;
    }

    private TabPage CreatePrimeCleanTab()
    {
        var page = new TabPage("🚿 Prime / Clean") { BackColor = Color.FromArgb(35, 35, 40), Padding = new Padding(16) };
        int y = 16;

        AddLabel(page, "Макс скорость Prime (км/ч):", 16, y);
        _nudMaxPrimeSpeed = AddNumeric(page, 260, y, 0, 5, 0.1m, (decimal)(_primeController?.MaxPrimeSpeedKmh ?? 0.5));
        y += 36;

        var sep = new Label { Text = "─── Pulse Clean ───", Location = new Point(16, y), Size = new Size(400, 20),
            ForeColor = Color.FromArgb(100, 100, 100), Font = new Font("Segoe UI", 8, FontStyle.Italic) };
        page.Controls.Add(sep);
        y += 24;

        AddLabel(page, "Pulse ON (мс):", 16, y);
        _nudPulseOnMs = AddNumeric(page, 260, y, 500, 10000, 100, _cleanController?.PulseOnMs ?? 2000);
        y += 36;

        AddLabel(page, "Pulse OFF (мс):", 16, y);
        _nudPulseOffMs = AddNumeric(page, 260, y, 200, 5000, 100, _cleanController?.PulseOffMs ?? 1000);
        y += 36;

        AddLabel(page, "Кол-во циклов:", 16, y);
        _nudCycleCount = AddNumeric(page, 260, y, 1, 20, 1, _cleanController?.CycleCount ?? 3);
        return page;
    }

    private TabPage CreateWeatherTab()
    {
        var page = new TabPage("🌤 Автометео") { BackColor = Color.FromArgb(35, 35, 40), Padding = new Padding(16) };
        int y = 16;

        AddLabel(page, "Порог стоянки (сек):", 16, y);
        _nudWeatherThreshold = AddNumeric(page, 260, y, 1, 120, 1, (_autoWeather?.StationaryThresholdMs ?? 10000) / 1000);
        y += 36;

        AddLabel(page, "Мин. скорость «стоп» (км/ч):", 16, y);
        _nudStoppedSpeedKmh = AddNumeric(page, 260, y, 0, 2, 0.1m, (decimal)(_autoWeather?.StoppedSpeedKmh ?? 0.1));
        return page;
    }

    private TabPage CreateTrialTab()
    {
        var page = new TabPage("📝 Trial") { BackColor = Color.FromArgb(35, 35, 40), Padding = new Padding(16) };
        int y = 16;

        AddLabel(page, "Интервал записи (мс):", 16, y);
        _nudLogIntervalMs = AddNumeric(page, 260, y, 100, 5000, 100, _trialLogger?.LogIntervalMs ?? 1000);
        y += 36;

        _chkLogAllStates = new CheckBox
        {
            Text = "Логировать все состояния (Alley, OutsideGrid)",
            Location = new Point(16, y),
            Size = new Size(400, 24),
            Checked = _trialLogger?.LogAllStates ?? false,
            ForeColor = Color.FromArgb(220, 220, 220),
        };
        page.Controls.Add(_chkLogAllStates);
        return page;
    }

    private void ApplySettings()
    {
        // Speed
        _sectionController.TargetSpeedKmh = (double)_nudTargetSpeed.Value;
        _sectionController.SpeedToleranceFraction = (double)_nudSpeedTolerance.Value / 100.0;

        // Prime
        if (_primeController != null)
            _primeController.MaxPrimeSpeedKmh = (double)_nudMaxPrimeSpeed.Value;

        // Clean
        if (_cleanController != null)
        {
            _cleanController.PulseOnMs = (int)_nudPulseOnMs.Value;
            _cleanController.PulseOffMs = (int)_nudPulseOffMs.Value;
            _cleanController.CycleCount = (int)_nudCycleCount.Value;
        }

        // AutoWeather
        if (_autoWeather != null)
        {
            _autoWeather.StationaryThresholdMs = (int)_nudWeatherThreshold.Value * 1000;
            _autoWeather.StoppedSpeedKmh = (double)_nudStoppedSpeedKmh.Value;
        }

        // Trial
        if (_trialLogger != null)
        {
            _trialLogger.LogIntervalMs = (int)_nudLogIntervalMs.Value;
            _trialLogger.LogAllStates = _chkLogAllStates.Checked;
        }
    }

    // ── Helpers ──

    private static void AddLabel(TabPage page, string text, int x, int y)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(x, y + 4),
            Size = new Size(230, 20),
            ForeColor = Color.FromArgb(200, 200, 200),
        };
        page.Controls.Add(lbl);
    }

    private static NumericUpDown AddNumeric(TabPage page, int x, int y,
        decimal min, decimal max, decimal increment, decimal value)
    {
        var nud = new NumericUpDown
        {
            Location = new Point(x, y),
            Size = new Size(120, 26),
            Minimum = min,
            Maximum = max,
            Increment = increment,
            Value = Math.Clamp(value, min, max),
            DecimalPlaces = increment < 1 ? 1 : 0,
            BackColor = Color.FromArgb(50, 50, 55),
            ForeColor = Color.FromArgb(220, 220, 220),
        };
        page.Controls.Add(nud);
        return nud;
    }
}
