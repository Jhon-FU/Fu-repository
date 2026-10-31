namespace Cet6WordMachine;

internal static class UiLayout
{
    private static readonly Dictionary<(string Name, float Size, FontStyle Style), Font> Fonts = [];

    public static int Pixels(Control control, int logical) =>
        (int)Math.Ceiling(logical * control.DeviceDpi / 96f);

    // Fonts use points and already follow Windows DPI. Scale their containers once.
    public static void Scale(Control control, float factor)
    {
        if (Math.Abs(factor - 1) > 0.001f)
        {
            control.Scale(new SizeF(factor, factor));
        }
    }

    public static int TextHeight(Label label, int width)
    {
        using var graphics = label.CreateGraphics();
        return TextRenderer.MeasureText(graphics, label.Text, label.Font,
            new Size(Math.Max(1, width - label.Padding.Horizontal), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height
            + label.Padding.Vertical;
    }

    public static void FitWord(Label label)
    {
        var preferred = label.Font.Size;
        var style = label.Font.Style;
        var name = label.Font.Name;
        var pending = false;
        void QueueResize()
        {
            if (pending || label.IsDisposed || !label.IsHandleCreated) return;
            pending = true;
            label.BeginInvoke(new Action(() =>
            {
                pending = false;
                if (!label.IsDisposed) ResizeWord();
            }));
        }
        void ResizeWord()
        {
            if (label.IsDisposed || label.ClientSize.Width <= 0) return;
            using var graphics = label.CreateGraphics();
            for (var size = preferred; size >= 8; size -= 0.5f)
            {
                var key = (name, size, style);
                if (!Fonts.TryGetValue(key, out var font))
                {
                    font = new Font(name, size, style, GraphicsUnit.Point);
                    Fonts[key] = font;
                }
                var measured = TextRenderer.MeasureText(graphics, label.Text, font,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine);
                if (measured.Width <= label.ClientSize.Width - label.Padding.Horizontal - Pixels(label, 8) &&
                    measured.Height <= label.ClientSize.Height - label.Padding.Vertical)
                {
                    if (label.Font != font) label.Font = font;
                    break;
                }
            }
        }
        // Changing Font synchronously inside a table's SetBounds can overwrite
        // that layout's new bounds. Fit only after the layout event completes.
        label.SizeChanged += (_, _) => QueueResize();
        label.HandleCreated += (_, _) => QueueResize();
        QueueResize();
    }

    public static void ScrollTextTable(Panel viewport, TableLayoutPanel table)
    {
        viewport.AutoScroll = true;
        table.Dock = DockStyle.Top;
        table.ColumnStyles.Clear();
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var resizing = false;
        void ResizeText()
        {
            if (resizing || viewport.IsDisposed || table.IsDisposed || !table.Visible) return;
            resizing = true;
            try
            {
                var width = Math.Max(Pixels(viewport, 80), viewport.ClientSize.Width
                    - SystemInformation.VerticalScrollBarWidth - table.Padding.Horizontal);
                var totalHeight = table.Padding.Vertical;
                for (var row = 0; row < table.RowCount; row++)
                {
                    var controls = table.Controls.Cast<Control>()
                        .Where(item => table.GetRow(item) == row && item.Visible).ToList();
                    var height = controls.Count == 0 ? 0 : controls.Max(item => item is Label label
                        ? TextHeight(label, width - label.Margin.Horizontal) + label.Margin.Vertical + Pixels(viewport, 4)
                        : Math.Max(Pixels(viewport, 32), item.GetPreferredSize(new Size(width, 0)).Height)
                          + item.Margin.Vertical);
                    table.RowStyles[row] = new RowStyle(SizeType.Absolute, height);
                    totalHeight += height;
                }
                table.Height = totalHeight;
            }
            finally { resizing = false; }
        }
        viewport.SizeChanged += (_, _) => ResizeText();
        table.VisibleChanged += (_, _) => ResizeText();
        foreach (Control control in table.Controls)
        {
            control.TextChanged += (_, _) => ResizeText();
            control.VisibleChanged += (_, _) => ResizeText();
        }
        ResizeText();
    }
}
