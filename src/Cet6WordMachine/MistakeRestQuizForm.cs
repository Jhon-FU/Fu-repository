namespace Cet6WordMachine;

internal sealed class MistakeRestQuizForm : UiDialog
{
    private readonly MistakeItem _item;
    private readonly TextBox _answer;
    private readonly TextBox _comparison;
    private readonly Label _status;
    private readonly Control _inputActions, _grades;
    private bool _revealed;
    internal int? SelfGrade { get; private set; }
    internal bool WasInvalidated { get; private set; }

    internal MistakeRestQuizForm(MistakeItem item)
    {
        _item = item;
        Name = "MistakeRestQuizForm"; Text = "答题隐藏错题卡";
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; TopMost = true;
        MaximizeBox = false; MinimizeBox = false; BackColor = UiTheme.Page; Font = UiTheme.Font();
        AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(600, 480); MinimumSize = new Size(520, 440);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(24) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 38, 64, 28 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        root.Controls.Add(UiTheme.Label("答一题，休息 1 小时", 17, true), 0, 0);
        var word = UiTheme.Label(item.Word, 26, true); word.Name = "RestQuizWord"; UiLayout.FitWord(word); root.Controls.Add(word, 0, 1);
        root.Controls.Add(UiTheme.Label("中文意思", 11), 0, 2);
        var body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _answer = new TextBox { Name = "RestQuizAnswer", Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Font = UiTheme.Font(12), PlaceholderText = "先写出你记得的意思，大意也可以", BorderStyle = BorderStyle.FixedSingle };
        _comparison = new TextBox { Name = "RestQuizComparison", Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Font = UiTheme.Font(12), ReadOnly = true, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Visible = false };
        body.Controls.Add(_answer); body.Controls.Add(_comparison); root.Controls.Add(body, 0, 3);
        _status = UiTheme.Label("基本说对即可隐藏；其余情况需等 2 分钟。", 11); _status.Name = "RestQuizStatus"; _status.ForeColor = UiTheme.Muted;
        root.Controls.Add(_status, 0, 4);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var submit = UiTheme.Button("查看答案", true); submit.Name = "RevealRestQuiz"; submit.Click += (_, _) => Reveal();
        var noIdea = UiTheme.Button("想不起来"); noIdea.Name = "RestQuizNoIdea"; noIdea.Click += (_, _) => Finish(0);
        var choiceHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _inputActions = Buttons(noIdea, submit); choiceHost.Controls.Add(_inputActions); actions.Controls.Add(choiceHost, 0, 0);
        var bad = UiTheme.Button("完全忘了"); bad.Name = "RestQuizGrade0"; bad.Click += (_, _) => Finish(0);
        var partial = UiTheme.Button("只想起一点"); partial.Name = "RestQuizGrade1"; partial.Click += (_, _) => Finish(1);
        var good = UiTheme.Button("基本说对", true); good.Name = "RestQuizGrade2"; good.Click += (_, _) => Finish(2);
        _grades = Buttons(bad, partial, good); _grades.Visible = false; _grades.Enabled = false;
        choiceHost.Controls.Add(_grades);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        var cancel = UiTheme.Button("取消（等 2 分钟）"); cancel.Name = "CancelRestQuiz"; cancel.Width = 190; cancel.Margin = new Padding(0, 6, 0, 0); cancel.DialogResult = DialogResult.Cancel;
        bottom.Controls.Add(cancel); actions.Controls.Add(bottom, 0, 1); root.Controls.Add(actions, 0, 5);
        FinishLayout(root); CancelButton = cancel;
        Shown += (_, _) => { Bounds = WidgetScreenBounds.Clamp(Bounds, Screen.FromControl(this).WorkingArea); _answer.Focus(); };
    }

    private static Control Buttons(params Button[] buttons)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = buttons.Length, RowCount = 1, Margin = Padding.Empty };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var index = 0; index < buttons.Length; index++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / buttons.Length));
            buttons[index].Dock = DockStyle.Fill; buttons[index].Margin = new Padding(index == 0 ? 0 : 5, 0, index == buttons.Length - 1 ? 0 : 5, 8);
            row.Controls.Add(buttons[index], index, 0);
        }
        return row;
    }
    private void Reveal()
    {
        if (_revealed) return;
        if (string.IsNullOrWhiteSpace(_answer.Text))
        { _status.Text = "请先输入中文意思，或选择“想不起来”。"; _status.ForeColor = UiTheme.Error; _answer.Focus(); return; }
        _revealed = true; _answer.ReadOnly = true;
        _comparison.Text = "参考释义：" + _item.Meaning + "\r\n\r\n你的回答：" + _answer.Text.Trim();
        _answer.Visible = false; _comparison.Visible = true; _comparison.BringToFront();
        _comparison.Select(0, 0);
        _inputActions.Visible = false; _grades.Visible = true; _grades.Enabled = true; _grades.BringToFront();
        _status.Text = "对照释义，按真实掌握程度自评。"; _status.ForeColor = UiTheme.Muted;
    }
    private void Finish(int grade)
    {
        if (SelfGrade is not null || WasInvalidated || (grade > 0 && !_revealed)) return;
        SelfGrade = grade; DialogResult = DialogResult.OK; Close();
    }
    internal void CancelForUpdate()
    { WasInvalidated = true; DialogResult = DialogResult.Cancel; Close(); }
}
