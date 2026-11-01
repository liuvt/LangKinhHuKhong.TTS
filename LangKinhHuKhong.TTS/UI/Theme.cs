using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LangKinhHuKhong.TTS.UI;

internal static class Theme
{
    // Modern Studio Palette (Slate & Indigo)
    public static readonly Color Background = Color.FromArgb(241, 245, 249);    // Slate 100 workspace canvas
    public static readonly Color Sidebar = Color.FromArgb(15, 23, 42);          // Slate 900 sidebar
    public static readonly Color SidebarAccent = Color.FromArgb(30, 41, 59);    // Slate 800 card/hover on sidebar
    public static readonly Color Surface = Color.FromArgb(255, 255, 255);       // Pure White card surface
    public static readonly Color Inset = Color.FromArgb(248, 250, 252);         // Slate 50 input/editor fill
    public static readonly Color Border = Color.FromArgb(203, 213, 225);        // Slate 300 crisp hairline border
    public static readonly Color Text = Color.FromArgb(15, 23, 42);             // Slate 900 primary text
    public static readonly Color Muted = Color.FromArgb(100, 116, 139);         // Slate 500 secondary text

    // Studio Accents
    public static readonly Color Blue = Color.FromArgb(79, 70, 229);            // Indigo 600 primary CTA
    public static readonly Color Gold = Color.FromArgb(13, 148, 136);           // Teal 600 preview/highlight accent
    public static readonly Color Green = Color.FromArgb(5, 150, 105);           // Emerald 600 ready/success
    public static readonly Color Danger = Color.FromArgb(225, 29, 72);          // Rose 600 destructive

    public static readonly Font Body = new("Segoe UI", 9.5f, FontStyle.Regular);
    public static readonly Font Small = new("Segoe UI", 8.75f, FontStyle.Regular);
    public static readonly Font Title = new("Segoe UI Semibold", 16f, FontStyle.Bold);
    public static readonly Font Heading = new("Segoe UI Semibold", 10.5f, FontStyle.Bold);

    public static Color ResolveParentBackColor(Control control)
    {
        var current = control.Parent;
        while (current is not null)
        {
            if (current.BackColor != Color.Transparent && current.BackColor.A == 255)
                return current.BackColor;
            current = current.Parent;
        }
        return Background;
    }

    public static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Min(Math.Min(rect.Width, rect.Height), radius * 2);
        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Label Label(string text, float size = 9.5f, bool bold = false, Color? color = null)
        => new()
        {
            Text = text,
            ForeColor = color ?? Text,
            Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill
        };

    public static TextBox Textbox(string? text = null) => new()
    {
        Text = text ?? "",
        BackColor = Color.White,
        ForeColor = Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Body,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Margin = new Padding(0, 3, 0, 3)
    };

    public static ComboBox Combo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Standard,
        BackColor = Color.White,
        ForeColor = Text,
        Font = Body,
        IntegralHeight = false,
        ItemHeight = 22,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Margin = new Padding(0, 3, 0, 3)
    };

    public static TableLayoutPanel Table(int cols, int rows)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = cols,
            RowCount = rows
        };
        if (cols == 1) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        if (rows == 1) table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        return table;
    }
}

