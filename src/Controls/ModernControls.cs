using System.Drawing.Drawing2D;

namespace RainAura.LunarFontFixer.Controls;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(243, 244, 246);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceAlt = Color.FromArgb(248, 249, 250);
    public static readonly Color Border = Color.FromArgb(211, 214, 219);
    public static readonly Color Text = Color.FromArgb(28, 30, 34);
    public static readonly Color Muted = Color.FromArgb(104, 109, 118);
    public static readonly Color Cyan = Color.FromArgb(66, 218, 255);
    public static readonly Color Purple = Color.FromArgb(137, 100, 255);
    public static readonly Color Green = Color.FromArgb(68, 219, 148);
    public static readonly Color Orange = Color.FromArgb(255, 181, 71);
    public static readonly Color Red = Color.FromArgb(255, 91, 119);
    public static readonly Color Success = Color.FromArgb(31, 128, 71);
    public static readonly Color Warning = Color.FromArgb(164, 96, 0);
    public static readonly Color Danger = Color.FromArgb(196, 43, 28);

    public static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal class RoundedPanel : Panel
{
    public int Radius { get; set; } = 18;
    public Color BorderColor { get; set; } = Theme.Border;
    public int BorderWidth { get; set; } = 1;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Padding = new Padding(1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.Rounded(rect, Radius);
        using var brush = new SolidBrush(BackColor);
        using var pen = new Pen(BorderColor, BorderWidth);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
        base.OnPaint(e);
    }
}

internal sealed class AccentButton : Button
{
    private bool _hovered;

    public bool Primary { get; set; }
    public Color Accent { get; set; } = Theme.Cyan;

    public AccentButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        ForeColor = Theme.Text;
        Height = 44;
        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.Rounded(rect, 11);

        if (Primary)
        {
            var end = Color.FromArgb(137, 100, 255);
            using var gradient = new LinearGradientBrush(rect, Accent, end, 20F);
            e.Graphics.FillPath(gradient, path);
            if (_hovered)
            {
                using var overlay = new SolidBrush(Color.FromArgb(24, Color.White));
                e.Graphics.FillPath(overlay, path);
            }
        }
        else
        {
            using var fill = new SolidBrush(_hovered ? Theme.SurfaceAlt : Theme.Surface);
            using var border = new Pen(_hovered ? Accent : Theme.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        TextRenderer.DrawText(e.Graphics, Text, Font, rect, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class IconTextButton : Button
{
    private bool _hovered;

    public IconTextButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(_hovered ? Color.FromArgb(242, 243, 245) : BackColor);

        var textSize = TextRenderer.MeasureText(e.Graphics, Text, Font,
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        var imageWidth = Image?.Width ?? 0;
        var imageHeight = Image?.Height ?? 0;
        var gap = imageWidth > 0 && Text.Length > 0 ? 6 : 0;
        var groupWidth = imageWidth + gap + textSize.Width;
        var startX = Math.Max(0, (ClientSize.Width - groupWidth) / 2);

        if (Image is not null)
            e.Graphics.DrawImage(Image, startX, (ClientSize.Height - imageHeight) / 2, imageWidth, imageHeight);

        var textRect = new Rectangle(startX + imageWidth + gap, 0, textSize.Width, ClientSize.Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal sealed class StatCard : RoundedPanel
{
    private readonly Label _value;
    private readonly Label _caption;

    public StatCard(string caption)
    {
        Radius = 16;
        BackColor = Theme.Surface;
        _value = new Label
        {
            AutoSize = false,
            Location = new Point(18, 15),
            Size = new Size(190, 32),
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = Theme.Text,
            Text = "—",
            BackColor = Color.Transparent
        };
        _caption = new Label
        {
            AutoSize = false,
            Location = new Point(20, 51),
            Size = new Size(190, 24),
            Font = new Font("Segoe UI", 9F),
            ForeColor = Theme.Muted,
            Text = caption,
            BackColor = Color.Transparent
        };
        Controls.Add(_value);
        Controls.Add(_caption);
    }

    public void SetValue(string value, Color color)
    {
        _value.Text = value;
        _value.ForeColor = color;
    }
}

internal sealed class StatusDot : Control
{
    public Color DotColor { get; set; } = Theme.Muted;

    public StatusDot()
    {
        Size = new Size(18, 18);
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var glow = new SolidBrush(Color.FromArgb(45, DotColor));
        using var core = new SolidBrush(DotColor);
        e.Graphics.FillEllipse(glow, 0, 0, Width, Height);
        e.Graphics.FillEllipse(core, 5, 5, Width - 10, Height - 10);
    }
}
