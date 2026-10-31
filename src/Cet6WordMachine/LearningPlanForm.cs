namespace Cet6WordMachine;

internal sealed class LearningPlanForm : UiDialog
{
    private readonly AppState _snapshot;
    private readonly StudyEngine _engine;
    private readonly DateOnly _today;
    private readonly NumericUpDown _daily;
    private readonly DateTimePicker _exam;
    private readonly DateTimePicker _completion;
    private readonly Label _projection;
    private readonly Label _error;
    private readonly CleanButton _recommend;

    public LearningPlanForm(AppState state, DateOnly today, Func<int, string, string?, bool> save)
    {
        _snapshot = StateStore.CopyForUpdate(state);
        _engine = new StudyEngine(StateStore.LoadWordBank(_snapshot));
        _today = today;
        Text = "学习计划"; Name = "LearningPlanForm";
        Font = UiTheme.Font(); BackColor = UiTheme.Page;
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(620, 580);
        MinimumSize = new Size(500, 530); MinimizeBox = false; MaximizeBox = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 7, AutoScroll = true };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 52, 92, 92, 54, 80, 54, 54 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.Controls.Add(UiTheme.Label("学习计划", 22, true), 0, 0);
        _daily = new NumericUpDown { Name = "DailyNewWordsInput", Dock = DockStyle.Fill,
            Minimum = 1, Maximum = StudyEngine.MaximumNewWordsPerDay, Value = Math.Clamp(state.DailyNewWords, 1, StudyEngine.MaximumNewWordsPerDay),
            Font = UiTheme.Font(12), Margin = new Padding(0, 6, 0, 14) };
        _exam = new DateTimePicker { Name = "ExamDateInput", Dock = DockStyle.Fill, Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd", Value = PickerValue(DateOnly.TryParse(state.ExamDate, out var date) ? date : today), Margin = new Padding(0, 6, 0, 14) };
        root.Controls.Add(Field("每日新词", _daily), 0, 1); root.Controls.Add(Field("考试日期", _exam), 0, 2);
        _projection = UiTheme.Label(""); _projection.Name = "PlanProjection"; _projection.ForeColor = UiTheme.Muted;
        root.Controls.Add(_projection, 0, 3);
        var advanced = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2, Margin = Padding.Empty };
        advanced.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        _completion = new DateTimePicker { Name = "NewWordCompletionDate", Dock = DockStyle.Fill, ShowCheckBox = true,
            Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Margin = new Padding(0, 6, 14, 5),
            Value = PickerValue(DateOnly.TryParse(state.NewWordCompletionDate, out var completion) ? completion : today),
            Checked = DateOnly.TryParse(state.NewWordCompletionDate, out _) };
        advanced.Controls.Add(Field("新词学完日期（可选）", _completion), 0, 0);
        var automatic = UiTheme.Button("恢复自动"); automatic.Name = "ResetCompletionDate"; automatic.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; automatic.Margin = new Padding(0, 0, 0, 10);
        automatic.Click += (_, _) => { _completion.Checked = false; UpdateProjection(); };
        advanced.Controls.Add(automatic, 1, 0); root.Controls.Add(advanced, 0, 4);
        _error = UiTheme.Label("", 10.5f); _error.Name = "PlanError"; _error.ForeColor = UiTheme.Error; root.Controls.Add(_error, 0, 5);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        var apply = UiTheme.Button("保存计划", true); apply.Name = "SaveLearningPlan"; apply.Margin = Padding.Empty; apply.Width = 130;
        _recommend = UiTheme.Button("采用推荐"); _recommend.Name = "ApplyRecommendedQuota"; _recommend.Width = 170;
        var cancel = UiTheme.Button("返回"); cancel.DialogResult = DialogResult.Cancel; cancel.Quiet = true; cancel.Width = 90;
        actions.Controls.Add(apply); actions.Controls.Add(_recommend); actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 6);
        FinishLayout(root); CancelButton = cancel; AcceptButton = apply;
        _daily.ValueChanged += (_, _) => UpdateProjection();
        _exam.ValueChanged += (_, _) => UpdateProjection();
        _completion.ValueChanged += (_, _) => UpdateProjection();
        _recommend.Click += (_, _) =>
        {
            var plan = PreviewPlan();
            if (plan.CanApply) _daily.Value = Math.Clamp(plan.RequiredPerDay, 1, StudyEngine.MaximumNewWordsPerDay);
        };
        apply.Click += (_, _) =>
        {
            if (DateOnly.FromDateTime(_exam.Value) < today) { _error.Text = "考试日期不能早于今天。"; return; }
            if (_completion.Checked && DateOnly.FromDateTime(_completion.Value) < today) { _error.Text = "新词学完日期不能早于今天。"; return; }
            if (!save((int)_daily.Value, DateOnly.FromDateTime(_exam.Value).ToString("yyyy-MM-dd"),
                    _completion.Checked ? DateOnly.FromDateTime(_completion.Value).ToString("yyyy-MM-dd") : null))
            { _error.Text = "保存未完成。请检查文件占用；日期变化时请重新打开计划。"; return; }
            DialogResult = DialogResult.OK; Close();
        };
        UpdateProjection();
    }

    private static DateTime PickerValue(DateOnly date) => new(Math.Clamp(date.ToDateTime(TimeOnly.MinValue).Ticks,
        DateTimePicker.MinimumDateTime.Ticks, DateTimePicker.MaximumDateTime.Ticks));

    private static TableLayoutPanel Field(string title, Control input)
    {
        var field = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        field.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); field.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        field.Controls.Add(UiTheme.Label(title), 0, 0); field.Controls.Add(input, 0, 1); return field;
    }

    private NewWordPlan PreviewPlan()
    {
        // Only the private snapshot changes while the dialog is being edited.
        _snapshot.ExamDate = DateOnly.FromDateTime(_exam.Value).ToString("yyyy-MM-dd");
        _snapshot.NewWordCompletionDate = _completion.Checked ? DateOnly.FromDateTime(_completion.Value).ToString("yyyy-MM-dd") : null;
        return _engine.GetNewWordPlan(_snapshot, _today);
    }

    private void UpdateProjection()
    {
        var plan = PreviewPlan();
        var finish = plan.ProjectedFinish((int)_daily.Value);
        _recommend.Enabled = plan.CanApply;
        _recommend.Text = plan.CanApply ? $"采用推荐：{plan.RequiredPerDay}/天" : "暂无推荐";
        _error.Text = "";
        _projection.ForeColor = UiTheme.Muted;
        if (plan.Status == NewWordPlanStatus.AllLearned) _projection.Text = "新词已学完，继续完成到期复习。";
        else if (plan.Status == NewWordPlanStatus.FinishCurrentSession) _projection.Text = "剩余新词已在今日任务中。";
        else if (finish is null || plan.Status is NewWordPlanStatus.NoStudyDays or NewWordPlanStatus.InvalidDate)
        { _projection.Text = "请调整日期，当前目标无法安排。"; _projection.ForeColor = UiTheme.Error; }
        else
        {
            var deadline = plan.CompletionDate ?? plan.ExamDate;
            var late = deadline is not null && (plan.HasCustomDeadline ? finish > deadline : finish >= deadline);
            _projection.Text = $"预计 {finish:yyyy-MM-dd} 学完" + (late ? " · 超出目标日期" : "") + (plan.CanApplyToday ? "" : " · 明天起生效");
            if (late) _projection.ForeColor = UiTheme.Error;
        }
    }
}
