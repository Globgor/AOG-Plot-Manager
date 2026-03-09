using PlotManager.Core.Models;

namespace PlotManager.UI.Forms;

/// <summary>
/// Modal dialog for capturing pre-trial weather conditions.
/// Required before enabling Plot Mode to ensure scientific data quality.
/// </summary>
public class FormWeatherSnapshot : Form
{
    private NumericUpDown _nudTemperature = null!;
    private NumericUpDown _nudHumidity = null!;
    private NumericUpDown _nudWindSpeed = null!;
    private ComboBox _cboWindDirection = null!;
    private TextBox _txtNotes = null!;
    private Label _lblWarnings = null!;
    private Button _btnStart = null!;
    private Button _btnCancel = null!;

    /// <summary>The captured weather snapshot (null if cancelled).</summary>
    public WeatherSnapshot? Result { get; private set; }

    public FormWeatherSnapshot()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "Pre-Trial Weather Check";
        Size = new Size(420, 420);
        MinimumSize = new Size(400, 380);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9);

        // ── Header ──
        var header = new Label
        {
            Text = "🌤  Record weather conditions before starting the trial.",
            Location = new Point(16, 12),
            Size = new Size(380, 20),
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = Color.FromArgb(80, 80, 80)
        };
        Controls.Add(header);

        int y = 42;

        // ── Temperature ──
        AddLabel("Temperature (°C):", 16, y);
        _nudTemperature = new NumericUpDown
        {
            Location = new Point(180, y - 2),
            Size = new Size(100, 26),
            Minimum = -20, Maximum = 50,
            DecimalPlaces = 1, Value = 20.0m,
            Increment = 0.5m,
        };
        Controls.Add(_nudTemperature);
        y += 34;

        // ── Humidity ──
        AddLabel("Humidity (%):", 16, y);
        _nudHumidity = new NumericUpDown
        {
            Location = new Point(180, y - 2),
            Size = new Size(100, 26),
            Minimum = 0, Maximum = 100,
            DecimalPlaces = 0, Value = 60m,
            Increment = 5m,
        };
        Controls.Add(_nudHumidity);
        y += 34;

        // ── Wind Speed ──
        AddLabel("Wind Speed (m/s):", 16, y);
        _nudWindSpeed = new NumericUpDown
        {
            Location = new Point(180, y - 2),
            Size = new Size(100, 26),
            Minimum = 0, Maximum = 30,
            DecimalPlaces = 1, Value = 1.0m,
            Increment = 0.5m,
        };
        Controls.Add(_nudWindSpeed);
        y += 34;

        // ── Wind Direction ──
        AddLabel("Wind Direction:", 16, y);
        _cboWindDirection = new ComboBox
        {
            Location = new Point(180, y - 2),
            Size = new Size(100, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _cboWindDirection.Items.AddRange(new object[]
        {
            "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
            "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW", "Calm"
        });
        _cboWindDirection.SelectedIndex = 0;
        Controls.Add(_cboWindDirection);
        y += 34;

        // ── Notes ──
        AddLabel("Notes (optional):", 16, y);
        y += 22;
        _txtNotes = new TextBox
        {
            Location = new Point(16, y),
            Size = new Size(370, 50),
            Multiline = true,
            PlaceholderText = "e.g. cloudy, dew present, light rain stopped 30 min ago..."
        };
        Controls.Add(_txtNotes);
        y += 60;

        // ── Warnings ──
        _lblWarnings = new Label
        {
            Location = new Point(16, y),
            Size = new Size(370, 40),
            ForeColor = Color.FromArgb(200, 80, 0),
            Font = new Font("Segoe UI", 8.5f),
            Text = string.Empty
        };
        Controls.Add(_lblWarnings);
        y += 44;

        // ── Buttons ──
        _btnStart = new Button
        {
            Text = "✅  Start Trial",
            Location = new Point(160, y),
            Size = new Size(120, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(76, 175, 80),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            DialogResult = DialogResult.OK,
        };
        _btnStart.FlatAppearance.BorderSize = 0;
        _btnStart.Click += BtnStart_Click;
        Controls.Add(_btnStart);

        _btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(290, y),
            Size = new Size(90, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(200, 200, 200),
            Font = new Font("Segoe UI", 9),
            DialogResult = DialogResult.Cancel,
        };
        _btnCancel.FlatAppearance.BorderSize = 0;
        Controls.Add(_btnCancel);

        // Wire validation on value change
        _nudTemperature.ValueChanged += (_, _) => ValidateAndWarn();
        _nudHumidity.ValueChanged += (_, _) => ValidateAndWarn();
        _nudWindSpeed.ValueChanged += (_, _) => ValidateAndWarn();

        AcceptButton = _btnStart;
        CancelButton = _btnCancel;

        ResumeLayout(true);
    }

    private void BtnStart_Click(object? sender, EventArgs e)
    {
        Result = new WeatherSnapshot
        {
            TemperatureC = (double)_nudTemperature.Value,
            HumidityPercent = (double)_nudHumidity.Value,
            WindSpeedMs = (double)_nudWindSpeed.Value,
            WindDirection = _cboWindDirection.SelectedItem?.ToString() ?? "N/A",
            Notes = _txtNotes.Text.Trim(),
            Timestamp = DateTime.Now,
        };
    }

    private void ValidateAndWarn()
    {
        var snapshot = new WeatherSnapshot
        {
            TemperatureC = (double)_nudTemperature.Value,
            HumidityPercent = (double)_nudHumidity.Value,
            WindSpeedMs = (double)_nudWindSpeed.Value,
        };

        List<string> warnings = snapshot.Validate();
        _lblWarnings.Text = warnings.Count > 0
            ? "⚠️ " + string.Join(" | ", warnings)
            : string.Empty;
    }

    private void AddLabel(string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(x, y + 2),
            Size = new Size(160, 20),
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(50, 50, 50)
        };
        Controls.Add(label);
    }
}
