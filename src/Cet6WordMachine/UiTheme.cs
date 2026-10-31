using System.Drawing.Drawing2D;

namespace Cet6WordMachine;

internal static class UiTheme
{
    internal static readonly Color Page = Color.FromArgb(244, 247, 246);
    internal static readonly Color Ink = Color.FromArgb(36, 51, 47);
    internal static readonly Color Muted = Color.FromArgb(100, 115, 109);
    internal static readonly Color Accent = Color.FromArgb(25, 116, 105);
    internal static readonly Color Line = Color.FromArgb(227, 234, 230);
    internal static readonly Color Soft = Color.FromArgb(237, 243, 239);
    internal static readonly Color Error = Color.FromArgb(165, 48, 43);
    private static readonly Dictionary<(float, FontStyle), Font> Fonts = [];

    internal static Font Font(float size = 11.5f, FontStyle style = FontStyle.Regular)
    {
        var key = (size, style);
        if (!Fonts.TryGetValue(key, out var font)) Fonts[key] = font = new Font("Microsoft YaHei UI", size, style);
        return font;
    }

    internal static Label Label(string text, float size = 11.5f, bool bold = false) => new()
    {
        Text = text, Font = Font(size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = Ink, BackColor = Color.Transparent, Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty,
    };

    internal static CleanButton Button(string text, bool primary = false) => new()
    {
        Text = text, Primary = primary, Size = new Size(150, 46), Margin = new Padding(0, 0, 10, 0),
    };

    internal static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class CleanButton : Button
{
    private bool _hover;
    private bool _pressed;
    private bool _primary;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Primary { get => _primary; set { _primary = value; Invalidate(); } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Quiet { get; set; }

    public CleanButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = Color.White;
        ForeColor = UiTheme.Ink;
        Font = UiTheme.Font();
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        var background = Parent?.BackColor ?? UiTheme.Page;
        e.Graphics.Clear(background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = Primary ? UiTheme.Accent : Quiet ? background : Color.White;
        if (_hover && Enabled) color = Primary ? ControlPaint.Dark(UiTheme.Accent, .08f) : UiTheme.Soft;
        if (_pressed && Enabled) color = Primary ? ControlPaint.Dark(UiTheme.Accent, .16f) : UiTheme.Line;
        if (!Enabled) color = UiTheme.Soft;
        using var shape = UiTheme.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), UiLayout.Pixels(this, 9));
        using var fill = new SolidBrush(color);
        e.Graphics.FillPath(fill, shape);
        if (!Primary && !Quiet)
        {
            using var border = new Pen(_hover && Enabled ? UiTheme.Accent : UiTheme.Line);
            e.Graphics.DrawPath(border, shape);
        }
        var ink = !Enabled ? UiTheme.Muted : Primary ? Color.White : Quiet ? UiTheme.Muted : UiTheme.Ink;
        var bounds = Rectangle.Inflate(ClientRectangle, -UiLayout.Pixels(this, 8), -2);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -6, -6), ink, color);
    }
}

internal sealed class SurfaceTable : TableLayoutPanel
{
    public SurfaceTable()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        Margin = Padding.Empty;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Page);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = UiTheme.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), UiLayout.Pixels(this, 16));
        using var fill = new SolidBrush(BackColor);
        using var line = new Pen(UiTheme.Line);
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(line, shape);
    }
}

internal sealed class SlimProgress : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int Maximum { get; set; } = 100;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int Value { get; set; }
    public SlimProgress()
    {
        Height = 8;
        AccessibleRole = AccessibleRole.ProgressBar;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Page);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var height = Math.Min(Height, UiLayout.Pixels(this, 6));
        var top = (Height - height) / 2f;
        using var track = UiTheme.Rounded(new RectangleF(0, top, Width, height), height / 2f);
        using var muted = new SolidBrush(UiTheme.Soft);
        e.Graphics.FillPath(muted, track);
        var width = Width * Math.Clamp(Value / (float)Math.Max(1, Maximum), 0, 1);
        if (width > 0)
        {
            using var bar = UiTheme.Rounded(new RectangleF(0, top, width, height), height / 2f);
            using var fill = new SolidBrush(UiTheme.Accent);
            e.Graphics.FillPath(fill, bar);
        }
    }
}

// Forms are created at runtime rather than by the WinForms designer. Scale the
// logical control geometry explicitly; point-sized fonts already follow DPI.
internal class UiDialog : Form
{
    private Control? _layout;
    private int _layoutDpi = 96;
    internal void FinishLayout(Control layout)
    {
        AutoScaleMode = AutoScaleMode.None;
        var logical = ClientSize; var minimum = MinimumSize;
        _layoutDpi = DeviceDpi; _layout = layout;
        UiLayout.Scale(layout, _layoutDpi / 96f);
        var area = Screen.FromControl(this).WorkingArea;
        var available = new Size(area.Width - UiLayout.Pixels(this, 32), area.Height - UiLayout.Pixels(this, 48));
        MinimumSize = new Size(Math.Min(UiLayout.Pixels(this, minimum.Width), available.Width), Math.Min(UiLayout.Pixels(this, minimum.Height), available.Height));
        ClientSize = new Size(Math.Min(UiLayout.Pixels(this, logical.Width), available.Width), Math.Min(UiLayout.Pixels(this, logical.Height), available.Height));
        Controls.Add(layout);
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (_layout is null) return;
        UiLayout.Scale(_layout, e.DeviceDpiNew / (float)_layoutDpi);
        _layoutDpi = e.DeviceDpiNew;
    }
}
