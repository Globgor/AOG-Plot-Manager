// Workflow: UI Modernization | Task: WelcomePanel
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PlotManager.UI.Controls;

/// <summary>
/// Welcome/splash screen — Step 0 of the wizard.
/// Shows app name, version, and two action buttons.
/// </summary>
public sealed class WelcomePanel : UserControl
{
    /// <summary>Fires when user wants to create a new setup.</summary>
    public event EventHandler? NewSetupRequested;

    /// <summary>Fires when user wants to load an existing profile.</summary>
    public event EventHandler? LoadProfileRequested;

    public WelcomePanel()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.BgPrimary;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        int cx = Width / 2;
        int cy = Height / 2 - 80;

        // ── Decorative gradient circle ──
        int gradientSize = 200;
        var gradientRect = new Rectangle(
            cx - gradientSize / 2, cy - 110,
            gradientSize, gradientSize);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(gradientRect);
            using var brush = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(40, AppTheme.AccentBlue),
                SurroundColors = new[] { Color.FromArgb(0, AppTheme.AccentBlue) },
            };
            g.FillEllipse(brush, gradientRect);
        }

        // ── Tractor emoji icon ──
        using var iconFont = new Font("Segoe UI Emoji", 48);
        var iconText = "🚜";
        var iconSize = g.MeasureString(iconText, iconFont);
        using (var brush = new SolidBrush(Color.White))
            g.DrawString(iconText, iconFont, brush,
                cx - iconSize.Width / 2, cy - 90);

        // ── Title ──
        const string title = "AOG Plot Manager";
        using var titleFont = new Font("Segoe UI", 28, FontStyle.Bold);
        var titleSize = g.MeasureString(title, titleFont);
        using (var brush = new SolidBrush(AppTheme.TextPrimary))
            g.DrawString(title, titleFont, brush,
                cx - titleSize.Width / 2, cy + 10);

        // ── Version ──
        const string version = "v0.2.0 — Wizard Edition";
        using var versionFont = new Font("Segoe UI", 11);
        var versionSize = g.MeasureString(version, versionFont);
        using (var brush = new SolidBrush(AppTheme.TextDim))
            g.DrawString(version, versionFont, brush,
                cx - versionSize.Width / 2, cy + 52);

        // ── Subtitle ──
        const string subtitle = "Автоматизація польових дослідів з AgOpenGPS";
        using var subFont = new Font("Segoe UI", 10);
        var subSize = g.MeasureString(subtitle, subFont);
        using (var brush = new SolidBrush(AppTheme.TextSecondary))
            g.DrawString(subtitle, subFont, brush,
                cx - subSize.Width / 2, cy + 78);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutButtons();
        Invalidate();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        CreateButtons();
    }

    private Button? _btnNew;
    private Button? _btnLoad;

    private void CreateButtons()
    {
        if (_btnNew != null) return; // Already created

        int cx = Width / 2;
        int buttonY = Height / 2 + 60;

        _btnNew = new Button
        {
            Text = "🆕  Нова настройка",
            Size = new Size(220, 48),
        };
        AppTheme.StyleButton(_btnNew, AppTheme.AccentBlue);
        _btnNew.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        _btnNew.Click += (_, _) => NewSetupRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(_btnNew);

        _btnLoad = new Button
        {
            Text = "📂  Завантажити профіль",
            Size = new Size(220, 48),
        };
        AppTheme.StyleButtonOutline(_btnLoad);
        _btnLoad.Font = new Font("Segoe UI", 11);
        _btnLoad.Height = 48;
        _btnLoad.Click += (_, _) => LoadProfileRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(_btnLoad);

        LayoutButtons();
    }

    private void LayoutButtons()
    {
        if (_btnNew == null || _btnLoad == null) return;

        int cx = Width / 2;
        int buttonY = Height / 2 + 60;

        _btnNew.Location = new Point(cx - _btnNew.Width / 2, buttonY);
        _btnLoad.Location = new Point(cx - _btnLoad.Width / 2, buttonY + 60);
    }
}
