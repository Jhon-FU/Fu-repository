namespace Cet6WordMachine;

internal sealed partial class MainForm
{
    private Panel BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(24, 14, 18, 14) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.White, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        var title = UiTheme.Label("单词机", 16, true); title.Name = "ApplicationTitle";
#if TESTMODE
        title.Text = "单词机 · 测试";
        title.Font = UiFont(13);
#endif
        title.ForeColor = TealColor;
        _wordBankLabel = UiTheme.Label(_state.ActiveWordBankName, 10.5f);
        _wordBankLabel.Name = "CurrentWordBankLabel";
        _wordBankLabel.AutoEllipsis = true;
        _wordBankLabel.ForeColor = MutedColor;
        _wordBankLabel.TextAlign = ContentAlignment.MiddleRight;
        _wordBankLabel.Padding = new Padding(0, 0, 14, 0);
        _applicationMenu = new ContextMenuStrip { Name = "ApplicationMenu", ShowImageMargin = false, Font = UiFont(11), BackColor = Color.White };
        void Menu(string text, string name, Action action)
        {
            var item = new ToolStripMenuItem(text) { Name = name, Padding = new Padding(9, 5, 9, 5) };
            item.Click += (_, _) => action();
            _applicationMenu.Items.Add(item);
        }
        Menu("今日任务", "TodayMenuItem", () => ShowHome(_state.ActiveSession is { Completed: false }));
        Menu("词库管理", "WordBankManagerMenuItem", ShowWordBankManager);
        Menu("学习计划", "LearningPlanMenuItem", ShowLearningPlan);
        Menu("AI 设置", "AiSettingsMenuItem", ShowAiSettings);
        Menu("已跳过词", "SkippedWordsMenu", ShowSkippedWords);
        _applicationMenu.Items.Add(new ToolStripSeparator());
        _enableStartupMenuItem = new ToolStripMenuItem("一键开启开机自启动") { Name = "EnableAutoStartMenuItem", Padding = new Padding(9, 5, 9, 5) };
        _enableStartupMenuItem.Click += (_, _) =>
        {
            var result = _startupRegistration.Enable();
            RefreshStartupMenu();
            SetStatus(result.Message, isWarning: !result.Success);
        };
        _applicationMenu.Items.Add(_enableStartupMenuItem);
        Menu("使用帮助", "HelpMenuItem", ShowHelp);
#if TESTMODE
        if (_testMode)
        {
            _applicationMenu.Items.Add(new ToolStripSeparator());
            Menu("跳到下一天", "NextDayMenuItem", JumpToNextDay);
        }
#endif
        _applicationMenu.Opening += (_, _) => RefreshStartupMenu();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
        var close = UiTheme.Button("×"); close.Name = "CloseWindowButton"; close.AccessibleName = "关闭窗口"; close.Size = new Size(40, 40); close.Quiet = true; close.Margin = Padding.Empty;
        close.Click += (_, _) => Close();
        _toolTips.SetToolTip(close, "关闭窗口");
        var minimize = UiTheme.Button("−"); minimize.AccessibleName = "最小化"; minimize.Size = new Size(40, 40); minimize.Quiet = true; minimize.Margin = new Padding(2, 0, 2, 0);
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _toolTips.SetToolTip(minimize, "最小化");
        var menu = UiTheme.Button("菜单 ▾"); menu.Name = "ApplicationMenuButton"; menu.Size = new Size(88, 40); menu.Quiet = true; menu.Margin = new Padding(4, 0, 4, 0);
        menu.Click += (_, _) => _applicationMenu.Show(menu, new Point(menu.Width, menu.Height), ToolStripDropDownDirection.BelowLeft);
        actions.Controls.Add(close); actions.Controls.Add(minimize); actions.Controls.Add(menu);
        layout.Controls.Add(title, 0, 0); layout.Controls.Add(_wordBankLabel, 1, 0); layout.Controls.Add(actions, 2, 0);
        header.Controls.Add(layout);
        header.Paint += (_, e) => { using var pen = new Pen(BorderColor); e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1); };
        header.MouseDown += (_, e) => BeginWindowDrag(e);
        title.MouseDown += (_, e) => BeginWindowDrag(e);
        _wordBankLabel.MouseDown += (_, e) => BeginWindowDrag(e);
        return header;
    }

    private void ShowHome(bool hasUnfinishedSession)
    {
        ClearContent();
        var due = _engine.DueCount(_state, _currentDate);
        var unlearned = _engine.UnlearnedCount(_state);
        var daily = Math.Clamp(_state.DailyNewWords, 1, StudyEngine.MaximumNewWordsPerDay);
        var completed = _engine.HasCompletedSession(_state, _currentDate);
        var map = _state.ProgressMap;
        var active = _state.ActiveSession is { Completed: false } current && current.Date == _currentDate ? current : null;
        var newToday = completed ? 0 : active is not null
            ? active.Words.Count(word => !map.ContainsKey(word) && !_engine.IsSkipped(_state, word))
            : Math.Min(Math.Max(0, daily - _state.Progress.Count(item => item.IntroducedOn == _currentDate && !_engine.IsSkipped(_state, item.Word))), unlearned);
        if (active is not null) due = active.Words.Count(word => !active.SelfGrades.ContainsKey(word) &&
            map.TryGetValue(word, out var item) && item.IntroducedOn < _currentDate && !_engine.IsSkipped(_state, word));
        var learned = _engine.LearnedCount(_state);
        var total = _engine.LearningTargetCount(_state);
        var panel = new TableLayoutPanel { ColumnCount = 1, RowCount = 5, BackColor = PageColor, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 64, 230, 32, 60, 48 }) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        heading.Controls.Add(UiTheme.Label(completed ? "今天已完成" : "今天的任务", 23, true), 0, 0);
        var date = UiTheme.Label(_currentDate.ToString("MM 月 dd 日"), 10.5f); date.ForeColor = MutedColor; date.TextAlign = ContentAlignment.MiddleRight;
        heading.Controls.Add(date, 1, 0);
        panel.Controls.Add(heading, 0, 0);

        var card = new SurfaceTable { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 2 };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); card.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        var stats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 22), BackColor = Color.White };
        stats.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        Control Metric(string title, int value, string name, bool first)
        {
            var block = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = first ? Padding.Empty : new Padding(24, 0, 0, 0), BackColor = Color.White };
            block.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            block.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); block.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var label = UiTheme.Label(title); label.ForeColor = MutedColor;
            var number = UiTheme.Label(value.ToString(), 34); number.Name = name;
            block.Controls.Add(label, 0, 0); block.Controls.Add(number, 0, 1);
            if (first) block.Paint += (_, e) => { using var pen = new Pen(BorderColor); e.Graphics.DrawLine(pen, block.Width - 1, 8, block.Width - 1, block.Height - 8); };
            return block;
        }
        stats.Controls.Add(Metric("新词", newToday, "TodayNewCount", true), 0, 0);
        stats.Controls.Add(Metric("复习", due, "TodayReviewCount", false), 1, 0);
        card.Controls.Add(stats, 0, 0);
        var start = UiTheme.Button(completed ? "查看今日结果" : hasUnfinishedSession ? "继续学习" : "开始学习", true);
        start.Name = "StartLearningButton"; start.Dock = DockStyle.Fill; start.Margin = Padding.Empty;
        start.Click += (_, _) => StartSession();
        card.Controls.Add(start, 0, 1); panel.Controls.Add(card, 0, 1);
        var progress = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        progress.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); progress.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        progress.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); progress.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        progress.Controls.Add(UiTheme.Label("词库进度"), 0, 0);
        var fraction = UiTheme.Label($"{learned:N0} / {total:N0} 词", 10.5f); fraction.Name = "HomeProgressLabel"; fraction.ForeColor = MutedColor; fraction.TextAlign = ContentAlignment.MiddleRight;
        progress.Controls.Add(fraction, 1, 0);
        var bar = new SlimProgress { Dock = DockStyle.Fill, Value = learned, Maximum = total, AccessibleName = $"已学 {learned}，共 {total} 词", Margin = Padding.Empty };
        progress.Controls.Add(bar, 0, 1); progress.SetColumnSpan(bar, 2); panel.Controls.Add(progress, 0, 3);
        var plan = UiTheme.Button("学习计划 ↗"); plan.Name = "LearningPlanButton"; plan.Quiet = true; plan.Anchor = AnchorStyles.Right; plan.Margin = Padding.Empty;
        plan.Click += (_, _) => ShowLearningPlan(); panel.Controls.Add(plan, 0, 4);
        AddCenteredPage(panel, "HomePage", 680, 434);
    }

    private void AddCenteredPage(Control content, string name, int width, int height, bool flexible = false)
    {
        var host = new Panel { Name = name, BackColor = PageColor };
        host.Controls.Add(content);
        void LayoutContent()
        {
            var targetWidth = Math.Min(UiLayout.Pixels(this, width), host.ClientSize.Width);
            var targetHeight = flexible ? Math.Max(UiLayout.Pixels(this, 360), Math.Min(UiLayout.Pixels(this, height), host.ClientSize.Height)) : UiLayout.Pixels(this, height);
            content.Bounds = new Rectangle(Math.Max(0, (host.ClientSize.Width - targetWidth) / 2),
                Math.Max(0, (host.ClientSize.Height - targetHeight) / 2), targetWidth, targetHeight);
        }
        host.SizeChanged += (_, _) => LayoutContent();
        AddPage(host); LayoutContent();
    }

    private void ShowLearningPlan()
    {
        var resumeTimer = _dayTimer.Enabled;
        var planDate = _currentDate;
        _dayTimer.Stop();
        try
        {
            using var dialog = new LearningPlanForm(_state, _currentDate, (quantity, examDate, completion) =>
            {
                if (GetToday() != planDate) { CheckForNewDay(); return false; }
                var canApplyToday = _engine.CanChangeNewQuotaToday(_state, _currentDate);
                var quantityChanged = quantity != _state.DailyNewWords;
                return TrySaveChange(candidate =>
                {
                    candidate.DailyNewWords = quantity;
                    candidate.ExamDate = examDate;
                    candidate.NewWordCompletionDate = completion;
                    if (canApplyToday && quantityChanged) candidate.ActiveSession = null;
                });
            });
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            RefreshChrome();
            ShowHome(_state.ActiveSession is { Completed: false });
            SetStatus(_engine.CanChangeNewQuotaToday(_state, _currentDate) ? "学习计划已保存。" : "学习计划已保存；每日数量从明天生效。", false);
        }
        finally { if (resumeTimer && !IsDisposed) _dayTimer.Start(); }
    }

    private void ShowAiSettings()
    {
        var resumeTimer = _dayTimer.Enabled;
        _dayTimer.Stop();
        try { using var settings = new AiSettingsForm(_aiServices); settings.ShowDialog(this); }
        finally { if (resumeTimer && !IsDisposed) _dayTimer.Start(); }
    }

    private void ShowHelp() => UiInformation.Show(this, "使用帮助",
        "学习与检测\r\n先判断是否认识单词，再回忆中文含义。查看答案后按实际情况自评；不要求拼写。\r\n\r\n" +
        "今日任务与退出\r\n完成今日任务后可关闭。紧急情况下可以在任务管理器结束进程，系统关机也不会被阻止。\r\n\r\n" +
        "开机自启动\r\n在菜单中一键开启。撤销时运行程序目录中的“撤销开机自启动.cmd”；软件不会自动恢复已撤销的启动项。\r\n\r\n" +
        "词库与进度\r\n每个词库分别保存任务和进度，切换回来可继续。首次使用新词库时可沿用同词同义的进度。\r\n\r\n" +
        "跳过与恢复\r\n跳过的词不再安排学习或复习；可在菜单的“已跳过词”中恢复。\r\n\r\n" +
        "学习计划\r\n自动计划优先预留复习时间；可设置每日数量、考试日期或新词学完日期。已开始的今日任务保留原数量。\r\n\r\n" +
        "AI 联想（可选）\r\n在 AI 设置中填写自己的 DeepSeek Key，然后在“我的联想”中点击 AI 生成。仅发送当前词、音标和释义，费用由你的 API 账号承担。结果可修改，点击保存后才生效；已保存的联想可离线使用。\r\n\r\n" +
        "来源与开源许可\r\n词库管理中的“词库说明与来源”保留完整来源信息。开源许可及第三方声明随程序提供。");

    private void HideStatus()
    {
        _statusTimer.Stop();
        _statusLabel.Text = "";
        _shell.RowStyles[2].Height = 0;
        _statusPanel.Visible = false;
        _persistentStatus = false;
    }

    private void SetStatus(string text, bool isWarning)
    {
        _statusTimer.Stop();
        _persistentStatus = isWarning;
        _statusLabel.Text = text;
        _statusLabel.BackColor = isWarning ? Color.FromArgb(253, 238, 234) : UiTheme.Soft;
        _statusLabel.ForeColor = isWarning ? UiTheme.Error : InkColor;
        _statusPanel.BackColor = _statusLabel.BackColor;
        _toolTips.SetToolTip(_statusLabel, text);
        var width = Math.Max(UiLayout.Pixels(this, 250), ClientSize.Width - UiLayout.Pixels(this, 40));
        var measured = TextRenderer.MeasureText(text, _statusLabel.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak);
        _shell.RowStyles[2].Height = Math.Max(UiLayout.Pixels(this, 40), measured.Height + UiLayout.Pixels(this, 18));
        _statusPanel.Visible = true;
        if (!isWarning) _statusTimer.Start();
    }
}

internal static class UiInformation
{
    internal static void Show(IWin32Window owner, string title, string text)
    {
        using var dialog = new UiDialog { Text = title, StartPosition = FormStartPosition.CenterParent,
            Font = UiTheme.Font(), BackColor = UiTheme.Page, ClientSize = new Size(640, 520),
            MinimumSize = new Size(420, 320), ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false,
            AutoScaleMode = AutoScaleMode.None };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(24) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var content = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Text = text, BorderStyle = BorderStyle.None, BackColor = UiTheme.Page, ForeColor = UiTheme.Ink };
        var close = UiTheme.Button("返回"); close.Anchor = AnchorStyles.Right; close.DialogResult = DialogResult.Cancel;
        layout.Controls.Add(content, 0, 0); layout.Controls.Add(close, 0, 1);
        dialog.CancelButton = close; dialog.FinishLayout(layout); dialog.ShowDialog(owner);
    }
}
