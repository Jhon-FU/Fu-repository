using System.Media;
using System.Runtime.InteropServices;

namespace Cet6WordMachine;

internal sealed partial class MainForm : Form
{
    private const int ScClose = 0xF060;
    private const uint MfByCommand = 0x00000000;

    private static readonly Color PageColor = UiTheme.Page;
    private static readonly Color InkColor = UiTheme.Ink;
    private static readonly Color MutedColor = UiTheme.Muted;
    private static readonly Color TealColor = UiTheme.Accent;
    private static readonly Color TealDarkColor = Color.FromArgb(9, 88, 82);
    private static readonly Color CoralColor = Color.FromArgb(202, 82, 55);
    private static readonly Color AmberColor = Color.FromArgb(192, 126, 24);
    private static readonly Color BorderColor = UiTheme.Line;
    private static readonly Dictionary<(float Size, FontStyle Style), Font> FontCache = [];

    private List<WordEntry> _bank;
    private StudyEngine _engine;
    private AppState _state;
#if TESTMODE
    private readonly bool _testMode;
#endif
    private readonly Panel _content;
    private readonly Label _statusLabel;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private readonly Panel _statusPanel;
    private readonly ToolTip _toolTips = new();
    private bool _persistentStatus;
    private Label _wordBankLabel = null!;
    private readonly StartupRegistration _startupRegistration;
    private readonly AiServices _aiServices;
    private ContextMenuStrip _applicationMenu = null!;
    private ToolStripMenuItem _enableStartupMenuItem = null!;
    private DateOnly _currentDate;
    private readonly System.Windows.Forms.Timer _dayTimer;
    private readonly MistakeWidgetForm _mistakeWidget;
    private int _layoutDpi;
    private TableLayoutPanel _shell = null!;
    private Control? _page;
    private bool _resizingPage;

