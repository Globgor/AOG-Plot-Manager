// Workflow: UI Modernization | Task: WizardNavBar
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PlotManager.UI.Controls;

/// <summary>
/// Horizontal step-indicator bar with Back/Next navigation.
/// Renders step circles connected by lines, with active/done/pending states.
/// </summary>
public sealed class WizardNavBar : UserControl
{
    /// <summary>Step labels displayed in the nav bar.</summary>
    public string[] Steps { get; set; } = Array.Empty<string>();

    private int _currentStep;
    private int _maxReachedStep;

    /// <summary>Current active step index (0-based).</summary>
    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            _currentStep = Math.Clamp(value, 0, Steps.Length - 1);
            if (_currentStep > _maxReachedStep)
                _maxReachedStep = _currentStep;
            Invalidate();
            StepChanged?.Invoke(this, _currentStep);
        }
    }

    /// <summary>Fires when the active step changes.</summary>
    public event EventHandler<int>? StepChanged;

    /// <summary>Fires when user requests to go back.</summary>
    public event EventHandler? BackRequested;

    /// <summary>Fires when user requests to go forward.</summary>
    public event EventHandler? NextRequested;

    private readonly Button _btnBack;
    private readonly Button _btnNext;

    public WizardNavBar()
    {
        Height = 70;
        Dock = DockStyle.Top;
        BackColor = AppTheme.BgSecondary;
        DoubleBuffered = true;

        _btnBack = new Button
        {
            Text = "← Назад",
            Size = new Size(100, 34),
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
        };
        AppTheme.StyleButtonOutline(_btnBack);
        _btnBack.Click += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);

        _btnNext = new Button
        {
            Text = "Далее →",
            Size = new Size(120, 34),
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };
        AppTheme.StyleButton(_btnNext, AppTheme.AccentBlue);

        _btnNext.Click += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty);

        Controls.Add(_btnBack);
        Controls.Add(_btnNext);

        Resize += (_, _) => PositionButtons();
        PositionButtons();
    }

    /// <summary>Set the text on the Next button (e.g. "Далее →" or "🚀 Запуск").</summary>
    public void SetNextText(string text) => _btnNext.Text = text;

    /// <summary>Set the accent color of the Next button.</summary>
    public void SetNextColor(Color color) => AppTheme.StyleButton(_btnNext, color);

    /// <summary>Enable or disable the Next button (for validation gating).</summary>
    public void SetNextEnabled(bool enabled) => _btnNext.Enabled = enabled;

    /// <summary>Show or hide the Back button.</summary>
    public void SetBackVisible(bool visible) => _btnBack.Visible = visible;

    private void PositionButtons()
    {
        _btnBack.Location = new Point(16, (Height - _btnBack.Height) / 2);
        _btnNext.Location = new Point(
            Width - _btnNext.Width - 16,
            (Height - _btnNext.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Steps.Length == 0) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        int stepCount = Steps.Length;
        int leftMargin = 140;
        int rightMargin = 160;
        int usableWidth = Width - leftMargin - rightMargin;
        int stepSpacing = stepCount > 1 ? usableWidth / (stepCount - 1) : 0;
        int cy = Height / 2 - 4;
        int circleSize = 28;

        // Draw connecting lines first
        for (int i = 0; i < stepCount - 1; i++)
        {
            int x1 = leftMargin + i * stepSpacing + circleSize / 2;
            int x2 = leftMargin + (i + 1) * stepSpacing - circleSize / 2;
            var lineColor = i < _currentStep ? AppTheme.StepDone : AppTheme.StepPending;
            using var pen = new Pen(lineColor, 2);
            g.DrawLine(pen, x1, cy + circleSize / 2, x2, cy + circleSize / 2);
        }

        // Draw step circles + labels
        using var fontLabel = new Font("Segoe UI", 8f);
        using var fontNum = new Font("Segoe UI", 9f, FontStyle.Bold);

        for (int i = 0; i < stepCount; i++)
        {
            int cx = leftMargin + i * stepSpacing;
            var rect = new Rectangle(cx, cy, circleSize, circleSize);

            Color bg, fg;
            if (i < _currentStep)
            {
                bg = AppTheme.StepDone;
                fg = Color.White;
            }
            else if (i == _currentStep)
            {
                bg = AppTheme.StepActive;
                fg = Color.White;
            }
            else
            {
                bg = AppTheme.StepPending;
                fg = AppTheme.TextDim;
            }

            // Circle
            using (var brush = new SolidBrush(bg))
                g.FillEllipse(brush, rect);

            // Number or checkmark
            string symbol = i < _currentStep ? "✓" : (i + 1).ToString();
            var textSize = g.MeasureString(symbol, fontNum);
            using (var brush = new SolidBrush(fg))
                g.DrawString(symbol, fontNum, brush,
                    cx + (circleSize - textSize.Width) / 2,
                    cy + (circleSize - textSize.Height) / 2);

            // Label below
            var labelSize = g.MeasureString(Steps[i], fontLabel);
            var labelColor = i == _currentStep ? AppTheme.TextPrimary : AppTheme.TextDim;
            using (var brush = new SolidBrush(labelColor))
                g.DrawString(Steps[i], fontLabel, brush,
                    cx + circleSize / 2 - labelSize.Width / 2,
                    cy + circleSize + 4);
        }

        // Bottom border line
        using var borderPen = new Pen(AppTheme.Border, 1);
        g.DrawLine(borderPen, 0, Height - 1, Width, Height - 1);
    }
}
