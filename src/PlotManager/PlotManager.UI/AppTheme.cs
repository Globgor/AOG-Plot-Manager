// Workflow: UI Modernization | Task: AppTheme
using System.Drawing;
using System.Windows.Forms;

namespace PlotManager.UI;

/// <summary>
/// Centralized dark theme constants and styling helpers.
/// All UI controls reference these values to ensure visual consistency.
/// </summary>
public static class AppTheme
{
    // ── Background palette ──
    public static readonly Color BgPrimary = Color.FromArgb(24, 26, 33);
    public static readonly Color BgSecondary = Color.FromArgb(32, 35, 44);
    public static readonly Color BgCard = Color.FromArgb(40, 44, 55);
    public static readonly Color BgInput = Color.FromArgb(50, 54, 68);
    public static readonly Color BgHover = Color.FromArgb(55, 60, 75);

    // ── Text palette ──
    public static readonly Color TextPrimary = Color.FromArgb(230, 233, 240);
    public static readonly Color TextSecondary = Color.FromArgb(160, 168, 185);
    public static readonly Color TextDim = Color.FromArgb(110, 118, 135);
    public static readonly Color TextOnAccent = Color.White;

    // ── Accent colors ──
    public static readonly Color AccentBlue = Color.FromArgb(66, 135, 245);
    public static readonly Color AccentGreen = Color.FromArgb(76, 175, 80);
    public static readonly Color AccentOrange = Color.FromArgb(255, 167, 38);
    public static readonly Color AccentPurple = Color.FromArgb(156, 39, 176);
    public static readonly Color AccentRed = Color.FromArgb(244, 67, 54);

    // ── Borders ──
    public static readonly Color Border = Color.FromArgb(60, 65, 80);
    public static readonly Color BorderActive = AccentBlue;

    // ── Step indicator ──
    public static readonly Color StepDone = AccentGreen;
    public static readonly Color StepActive = AccentBlue;
    public static readonly Color StepPending = Color.FromArgb(70, 75, 90);

    // ── Fonts ──
    public static readonly Font FontTitle = new("Segoe UI", 18, FontStyle.Bold);
    public static readonly Font FontHeading = new("Segoe UI", 13, FontStyle.Bold);
    public static readonly Font FontSubheading = new("Segoe UI", 11, FontStyle.Bold);
    public static readonly Font FontBody = new("Segoe UI", 10);
    public static readonly Font FontSmall = new("Segoe UI", 9);
    public static readonly Font FontMono = new("Consolas", 9.5f);

    // ── Sizing ──
    public const int CardRadius = 8;
    public const int CardPadding = 16;
    public const int FieldSpacing = 10;
    public const int SectionSpacing = 20;

    // ════════════════════════════════════════════════════════════════════
    // Styling helpers — apply theme to standard WinForms controls
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Apply dark theme base to a Form.</summary>
    public static void StyleForm(Form form)
    {
        form.BackColor = BgPrimary;
        form.ForeColor = TextPrimary;
        form.Font = FontBody;
    }

    /// <summary>Style a panel as a dark card with subtle border.</summary>
    public static void StyleCard(Panel panel)
    {
        panel.BackColor = BgCard;
        panel.ForeColor = TextPrimary;
        panel.Padding = new Padding(CardPadding);
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Border, 1);
            var rect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
            e.Graphics.DrawRectangle(pen, rect);
        };
    }

    /// <summary>Style a flat accent button.</summary>
    public static void StyleButton(Button btn, Color accent)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 0;
        btn.BackColor = accent;
        btn.ForeColor = TextOnAccent;
        btn.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        btn.Cursor = Cursors.Hand;
        btn.Height = 40;

        // Hover effect
        btn.MouseEnter += (_, _) =>
            btn.BackColor = ControlPaint.Light(accent, 0.15f);
        btn.MouseLeave += (_, _) =>
            btn.BackColor = accent;
    }

    /// <summary>Style a secondary (outline) button.</summary>
    public static void StyleButtonOutline(Button btn)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderColor = Border;
        btn.FlatAppearance.BorderSize = 1;
        btn.BackColor = BgSecondary;
        btn.ForeColor = TextPrimary;
        btn.Font = FontBody;
        btn.Cursor = Cursors.Hand;
        btn.Height = 36;

        btn.MouseEnter += (_, _) => btn.BackColor = BgHover;
        btn.MouseLeave += (_, _) => btn.BackColor = BgSecondary;
    }

    /// <summary>Style a TextBox for dark theme.</summary>
    public static void StyleTextBox(TextBox tb)
    {
        tb.BackColor = BgInput;
        tb.ForeColor = TextPrimary;
        tb.BorderStyle = BorderStyle.FixedSingle;
        tb.Font = FontBody;
    }

    /// <summary>Style a NumericUpDown for dark theme.</summary>
    public static void StyleNumeric(NumericUpDown nud)
    {
        nud.BackColor = BgInput;
        nud.ForeColor = TextPrimary;
        nud.Font = FontBody;
        nud.BorderStyle = BorderStyle.FixedSingle;
    }

    /// <summary>Style a ComboBox for dark theme.</summary>
    public static void StyleComboBox(ComboBox cmb)
    {
        cmb.BackColor = BgInput;
        cmb.ForeColor = TextPrimary;
        cmb.FlatStyle = FlatStyle.Flat;
        cmb.Font = FontBody;
    }

    /// <summary>Style a DataGridView for dark theme.</summary>
    public static void StyleDataGrid(DataGridView dgv)
    {
        dgv.BackgroundColor = BgSecondary;
        dgv.GridColor = Border;
        dgv.BorderStyle = BorderStyle.None;
        dgv.RowHeadersVisible = false;
        dgv.EnableHeadersVisualStyles = false;
        dgv.AllowUserToAddRows = false;
        dgv.AllowUserToDeleteRows = false;
        dgv.AllowUserToResizeRows = false;
        dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        dgv.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = BgCard,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(6, 4, 6, 4),
            SelectionBackColor = BgCard,
            SelectionForeColor = TextSecondary,
        };

        dgv.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = BgSecondary,
            ForeColor = TextPrimary,
            Font = FontBody,
            Padding = new Padding(6, 4, 6, 4),
            SelectionBackColor = BgHover,
            SelectionForeColor = TextPrimary,
        };

        dgv.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(36, 39, 50),
            ForeColor = TextPrimary,
            SelectionBackColor = BgHover,
            SelectionForeColor = TextPrimary,
        };
    }

    /// <summary>Create a styled section header label.</summary>
    public static Label CreateSectionHeader(string text)
    {
        return new Label
        {
            Text = text,
            Font = FontSubheading,
            ForeColor = AccentBlue,
            AutoSize = true,
            Margin = new Padding(0, SectionSpacing, 0, 6),
        };
    }

    /// <summary>Create a styled description/help label.</summary>
    public static Label CreateHelpText(string text)
    {
        return new Label
        {
            Text = text,
            Font = FontSmall,
            ForeColor = TextDim,
            AutoSize = true,
            MaximumSize = new Size(700, 0),
            Margin = new Padding(0, 2, 0, 8),
        };
    }
}
