using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;

namespace LangKinhHuKhong.TTS.UI;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(247, 242, 236);
    public static readonly Color Sidebar = Color.FromArgb(87, 12, 24);
    public static readonly Color SidebarAccent = Color.FromArgb(122, 20, 35);
    public static readonly Color Surface = Color.FromArgb(255, 252, 248);
    public static readonly Color Inset = Color.FromArgb(251, 247, 241);
    public static readonly Color Border = Color.FromArgb(228, 211, 191);
    public static readonly Color Text = Color.FromArgb(71, 33, 27);
    public static readonly Color Muted = Color.FromArgb(136, 104, 94);
    // Retain the old property name for compatibility: Primary accent is now royal red.
    public static readonly Color Blue = Color.FromArgb(185, 42, 56);
    public static readonly Color Gold = Color.FromArgb(221, 173, 72);
    public static readonly Color Green = Color.FromArgb(61, 132, 92);
    public static readonly Color Danger = Color.FromArgb(198, 65, 68);
    public static readonly Font Body = new("Segoe UI", 10f, FontStyle.Regular);
    public static readonly Font Small = new("Segoe UI", 9f, FontStyle.Regular);
    public static readonly Font Title = new("Segoe UI Semibold", 20f, FontStyle.Bold);
    public static readonly Font Heading = new("Segoe UI Semibold", 12f, FontStyle.Bold);

    public static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(Math.Min(rect.Width, rect.Height), radius * 2);
        if (d <= 0) { path.AddRectangle(rect); return path; }
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Label Label(string text, float size = 10, bool bold = false, Color? color = null)
        => new() { Text = text, ForeColor = color ?? Text,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent,
            AutoEllipsis = true, UseMnemonic = false, Dock = DockStyle.Fill };

    public static TextBox Textbox(string? text = null) => new()
    {
        Text = text ?? "", BackColor = Color.White, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle,
        Font = Body, Margin = new Padding(2, 6, 2, 6)
    };

    public static ComboBox Combo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
        BackColor = Color.White, ForeColor = Text, Font = Body, IntegralHeight = false,
        Height = 34, Margin = new Padding(0, 6, 0, 6)
    };

    public static TableLayoutPanel Table(int cols, int rows) => new()
    {
        Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty,
        Padding = Padding.Empty, ColumnCount = cols, RowCount = rows
    };
}

internal sealed class SurfacePanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 18;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowBorder { get; set; } = true;
    public SurfacePanel()
    {
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        Padding = new Padding(17);
        Margin = Padding.Empty;
        Resize += (_, _) => Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(1, 1, Width - 3, Height - 3);
        if (rect.Width <= 1 || rect.Height <= 1) return;
        using var shadowPath = Theme.Rounded(new Rectangle(rect.X, rect.Y + 2, rect.Width, rect.Height), Radius);
        using var shadowBrush = new SolidBrush(Color.FromArgb(16, 76, 31, 18));
        e.Graphics.FillPath(shadowBrush, shadowPath);
        using var path = Theme.Rounded(rect, Radius);
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillPath(brush, path);
        if (ShowBorder)
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawPath(pen, path);
        }
    }
}

internal sealed class ModernButton : Button
{
    public enum Kind { Primary, Secondary, Ghost, Destructive, Gold }
    private bool _over;
    private Kind _style = Kind.Secondary;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Kind Style { get => _style; set { _style = value; Invalidate(); } }

    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        Height = 40;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 0, 8, 0);
        DoubleBuffered = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _over = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _over = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(1, 1, Width - 3, Height - 3);
        if (rect.Width <= 2 || rect.Height <= 2) return;
        var fill = Style switch
        {
            Kind.Primary => Theme.Blue,
            Kind.Gold => Theme.Gold,
            Kind.Destructive => Color.FromArgb(132, 25, 38),
            Kind.Ghost => Theme.SidebarAccent,
            _ => Color.White
        };
        if (_over && Enabled) fill = ControlPaint.Light(fill, .10f);
        if (!Enabled) fill = Theme.Border;
        using var path = Theme.Rounded(rect, 10);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        using var line = new Pen(Style switch
        {
            Kind.Primary => Color.FromArgb(140, 28, 42),
            Kind.Gold => Color.FromArgb(179, 136, 45),
            Kind.Destructive => Color.FromArgb(110, 18, 29),
            Kind.Ghost => Color.FromArgb(158, 49, 65),
            _ => Theme.Border
        });
        e.Graphics.DrawPath(line, path);
        var foreground = !Enabled ? Theme.Muted : Style switch
        {
            Kind.Primary or Kind.Ghost or Kind.Destructive => Color.White,
            Kind.Gold => Color.FromArgb(86, 34, 16),
            _ => Theme.Text
        };
        var alignment = TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left :
            TextAlign == ContentAlignment.MiddleRight ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter;
        var textRect = TextAlign == ContentAlignment.MiddleLeft ? new Rectangle(rect.X + 17, rect.Y, rect.Width - 20, rect.Height) : rect;
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, foreground,
            alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class ModernProgressBar : Control
{
    private int _percent;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Percent { get => _percent; set { _percent = Math.Clamp(value, 0, 100); Invalidate(); } }
    public ModernProgressBar() { Height = 10; BackColor = Theme.Inset; DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var bg = Theme.Rounded(new Rectangle(0, 0, Width, Height - 1), 5);
        using var brush = new SolidBrush(Color.FromArgb(240, 231, 217));
        e.Graphics.FillPath(brush, bg);
        var w = (int)Math.Round(Width * Percent / 100d);
        if (w > 0)
        {
            using var fg = Theme.Rounded(new Rectangle(0, 0, Math.Max(2,w), Height - 1), 5);
            using var gold = new SolidBrush(Theme.Gold);
            e.Graphics.FillPath(gold, fg);
        }
    }
}
