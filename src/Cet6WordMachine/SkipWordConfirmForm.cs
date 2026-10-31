namespace Cet6WordMachine;

internal sealed class SkipWordConfirmForm : UiDialog
{
    public SkipWordConfirmForm(string word)
    {
        Text = "确认跳过此词";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(500, 340);
        BackColor = UiTheme.Page;
        Font = UiTheme.Font(11);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(24),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, Text = $"确认已掌握 {word}？",
            Font = new Font(Font.FontFamily, 15, FontStyle.Bold),
            ForeColor = UiTheme.Ink, TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        var description = new Label
        {
            Name = "SkipConfirmationDescription",
            Dock = DockStyle.Fill,
            Text = "此词将不再安排学习或复习。\n\n今日任务会尽量补入后续新词。已有记录保留，可在菜单的“已跳过词”中恢复。",
            ForeColor = UiTheme.Muted,
        };
        layout.Controls.Add(description, 0, 1);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        var cancel = new CleanButton
        {
            Name = "CancelPermanentSkip", Text = "取消", DialogResult = DialogResult.Cancel,
            Size = new Size(130, 42), Margin = new Padding(8, 4, 0, 0),
        };
        var confirm = new CleanButton
        {
            Name = "ConfirmPermanentSkip", Text = "确认跳过", DialogResult = DialogResult.OK,
            Primary = true,
            Size = new Size(160, 42), Margin = new Padding(8, 4, 0, 0),
            BackColor = Color.FromArgb(15, 118, 110), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(confirm);
        layout.Controls.Add(actions, 0, 2);
        FinishLayout(layout);
        // A habitual Enter/Escape must cancel, never confirm an accidental click.
        AcceptButton = cancel;
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            layout.PerformLayout();
            var required = UiLayout.TextHeight(description, description.ClientSize.Width);
            if (required > description.ClientSize.Height)
                ClientSize = new Size(ClientSize.Width, ClientSize.Height + required - description.ClientSize.Height + 12);
            cancel.Focus();
        };
    }
}
