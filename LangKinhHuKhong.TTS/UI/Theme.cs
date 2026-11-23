using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LangKinhHuKhong.TTS.UI;

internal static class Theme
{
    // Obsidian & Deep Crimson Studio Palette ("Đỏ đậm đen")
    public static readonly Color Background = Color.FromArgb(11, 9, 11);         // #0B090B - Deep Obsidian Black canvas
    public static readonly Color Sidebar = Color.FromArgb(7, 5, 7);              // #070507 - Pitch Onyx sidebar
    public static readonly Color SidebarAccent = Color.FromArgb(24, 12, 16);     // #180C10 - Dark Wine-Charcoal sidebar card
    public static readonly Color Surface = Color.FromArgb(19, 14, 17);           // #130E11 - Obsidian-Crimson card surface
    public static readonly Color Inset = Color.FromArgb(27, 18, 22);             // #1B1216 - Elevated Dark Crimson inset
    public static readonly Color InputBg = Color.FromArgb(23, 16, 20);           // #171014 - Editor / Input / List fill
    public static readonly Color InputAltBg = Color.FromArgb(29, 20, 25);        // #1D1419 - Alternating list row fill
    public static readonly Color HeaderBg = Color.FromArgb(38, 15, 22);          // #260F16 - Dark Crimson table header
    public static readonly Color SelectionBg = Color.FromArgb(112, 20, 34);      // #701422 - Deep Crimson active selection
    public static readonly Color Border = Color.FromArgb(74, 28, 38);            // #4A1C26 - Crisp Deep Wine hairline border
    public static readonly Color Text = Color.FromArgb(246, 238, 240);           // #F6EEF0 - Crisp Warm Off-White primary text
    public static readonly Color Muted = Color.FromArgb(178, 152, 160);          // #B298A0 - Muted Rose-Slate secondary text

    // Studio Accents (Crimson & Dark Ruby)
    public static readonly Color Blue = Color.FromArgb(168, 22, 35);             // #A81623 - Primary Deep Crimson CTA
    public static readonly Color AccentText = Color.FromArgb(248, 105, 118);     // #F86976 - High-contrast Crimson badge text
    public static readonly Color Gold = Color.FromArgb(118, 22, 42);             // #76162A - Dark Burgundy / Wine secondary CTA
    public static readonly Color GoldText = Color.FromArgb(251, 176, 64);        // #FBB040 - Warm Amber status warning text
    public static readonly Color Green = Color.FromArgb(52, 211, 153);           // #34D399 - Emerald ready/success text
    public static readonly Color Danger = Color.FromArgb(215, 28, 62);           // #D71C3E - Vivid Crimson-Rose destructive
    public static readonly Color DangerText = Color.FromArgb(251, 113, 133);     // #FB7185 - Readable Rose error text

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
        BackColor = InputBg,
        ForeColor = Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Body,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Margin = new Padding(0, 3, 0, 3)
    };

    public static ComboBox Combo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = InputBg,
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

    public static ToolStripProfessionalRenderer CreateMenuRenderer()
        => new(new CrimsonMenuColorTable()) { RoundedEdges = true };

    private sealed class CrimsonMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Color.FromArgb(168, 22, 35);
        public override Color MenuItemSelected => SelectionBg;
        public override Color MenuItemSelectedGradientBegin => SelectionBg;
        public override Color MenuItemSelectedGradientEnd => SelectionBg;
        public override Color ImageMarginGradientBegin => Sidebar;
        public override Color ImageMarginGradientMiddle => Sidebar;
        public override Color ImageMarginGradientEnd => Sidebar;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Color.Transparent;
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
        BackColor = Theme.Inset;
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

        Color fill;
        Color border;
        Color foreground;

        if (!Enabled)
        {
            fill = Color.FromArgb(22, 16, 19);
            border = Color.FromArgb(44, 26, 32);
            foreground = Color.FromArgb(104, 86, 92);
        }
        else
        {
            switch (Style)
            {
                case Kind.Primary:
                    fill = _down ? Color.FromArgb(132, 16, 28)
                         : _over ? Color.FromArgb(198, 28, 44)
                         : Theme.Blue;
                    border = _over ? Color.FromArgb(238, 68, 84) : Color.FromArgb(204, 40, 56);
                    foreground = Color.White;
                    break;

                case Kind.Gold:
                    fill = _down ? Color.FromArgb(90, 16, 32)
                         : _over ? Color.FromArgb(146, 28, 52)
                         : Theme.Gold;
                    border = _over ? Color.FromArgb(196, 52, 80) : Color.FromArgb(156, 36, 60);
                    foreground = Color.FromArgb(255, 238, 242);
                    break;

                case Kind.Destructive:
                    fill = _down ? Color.FromArgb(128, 14, 38)
                         : _over ? Color.FromArgb(215, 28, 62)
                         : Color.FromArgb(155, 20, 46);
                    border = _over ? Color.FromArgb(244, 63, 94) : Color.FromArgb(196, 34, 66);
                    foreground = Color.White;
                    break;

                case Kind.Ghost:
                    fill = _down ? Color.FromArgb(58, 20, 28)
                         : _over ? Color.FromArgb(36, 16, 22)
                         : parentBg;
                    border = _over ? Color.FromArgb(98, 34, 46) : parentBg;
                    foreground = _over ? Color.White : Color.FromArgb(208, 188, 196);
                    break;

                default: // Kind.Secondary
                    fill = _down ? Color.FromArgb(58, 24, 32)
                         : _over ? Color.FromArgb(42, 22, 28)
                         : Color.FromArgb(29, 19, 24);
                    border = _over ? Color.FromArgb(148, 46, 62) : Theme.Border;
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
        using var brush = new SolidBrush(Color.FromArgb(42, 22, 28));
        e.Graphics.FillPath(brush, bg);
        int w = (int)Math.Round((Width - 1) * Percent / 100d);
        if (w > 0)
        {
            using var fg = Theme.Rounded(new Rectangle(0, 0, Math.Max(4, w), Height - 1), 4);
            using var accent = new SolidBrush(Color.FromArgb(208, 32, 48));
            e.Graphics.FillPath(accent, fg);
        }
    }
}