internal sealed class SurfacePanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 10;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowBorder { get; set; } = true;

    public SurfacePanel()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Padding = new Padding(16);
        Margin = Padding.Empty;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Fill interior with BackColor so transparent child labels/tables inherit the exact card color,
        // while clipping outer corners to the parent's background color.
        var outerColor = Theme.ResolveParentBackColor(this);
        e.Graphics.Clear(outerColor);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width <= 1 || rect.Height <= 1) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(rect, Radius);
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!ShowBorder) return;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width <= 1 || rect.Height <= 1) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(rect, Radius);
        using var pen = new Pen(Theme.Border, 1f);
        e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class ModernButton : Button
{
    public enum Kind { Primary, Secondary, Ghost, Destructive, Gold }

    private bool _over;
    private bool _down;
    private Kind _style = Kind.Secondary;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Kind Style
    {
        get => _style;
        set { _style = value; Invalidate(); }
    }

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.White;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
        Height = 34;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 2, 6, 2);
        UseVisualStyleBackColor = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _over = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _over = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { if (mevent.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _down = false; Invalidate(); base.OnMouseUp(mevent); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // All background painting is performed atomically inside OnPaint to prevent flicker.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var parentBg = Theme.ResolveParentBackColor(this);
        e.Graphics.Clear(parentBg);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(1, 1, Width - 3, Height - 3);
        if (rect.Width <= 2 || rect.Height <= 2) return;

        bool onDarkSurface = parentBg.GetBrightness() < 0.35f;
        Color fill;
        Color border;
        Color foreground;

        if (!Enabled)
        {
            fill = onDarkSurface ? Color.FromArgb(30, 41, 59) : Color.FromArgb(241, 245, 249);
            border = onDarkSurface ? Color.FromArgb(51, 65, 85) : Color.FromArgb(226, 232, 240);
            foreground = Color.FromArgb(148, 163, 184);
        }
        else
        {
            switch (Style)
            {
                case Kind.Primary:
                    fill = _down ? Color.FromArgb(55, 48, 163)
                         : _over ? Color.FromArgb(67, 56, 202)
                         : Theme.Blue;
                    border = _over ? Color.FromArgb(55, 48, 163) : Color.FromArgb(67, 56, 202);
                    foreground = Color.White;
                    break;

                case Kind.Gold:
                    fill = _down ? Color.FromArgb(17, 94, 89)
                         : _over ? Color.FromArgb(15, 118, 110)
                         : Theme.Gold;
                    border = _over ? Color.FromArgb(17, 94, 89) : Color.FromArgb(15, 118, 110);
                    foreground = Color.White;
                    break;

                case Kind.Destructive:
                    fill = _down ? Color.FromArgb(159, 18, 57)
                         : _over ? Color.FromArgb(190, 18, 60)
                         : Theme.Danger;
                    border = _over ? Color.FromArgb(159, 18, 57) : Color.FromArgb(190, 18, 60);
                    foreground = Color.White;
                    break;

                case Kind.Ghost:
                    if (onDarkSurface)
                    {
                        fill = _down ? Color.FromArgb(51, 65, 85)
                             : _over ? Color.FromArgb(30, 41, 59)
                             : parentBg;
                        border = _over ? Color.FromArgb(51, 65, 85) : parentBg;
                        foreground = _over ? Color.White : Color.FromArgb(203, 213, 225);
                    }
                    else
                    {
                        fill = _down ? Color.FromArgb(226, 232, 240)
                             : _over ? Color.FromArgb(241, 245, 249)
                             : Color.White;
                        border = _over ? Color.FromArgb(148, 163, 184) : Theme.Border;
                        foreground = Theme.Text;
                    }
                    break;

                default: // Kind.Secondary
                    fill = _down ? Color.FromArgb(226, 232, 240)
                         : _over ? Color.FromArgb(241, 245, 249)
                         : Color.White;
                    border = _over ? Color.FromArgb(148, 163, 184) : Theme.Border;
                    foreground = Theme.Text;
                    break;
            }
        }

        using var path = Theme.Rounded(rect, 7);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        using var pen = new Pen(border, 1f);
        e.Graphics.DrawPath(pen, path);

        var alignment = TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left :
            TextAlign == ContentAlignment.MiddleRight ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter;
        var textRect = TextAlign == ContentAlignment.MiddleLeft
            ? new Rectangle(rect.X + 12, rect.Y, rect.Width - 16, rect.Height)
            : new Rectangle(rect.X + 4, rect.Y, rect.Width - 8, rect.Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, foreground,
            alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class ModernProgressBar : Control
{
    private int _percent;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Percent
    {
        get => _percent;
        set { _percent = Math.Clamp(value, 0, 100); Invalidate(); }
    }

    public ModernProgressBar()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        Height = 8;
        BackColor = Theme.Surface;
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var parentBg = Theme.ResolveParentBackColor(this);
        e.Graphics.Clear(parentBg);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var trackRect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (trackRect.Width <= 2 || trackRect.Height <= 2) return;
        using var bg = Theme.Rounded(trackRect, 4);
        using var brush = new SolidBrush(Color.FromArgb(226, 232, 240));
        e.Graphics.FillPath(brush, bg);
        int w = (int)Math.Round((Width - 1) * Percent / 100d);
        if (w > 0)
        {
            using var fg = Theme.Rounded(new Rectangle(0, 0, Math.Max(4, w), Height - 1), 4);
            using var accent = new SolidBrush(Theme.Blue);
            e.Graphics.FillPath(accent, fg);
        }
    }
}