    private DailySession? _session;
    private int _pendingConfidence;
    private bool _quizAnswered;
    private bool _allowClose;

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool revert);

    [DllImport("user32.dll")]
    private static extern bool DeleteMenu(IntPtr hMenu, uint position, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    public MainForm(StartupRegistration? startupRegistration = null, AiServices? aiServices = null)
    {
        _startupRegistration = startupRegistration ?? StartupRegistration.ForCurrentProcess();
        _aiServices = aiServices ?? AiServices.CreateDefault();
        _currentDate = GetToday();
        _state = StateStore.Load();
        _bank = StateStore.LoadWordBank(_state);
        _engine = new StudyEngine(_bank);
        _mistakeWidget = new MistakeWidgetForm();
#if TESTMODE
        _testMode = true;
#endif

#if TESTMODE
        Text = "单词机社区版 · 测试版";
#else
        Text = "单词机 · 社区版";
#endif
        BackColor = PageColor;
        ForeColor = InkColor;
        AutoScaleMode = AutoScaleMode.None;
        _layoutDpi = DeviceDpi;
        StartPosition = FormStartPosition.CenterScreen;
        var area = Screen.FromControl(this).WorkingArea;
        var gap = UiLayout.Pixels(this, 24);
        ClientSize = new Size(Math.Min(UiLayout.Pixels(this, 1040), area.Width - gap * 2),
            Math.Min(UiLayout.Pixels(this, 740), area.Height - gap * 2));
        MinimumSize = new Size(Math.Min(UiLayout.Pixels(this, 680), area.Width - gap * 2),
            Math.Min(UiLayout.Pixels(this, 480), area.Height - gap * 2));
        MaximizeBox = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = true;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = PageColor,
        };
        _shell = shell;
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));

        var header = BuildHeader();
        _content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Padding.Empty,
            BackColor = PageColor,
            AutoScroll = true,
        };
        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Soft,
        };
        _statusPanel = footer;
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 16, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UiFont(10.5f),
            ForeColor = UiTheme.Ink,
            Text = "",
        };
        footer.Controls.Add(_statusLabel);

        shell.Controls.Add(header, 0, 0);
        shell.Controls.Add(_content, 0, 1);
        shell.Controls.Add(footer, 0, 2);
        UiLayout.Scale(shell, _layoutDpi / 96f);
        Controls.Add(shell);
        _content.SizeChanged += (_, _) => ResizePage();
        _statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _statusTimer.Tick += (_, _) => HideStatus();

        _dayTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _dayTimer.Tick += (_, _) => CheckForNewDay();

        Shown += (_, _) =>
        {
            RefreshChrome();
            PrepareInitialView();
            RefreshMistakeWidget();
            _dayTimer.Start();
            if (!_state.HasChosenWordBank) BeginInvoke(new Action(ShowWordBankManager));
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var menu = GetSystemMenu(Handle, false);
        if (menu != IntPtr.Zero)
        {
            DeleteMenu(menu, ScClose, MfByCommand);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dayTimer?.Dispose();
            _mistakeWidget?.Dispose();
            _applicationMenu?.Dispose();
            _statusTimer?.Dispose();
            _toolTips.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message message)
    {
        const int wmNcHitTest = 0x84;
        base.WndProc(ref message);
        if (message.Msg != wmNcHitTest || message.Result != (IntPtr)1) return;
        var point = PointToClient(new Point(unchecked((short)(long)message.LParam),
            unchecked((short)((long)message.LParam >> 16))));
        var border = UiLayout.Pixels(this, 7);
        var left = point.X < border;
        var right = point.X >= ClientSize.Width - border;
        var top = point.Y < border;
        var bottom = point.Y >= ClientSize.Height - border;
        message.Result = (IntPtr)(top ? (left ? 13 : right ? 14 : 12)
            : bottom ? (left ? 16 : right ? 17 : 15) : left ? 10 : right ? 11 : 1);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (_shell is null) return;
        UiLayout.Scale(_shell, e.DeviceDpiNew / (float)_layoutDpi);
        _layoutDpi = e.DeviceDpiNew;
        ResizePage();
    }

    private void AddPage(Control page)
    {
        UiLayout.Scale(page, _layoutDpi / 96f);
        page.Dock = DockStyle.None;
        _content.AutoScroll = page.Name != "SkippedWordsPage";
        _content.AutoScrollMinSize = Size.Empty;
        _page = page;
        _content.Controls.Add(page);
        ResizePage();
    }

    private void ResizePage()
    {
        if (_page is null || _page.IsDisposed || _resizingPage) return;
        _resizingPage = true;
        try
        {
            var isList = _page.Name == "SkippedWordsPage";
            var x = UiLayout.Pixels(this, 28); var y = UiLayout.Pixels(this, 24);
            var minimumHeight = UiLayout.Pixels(this, _page.Name is "HomePage" or "ResultsPage" ? 434 : 360);
            var height = Math.Max(isList ? 100 : minimumHeight, _content.Height - y * 2);
            var scrolling = !isList && height > _content.Height - y * 2;
            var width = Math.Max(100, _content.Width - x * 2 - (scrolling ? SystemInformation.VerticalScrollBarWidth : 0));
            _content.AutoScroll = scrolling;
            _content.AutoScrollMinSize = new Size(0, scrolling ? height + y * 2 : 0);
            _page.Bounds = new Rectangle(x, y + (isList ? 0 : _content.AutoScrollPosition.Y), width, height);
        }
        finally { _resizingPage = false; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var emergencyClose =
            e.CloseReason is CloseReason.WindowsShutDown
                or CloseReason.TaskManagerClosing;
        if (!_allowClose && !emergencyClose)
        {
            e.Cancel = true;
            SystemSounds.Hand.Play();
            SetStatus("请先完成今日检测。紧急情况下可在任务管理器结束进程。", isWarning: true);
        }

        if (_allowClose || emergencyClose)
        {
            StateStore.Save(_state);
            _mistakeWidget.CloseForApplication();
        }

        base.OnFormClosing(e);
    }

    private void BeginWindowDrag(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
    }

    private void PrepareInitialView()
    {
        if (_state.ActiveSession is { Completed: false })
        {
            _session = _engine.GetOrCreateSession(_state, _currentDate);
            StateStore.Save(_state);
            ShowHome(hasUnfinishedSession: true);
            return;
        }

        var completedToday = _state.History.LastOrDefault(
            item => item.Date == _currentDate && item.Completed);
        if (completedToday is not null)
        {
            _session = _engine.GetOrCreateSession(_state, _currentDate);
            StateStore.Save(_state);
            if (!_session.Completed)
            {
                ShowHome(hasUnfinishedSession: true);
                SetStatus("已补齐今天漏排的复习和新词，之前的答案与记录已保留。", isWarning: false);
                return;
            }
            _allowClose = true;
            ShowResults(_session, newlyCompleted: false);
            return;
        }

        var due = _engine.DueCount(_state, _currentDate);
        var unlearned = _engine.UnlearnedCount(_state);
        if (due == 0 && unlearned == 0)
        {
            _session = _engine.GetOrCreateSession(_state, _currentDate);
            StateStore.Save(_state);
            _allowClose = true;
            ShowResults(_session, newlyCompleted: true);
            return;
        }

        ShowHome(hasUnfinishedSession: false);
    }

    private void ShowSkippedWords()
    {
        ClearContent();
        _content.AutoScrollPosition = Point.Empty;
        var words = _state.SkippedWords.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(word => word, StringComparer.OrdinalIgnoreCase).ToList();
        var root = new TableLayoutPanel
        {
            Name = "SkippedWordsPage", Dock = DockStyle.Fill,
            ColumnCount = 1, RowCount = 3, BackColor = PageColor,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        var title = new Label
        {
            Dock = DockStyle.Fill, Font = UiFont(22, FontStyle.Bold), ForeColor = InkColor,
            Text = $"已跳过词 · {words.Count}",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        root.Controls.Add(title, 0, 0);
        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, BackColor = Color.White,
            Padding = new Padding(12),
        };
        if (words.Count == 0)
            list.Controls.Add(new Label
            {
                AutoSize = true, Text = "还没有跳过的词。",
                Font = UiFont(11), ForeColor = MutedColor, Margin = new Padding(12),
            });
        foreach (var word in words)
        {
            var entry = _bank.FirstOrDefault(w => string.Equals(w.Word, word, StringComparison.OrdinalIgnoreCase));
            var wasLearned = _state.ProgressMap.ContainsKey(word);
            var row = new TableLayoutPanel
            {
                ColumnCount = 3, RowCount = 1, Height = 100,
                BackColor = UiTheme.Soft,
                Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0, 0, 0, 8),
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, Text = word, Font = UiFont(12, FontStyle.Bold),
                ForeColor = TealDarkColor, TextAlign = ContentAlignment.MiddleLeft,
            }, 0, 0);
            row.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, Font = UiFont(11.5f), ForeColor = InkColor,
                Text = entry?.Meaning ?? "该词暂不在当前词库",
                TextAlign = ContentAlignment.MiddleLeft,
            }, 1, 0);
            var restore = ActionButton("恢复", TealColor, TealDarkColor);
            restore.Name = "RestoreSkippedWord";
            restore.Tag = word;
            restore.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            restore.Height = 44;
            restore.Margin = new Padding(8, 0, 0, 0);
            restore.Click += (_, _) =>
            {
                if (!TrySaveChange(candidate => _engine.RestoreWord(candidate, word, GetToday()))) return;
                ShowSkippedWords();
                RefreshChrome();
                RefreshMistakeWidget();
                SetStatus($"已恢复 {word}，会在后续排任务时安排{(wasLearned ? "复习" : "学习")}；正在进行的任务保持不变。", isWarning: false);
            };
            row.Controls.Add(restore, 2, 0);
            list.Controls.Add(row);
        }
        var resizingRows = false;
        void ResizeRows()
        {
            if (resizingRows || list.IsDisposed) return;
            resizingRows = true;
            try
            {
                list.AutoScroll = false;
                list.AutoScrollMinSize = Size.Empty;
                var width = Math.Max(UiLayout.Pixels(this, 300), list.ClientSize.Width - list.Padding.Horizontal
                    - SystemInformation.VerticalScrollBarWidth - UiLayout.Pixels(this, 8));
                foreach (var row in list.Controls.OfType<TableLayoutPanel>())
                {
                    row.Width = width;
                    var available = width - row.Padding.Horizontal - (int)row.ColumnStyles[2].Width;
                    var labels = row.Controls.OfType<Label>();
                    var height = labels.Max(label => UiLayout.TextHeight(label,
                        (int)(available * row.ColumnStyles[row.GetColumn(label)].Width / 100) - label.Margin.Horizontal)
                        + label.Margin.Vertical);
                    row.Height = Math.Max(UiLayout.Pixels(this, 90), height + row.Padding.Vertical + UiLayout.Pixels(this, 8));
                    row.PerformLayout();
                }
                list.AutoScroll = true;
                list.PerformLayout();
            }
            finally { resizingRows = false; }
        }
        list.SizeChanged += (_, _) => ResizeRows();
        root.Controls.Add(list, 0, 1);
        var back = ActionButton("返回首页", TealColor, TealDarkColor);
        back.Name = "BackFromSkippedWords";
        back.Size = new Size(190, 46);
        back.Margin = new Padding(0, 8, 0, 0);
        back.Click += (_, _) => ShowHome(hasUnfinishedSession: _state.ActiveSession is { Completed: false });
        root.Controls.Add(back, 0, 2);
        AddPage(root);
        ResizeRows();
    }

    private void ShowWordBankManager()
    {
        _dayTimer.Stop();
        try
        {
            using var manager = new WordBankManagerForm(_state);
            if (manager.ShowDialog(this) != DialogResult.OK || manager.SelectedBank is null) return;
            var candidate = WordBankCatalog.Switch(_state, manager.SelectedBank, manager.ReuseMatchingProgress);
            // Persist all bank snapshots together before replacing the visible
            // account. A failed write leaves the current task and cards intact.
            StateStore.Save(candidate);
            _state = candidate;
            _bank = StateStore.LoadWordBank(_state);
            _engine = new StudyEngine(_bank);
            _session = _state.ActiveSession;
            _currentDate = GetToday();
            _allowClose = _engine.HasCompletedSession(_state, _currentDate);
            RefreshChrome();
            RefreshMistakeWidget();
            ShowHome(hasUnfinishedSession: _session is { Completed: false });
            SetStatus($"当前词库：{_state.ActiveWordBankName}（{_bank.Count:N0} 词）。各词库任务与进度已分别保存。", isWarning: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or InvalidDataException)
        {
            SetStatus("词库切换未完成，当前任务保留，可重试：" + ex.Message, isWarning: true);
        }
        finally { _dayTimer.Start(); }
    }

    private bool TrySaveChange(Action<AppState> change)
    {
        // Work on a copy so a failed write cannot advance a cursor or count a grade.
        var candidate = StateStore.CopyForUpdate(_state);
        change(candidate);
        try
        {
            StateStore.Save(candidate);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("保存失败，本次操作尚未生效。请检查磁盘空间或文件占用，然后再次点击刚才的按钮重试。", isWarning: true);
            return false;
        }

        _state = candidate;
        _session = candidate.ActiveSession;
        HideStatus();
        return true;
    }

    private Button BuildSkipWordButton(string word, Action afterSaved)
    {
        var button = ActionButton("已掌握，跳过此词", Color.White, MutedColor);
        button.Name = "PermanentSkipButton";
        button.ForeColor = TealDarkColor;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = BorderColor;
        button.Size = new Size(210, 46);
        button.Click += (_, _) =>
        {
            var resumeTimer = _dayTimer.Enabled;
            _dayTimer.Stop();
            try
            {
                using var confirm = new SkipWordConfirmForm(word);
                if (confirm.ShowDialog(this) != DialogResult.OK) return;
                if (GetToday() != _currentDate)
                {
                    CheckForNewDay();
                    SetStatus("日期已变化，尚未跳过该词；请在当前页面重新操作。", isWarning: true);
                    return;
                }
                var oldWords = _state.ActiveSession?.Words.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
                var wasInActiveTask = oldWords.Contains(word);
                var hadSubmittedGrade = _state.ActiveSession?.SelfGrades.ContainsKey(word) == true;
                if (!TrySaveChange(candidate => _engine.SkipWord(candidate, word, _currentDate))) return;
                var replacement = _state.ActiveSession?.Words.FirstOrDefault(w => !oldWords.Contains(w));
                afterSaved();
                RefreshChrome();
                RefreshMistakeWidget();
                SetStatus(replacement is not null
                    ? $"已跳过 {word}，{(hadSubmittedGrade ? "原评分保留；" : "")}已补入后续新词；可在菜单“已跳过词”恢复。"
                    : wasInActiveTask
                        ? $"已跳过 {word}；后续未学词已用尽，没有可补入的词。可在菜单恢复。"
                        : $"已跳过 {word}，以后不再安排复习；可在菜单“已跳过词”恢复。",
                    isWarning: false);
            }
            finally
            {
                if (resumeTimer && !IsDisposed) _dayTimer.Start();
            }
        };
        return button;
    }

    private DailySession SavedSession(DailySession source) =>
        _state.History.LastOrDefault(s => s.Date == source.Date && s.CompletedAt == source.CompletedAt) ?? source;

    private void ShowAfterWordSkipped()
    {
        if (_session is not null) ShowSessionStep();
        else if (_state.History.LastOrDefault(s => s.Date == _currentDate && s.Completed) is { } finished)
            ShowResults(finished, newlyCompleted: false);
        else ShowHome(hasUnfinishedSession: false);
    }

    private void StartSession()
    {
        _session = _engine.GetOrCreateSession(_state, _currentDate);
        if (_session.Completed)
        {
            _state.ActiveSession = null;
            StateStore.Save(_state);
            _allowClose = true;
            ShowResults(_session, newlyCompleted: true);
            return;
        }

        _state.ActiveSession = _session;
        StateStore.Save(_state);
        ShowSessionStep();
    }

    private void ShowSessionStep()
    {
        if (_session is null)
        {
            return;
        }

        var step = _engine.GetSessionStep(_session);
        if (step == SessionStep.Complete)
        {
            ShowResults(_session, newlyCompleted: !_session.Completed);
        }
        else if (step == SessionStep.NewStudy)
        {
            ShowStudyCard();
        }
        else
        {
            ShowQuiz();
        }
    }

    private void ShowQuiz()
    {
        if (_session is null)
        {
            return;
        }

        var step = _engine.GetSessionStep(_session);
        if (step == SessionStep.NewStudy)
        {
            ShowStudyCard();
            return;
        }

        if (step == SessionStep.Complete)
        {
            ShowResults(_session, newlyCompleted: true);
            return;
        }

        ShowRecallCard(_session, retrySource: null);
    }

    private void ShowMistakeRetry(DailySession source)
    {
        var words = source.Words
            .Where(word => !_engine.IsSkipped(_state, word))
            .Where(word => source.SelfGrades.TryGetValue(word, out var grade) && grade < 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (words.Count == 0) return;

        // Same-day practice is deliberately separate from the saved assessment.
        // Remembering an answer just shown must not postpone tomorrow's review.
        var retry = new DailySession { Date = source.Date, Words = words, StudyFinished = true };
        ShowRecallCard(retry, source);
        SetStatus("错词再测是额外练习，不改变今日正式成绩和已安排的复习日期。", isWarning: false);
    }

    private void ShowLegacyQuiz()
    {
        if (_session is null)
        {
            return;
        }

        if (_session.QuizIndex >= _session.Words.Count)
        {
            _state.ActiveSession = null;
            StateStore.Save(_state);
            _allowClose = true;
            ShowResults(_session, newlyCompleted: true);
            return;
        }

        _quizAnswered = false;
        ClearContent();
        var word = _session.Words[_session.QuizIndex];
        var entry = _engine.GetWord(word);
        var options = _engine.BuildOptions(_session, word);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = PageColor,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.White,
            Padding = new Padding(34, 20, 34, 18),
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var stage = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = $"识别检测  {_session.QuizIndex + 1} / {_session.Words.Count}",
            Font = UiFont(10, FontStyle.Bold),
            ForeColor = CoralColor,
        };
        var wordLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = entry.Word,
            Font = UiFont(32, FontStyle.Bold),
            ForeColor = InkColor,
        };
        var prompt = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = $"选出最接近的中文含义  /{entry.Phonetic}/",
            Font = UiFont(10),
            ForeColor = MutedColor,
        };
        var optionsGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(18, 4, 18, 4),
        };
        optionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        optionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        optionsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        optionsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var optionCards = new List<OptionCard>();
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var optionCard = new OptionCard(option);
            optionCards.Add(optionCard);
            optionsGrid.Controls.Add(optionCard, index % 2, index / 2);
        }
        card.Controls.Add(stage, 0, 0);
        card.Controls.Add(wordLabel, 0, 1);
        card.Controls.Add(prompt, 0, 2);
        card.Controls.Add(optionsGrid, 0, 3);

        var feedback = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White,
            Padding = new Padding(30, 6, 30, 6),
            Visible = false,
        };
        feedback.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        feedback.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var feedbackLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UiFont(14, FontStyle.Bold),
            ForeColor = TealColor,
        };
        var correctMeaning = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Font = UiFont(10.5f),
            ForeColor = InkColor,
            Text = entry.Meaning,
        };
        feedback.Controls.Add(feedbackLabel, 0, 0);
        feedback.Controls.Add(correctMeaning, 0, 1);

        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = PageColor,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 6),
            Margin = new Padding(0),
        };
        var nextButton = ActionButton(
            _session.QuizIndex + 1 >= _session.Words.Count ? "查看今日结果" : "下一题",
            TealColor,
            TealDarkColor);
        nextButton.Size = new Size(220, 48);
        nextButton.Enabled = false;
        nextButton.Click += (_, _) =>
        {
            if (_session.QuizIndex >= _session.Words.Count)
            {
                _state.ActiveSession = null;
                StateStore.Save(_state);
                _allowClose = true;
                ShowResults(_session, newlyCompleted: true);
            }
            else
            {
                StateStore.Save(_state);
                ShowQuiz();
            }
        };
        actionRow.Controls.Add(nextButton);
        foreach (var optionCard in optionCards)
        {
            optionCard.Click += (_, _) => AnswerQuiz(optionCard, optionCard.Option);
            optionCard.TextLabel.Click += (_, _) => AnswerQuiz(optionCard, optionCard.Option);
        }

        root.Controls.Add(card, 0, 0);
        root.Controls.Add(feedback, 0, 1);
        root.Controls.Add(actionRow, 0, 2);
        AddPage(root);

        void AnswerQuiz(OptionCard selectedCard, string option)
        {
            if (_quizAnswered)
            {
                return;
            }

            _quizAnswered = true;
            var correctOption = StudyEngine.FormatOptionMeaning(entry.Meaning);
            var wasCorrect = _engine.SubmitAnswer(
                _state,
                _session,
                entry.Word,
                option,
                _currentDate);
            StateStore.Save(_state);

            foreach (var optionCard in optionCards)
            {
                optionCard.Cursor = Cursors.Default;
                if (optionCard.Option == correctOption)
                {
                    optionCard.SetState(Color.FromArgb(220, 241, 235), Color.FromArgb(220, 241, 235));
                }
            }

            if (!wasCorrect)
            {
                selectedCard.SetState(Color.FromArgb(251, 229, 222), Color.FromArgb(251, 229, 222));
            }

            feedbackLabel.Text = wasCorrect
                ? "正确"
                : $"答错了，正确含义：{correctOption}";
            feedbackLabel.ForeColor = wasCorrect ? TealColor : CoralColor;
            correctMeaning.Text = $"{entry.Meaning}     {entry.Hint}";
            feedback.Visible = true;
            nextButton.Enabled = true;
            SetStatus(
                wasCorrect ? "已记录，按遗忘曲线安排下次复习。" : "答错的词已安排明天重新复习。",
                isWarning: !wasCorrect);
        }
    }

    private void ClearContent()
    {
        if (!_persistentStatus) HideStatus();
        _page = null;
        _content.AutoScrollPosition = Point.Empty;
        _content.SuspendLayout();
        var controls = _content.Controls.Cast<Control>().ToArray();
        _content.Controls.Clear();
        foreach (var control in controls)
        {
            control.Dispose();
        }
        _content.ResumeLayout();

        if (string.Equals(
                Environment.GetEnvironmentVariable("WORD_MACHINE_COMMUNITY_GC_ON_CLEAR"),
                "1",
                StringComparison.Ordinal))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private void RefreshChrome()
    {
        _wordBankLabel.Text = _state.ActiveWordBankName;
        _toolTips.SetToolTip(_wordBankLabel, _state.ActiveWordBankName);
    }

    private void CheckForNewDay()
    {
        var today = GetToday();
        if (today == _currentDate)
        {
            return;
        }

        // Prepare and persist the whole transition before changing the live date,
        // session or page. A failed save must leave yesterday's controls attached
        // to yesterday's state, and the unchanged date lets the timer retry.
        DailySession? nextSession = null;
        if (!TrySaveChange(candidate =>
        {
            if (candidate.ActiveSession is { Completed: false } ||
                candidate.History.Any(item => item.Date == today && item.Completed) ||
                (_engine.DueCount(candidate, today) == 0 && _engine.UnlearnedCount(candidate) == 0))
            {
                nextSession = _engine.GetOrCreateSession(candidate, today);
            }
        }))
        {
            SetStatus("跨天任务保存失败，当前学习进度保持不变；将在 30 秒后自动重试。请检查磁盘空间或文件占用。",
                isWarning: true);
            return;
        }

        _currentDate = today;
        _session = nextSession;
        _allowClose = nextSession?.Completed == true;
        _mistakeWidget.UpdateItems([], MistakeWidgetContext);
        RefreshChrome();
        // The new state is already saved. Render it without a second save that
        // could fail after the live date has advanced but before the page changes.
        if (nextSession is { Completed: true })
        {
            ShowResults(nextSession, newlyCompleted: false);
        }
        else
        {
            ShowHome(hasUnfinishedSession: nextSession is not null);
        }
        SetStatus("日期已经更新，正在生成今天的新词与到期复习。", isWarning: false);
    }

#if TESTMODE
    private void JumpToNextDay()
    {
        var session = _session ??
                      _state.ActiveSession ??
                      _state.History.LastOrDefault(item => item.Date == _currentDate) ??
                      _engine.GetOrCreateSession(_state, _currentDate);

        if (!session.Completed)
        {
            _engine.CompleteDayAsAllCorrect(_state, session, _currentDate);
        }

        _currentDate = _currentDate.AddDays(1);
        Environment.SetEnvironmentVariable(
            "WORD_MACHINE_COMMUNITY_TODAY",
            _currentDate.ToString("yyyy-MM-dd"));
        _state.ActiveSession = null;
        _session = null;
        _allowClose = false;
        _mistakeWidget.UpdateItems([], MistakeWidgetContext);
        StateStore.Save(_state);
        RefreshChrome();
        PrepareInitialView();
        SetStatus(
            $"已跳到 {_currentDate:yyyy-MM-dd}，并默认昨天全部答对。",
            isWarning: false);
    }
#endif

    private static DateOnly GetToday()
    {
        var overrideValue = Environment.GetEnvironmentVariable(
            "WORD_MACHINE_COMMUNITY_TODAY");
        return DateOnly.TryParse(overrideValue, out var simulated)
            ? simulated
            : DateOnly.FromDateTime(DateTime.Today);
    }

    private void RefreshMistakeWidget()
    {
        DailySession? session = null;
        if (_session?.Date == _currentDate)
        {
            session = _session;
        }
        else if (_state.ActiveSession?.Date == _currentDate)
        {
            session = _state.ActiveSession;
        }
        else
        {
            session = _state.History.LastOrDefault(item => item.Date == _currentDate);
        }

        if (session is null)
        {
            _mistakeWidget.UpdateItems([], MistakeWidgetContext);
            return;
        }

        var items = session.Words
            .Where(word => !_engine.IsSkipped(_state, word) &&
                session.SelfGrades.TryGetValue(word, out var grade) &&
                grade < 2)
            .Select(word =>
            {
                var entry = _engine.GetWord(word);
                return new MistakeItem(
                    word,
                    entry.Phonetic,
                    entry.Meaning,
                    session.UserAnswers.GetValueOrDefault(word, ""),
                    session.SelfGrades.GetValueOrDefault(word, 0));
            })
            .ToList();
        _mistakeWidget.UpdateItems(items, MistakeWidgetContext);
    }

    private string MistakeWidgetContext => $"{_state.ActiveWordBankId}\n{_currentDate:yyyy-MM-dd}";

    private string GetHint(WordEntry entry) =>
        _state.CustomHints.TryGetValue(entry.Word, out var customHint)
            ? customHint
            : entry.Hint;

    private void RefreshStartupMenu()
    {
        var status = _startupRegistration.GetStatus();
        _enableStartupMenuItem.Text = status.MenuText;
        _enableStartupMenuItem.Enabled = status.CanEnable;
        _enableStartupMenuItem.ToolTipText = status.Detail;
    }

    private static Button ActionButton(string text, Color backColor, Color hoverColor) =>
        UiTheme.Button(text, backColor.ToArgb() == TealColor.ToArgb());

    private sealed class OptionCard : Panel
    {
        private readonly Color _normalColor = Color.FromArgb(248, 250, 251);
        private readonly Color _hoverColor = Color.FromArgb(235, 242, 244);

        public OptionCard(string option)
        {
            Option = option;
            Dock = DockStyle.Fill;
            Margin = new Padding(4);
            BackColor = _normalColor;
            BorderStyle = BorderStyle.FixedSingle;
            Cursor = Cursors.Hand;
            Padding = new Padding(6, 2, 6, 2);

            TextLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = option,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = UiFont(9.5f),
                ForeColor = InkColor,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                UseCompatibleTextRendering = true,
            };
            Controls.Add(TextLabel);

            MouseEnter += (_, _) => BackColor = _hoverColor;
            MouseLeave += (_, _) => BackColor = _normalColor;
            TextLabel.MouseEnter += (_, _) =>
            {
                BackColor = _hoverColor;
                TextLabel.BackColor = _hoverColor;
            };
            TextLabel.MouseLeave += (_, _) =>
            {
                BackColor = _normalColor;
                TextLabel.BackColor = Color.Transparent;
            };
        }

        public string Option { get; }

        public Label TextLabel { get; }

        public void SetState(Color background, Color border)
        {
            BackColor = background;
            TextLabel.BackColor = background;
            BorderStyle = BorderStyle.FixedSingle;
        }
    }

    private static Font UiFont(float size, FontStyle style = FontStyle.Regular)
    {
        var key = (size, style);
        if (!FontCache.TryGetValue(key, out var font))
        {
            font = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
            FontCache[key] = font;
        }

        return font;
    }
}