internal sealed class DarkListView : ListView
{
    private static readonly Font HeaderFont = new("Segoe UI Semibold", 8.75f, FontStyle.Bold);

    public DarkListView(bool checkBoxes)
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        View = View.Details;
        FullRowSelect = true;
        HideSelection = false;
        CheckBoxes = checkBoxes;
        MultiSelect = true;
        OwnerDraw = true;
        BackColor = Theme.InputBg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.25f);
        BorderStyle = BorderStyle.FixedSingle;
        GridLines = false;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;

        Resize += (_, _) => FitLastColumn();
    }

    private void FitLastColumn()
    {
        if (Columns.Count == 0 || ClientSize.Width <= 100) return;
        int fixedWidth = 0;
        for (int i = 0; i < Columns.Count - 1; i++)
            fixedWidth += Columns[i].Width;
        int remaining = ClientSize.Width - fixedWidth - 6;
        if (remaining > 120)
            Columns[^1].Width = remaining;
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using var bg = new SolidBrush(Theme.HeaderBg);
        e.Graphics.FillRectangle(bg, e.Bounds);
        using var borderPen = new Pen(Theme.Border, 1f);
        e.Graphics.DrawLine(borderPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        e.Graphics.DrawLine(borderPen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);

        var textRect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", HeaderFont, textRect,
            Color.FromArgb(244, 212, 220),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        // Row cells are rendered cleanly per-column inside OnDrawSubItem.
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        if (e.Item is null || e.SubItem is null) return;
        bool selected = e.Item.Selected;
        Color rowBg = selected
            ? Theme.SelectionBg
            : (e.ItemIndex % 2 == 1 ? Theme.InputAltBg : Theme.InputBg);

        using (var bg = new SolidBrush(rowBg))
            e.Graphics.FillRectangle(bg, e.Bounds);

        using (var linePen = new Pen(Color.FromArgb(38, 22, 28), 1f))
            e.Graphics.DrawLine(linePen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        if (selected && e.ColumnIndex == 0)
        {
            using var barBrush = new SolidBrush(Color.FromArgb(238, 56, 74));
            e.Graphics.FillRectangle(barBrush, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
        }

        int textOffsetX = 8;
        if (e.ColumnIndex == 0 && CheckBoxes)
        {
            int boxSize = 14;
            int boxX = e.Bounds.X + 7;
            int boxY = e.Bounds.Y + (e.Bounds.Height - boxSize) / 2;
            var boxRect = new Rectangle(boxX, boxY, boxSize, boxSize);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (e.Item.Checked)
            {
                using var fill = new SolidBrush(Theme.Blue);
                using var pen = new Pen(Color.FromArgb(244, 72, 88), 1f);
                e.Graphics.FillRectangle(fill, boxRect);
                e.Graphics.DrawRectangle(pen, boxRect);
                using var checkPen = new Pen(Color.White, 1.8f);
                e.Graphics.DrawLines(checkPen,
                [
                    new Point(boxX + 3, boxY + 7),
                    new Point(boxX + 6, boxY + 10),
                    new Point(boxX + 11, boxY + 4)
                ]);
            }
            else
            {
                using var fill = new SolidBrush(Theme.Background);
                using var pen = new Pen(Theme.Border, 1f);
                e.Graphics.FillRectangle(fill, boxRect);
                e.Graphics.DrawRectangle(pen, boxRect);
            }
            textOffsetX = 28;
        }

        Color fg = selected ? Color.White : Theme.Text;
        var textRect = new Rectangle(e.Bounds.X + textOffsetX, e.Bounds.Y, Math.Max(0, e.Bounds.Width - textOffsetX - 6), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, textRect, fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
