namespace Cet6WordMachine;

internal sealed partial class MainForm
{
    private static TableLayoutPanel EqualActions(params Control[] buttons)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = buttons.Length, RowCount = 1,
            Margin = Padding.Empty, BackColor = UiTheme.Page };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < buttons.Length; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / buttons.Length));
            buttons[i].Dock = DockStyle.Fill;
            buttons[i].Margin = new Padding(i == 0 ? 0 : 5, 6, i == buttons.Length - 1 ? 0 : 5, 0);
            row.Controls.Add(buttons[i], i, 0);
        }
        return row;
    }

    private TableLayoutPanel FocusLayout(WordEntry entry, string stage, out SurfaceTable card, out Panel body,
        bool phonetic = true)
    {
        var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = PageColor };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        card = new SurfaceTable { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(28, 18, 28, 22) };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        var hasPhonetic = phonetic && !string.IsNullOrWhiteSpace(entry.Phonetic);
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, hasPhonetic ? 32 : 0));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var caption = UiTheme.Label(stage, 10.5f); caption.Name = "CardStage"; caption.ForeColor = MutedColor;
        var word = UiTheme.Label(entry.Word, 38, true); word.Name = "CardWord"; word.TextAlign = ContentAlignment.MiddleCenter;
        var sound = UiTheme.Label(hasPhonetic ? "/" + entry.Phonetic.Trim('/') + "/" : "", 12);
        sound.Name = "CardPhonetic"; sound.Visible = hasPhonetic; sound.TextAlign = ContentAlignment.MiddleCenter; sound.ForeColor = MutedColor;
        body = new Panel { Name = "CardBody", Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 16, 0, 0) };
        card.Controls.Add(caption, 0, 0); card.Controls.Add(word, 0, 1); card.Controls.Add(sound, 0, 2); card.Controls.Add(body, 0, 3);
        root.Controls.Add(card, 0, 0);
        // Imported words can include spaces and hyphens, so fit every word label.
        UiLayout.FitWord(word);
        return root;
    }

    private static Label DetailLabel(string text, string name, bool emphasis = false)
    {
        var label = UiTheme.Label(text, emphasis ? 17 : 11.5f, emphasis);
        label.Name = name; label.TextAlign = ContentAlignment.TopLeft;
        label.Margin = new Padding(0, 0, 0, 18);
        if (!emphasis) label.ForeColor = MutedColor;
        label.Visible = !string.IsNullOrWhiteSpace(text);
        return label;
    }

    private static TableLayoutPanel ScrollDetails(Panel host, params Label[] labels)
    {
        var table = new TableLayoutPanel { ColumnCount = 1, RowCount = labels.Length,
            BackColor = Color.White, Padding = new Padding(8, 8, 8, 0), Margin = Padding.Empty };
        for (var i = 0; i < labels.Length; i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(labels[i], 0, i);
        }
        host.Controls.Add(table);
        UiLayout.ScrollTextTable(host, table);
        return table;
    }

    private void EditHint(WordEntry entry, Label hint)
    {
        var resumeTimer = _dayTimer.Enabled;
        _dayTimer.Stop();
        try
        {
            using var editor = new HintEditorForm(entry, GetHint(entry), _aiServices);
            if (editor.ShowDialog(this) != DialogResult.OK) return;
            if (GetToday() != _currentDate)
            {
                CheckForNewDay(); SetStatus("日期已变化，请在今日任务中重新编辑联想。", true); return;
            }
            if (!TrySaveChange(candidate =>
            {
                if (string.IsNullOrWhiteSpace(editor.HintText)) candidate.CustomHints.Remove(entry.Word);
                else candidate.CustomHints[entry.Word] = editor.HintText;
            })) return;
            hint.Text = GetHint(entry); hint.Visible = !string.IsNullOrWhiteSpace(hint.Text);
            SetStatus("联想已保存。", false);
        }
        finally { if (resumeTimer && !IsDisposed) _dayTimer.Start(); }
    }

    private void ShowStudyCard()
    {
        if (_session is null) return;
        if (_session.StudyIndex >= _session.Words.Count) { _session.StudyFinished = true; ShowQuiz(); return; }
        _pendingConfidence = -1;
        ClearContent();
        var word = _session.Words[_session.StudyIndex];
        var entry = _engine.GetWord(word);
        var isReview = _state.ProgressMap.TryGetValue(word, out var progress) && progress.IntroducedOn < _session.Date;
        var root = FocusLayout(entry, $"{(isReview ? "到期复习" : "今日新词")}  {_session.StudyIndex + 1} / {_session.Words.Count}", out _, out var body);
        var meaning = DetailLabel(entry.Meaning, "StudyMeaning", true);
        var hint = DetailLabel(GetHint(entry), "StudyHint");
        var example = DetailLabel(entry.Example, "StudyExample");
        ScrollDetails(body, meaning, hint, example);
        body.Visible = false;

        var unknown = UiTheme.Button("不认识"); unknown.Name = "StudyRating0";
        var unsure = UiTheme.Button("有点印象"); unsure.Name = "StudyRating1";
        var known = UiTheme.Button("认识"); known.Name = "StudyRating2";
        var ratings = EqualActions(unknown, unsure, known);
        var next = UiTheme.Button(_session.StudyIndex + 1 >= _session.Words.Count ? "进入今日检测" : "下一个", true);
        next.Name = "StudyNext"; next.Enabled = false;
        next.Click += (_, _) =>
        {
            if (_pendingConfidence < 0) return;
            if (!TrySaveChange(candidate => _engine.RateCard(candidate.ActiveSession!, entry.Word, _pendingConfidence))) return;
            ShowSessionStep();
        };
        var edit = UiTheme.Button("我的联想"); edit.Name = "EditStudyHint";
        edit.Click += (_, _) => EditHint(entry, hint);
        var skip = BuildSkipWordButton(entry.Word, ShowAfterWordSkipped); skip.Text = "跳过此词";
        var actions = EqualActions(edit, skip, next); actions.Visible = false;
        var actionHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        actionHost.Controls.Add(ratings); actionHost.Controls.Add(actions);
        root.Controls.Add(actionHost, 0, 1);
        void Reveal(int confidence)
        {
            _pendingConfidence = confidence;
            HideStatus();
            body.Visible = true; ratings.Visible = false; actions.Visible = true; actions.BringToFront();
            next.Enabled = true; next.Focus();
        }
        unknown.Click += (_, _) => Reveal(0); unsure.Click += (_, _) => Reveal(1); known.Click += (_, _) => Reveal(2);
        AddCenteredPage(root, "StudyPage", 760, 500, flexible: true);
    }

    private void ShowRecallCard(DailySession quiz, DailySession? retrySource)
    {
        var isRetry = retrySource is not null;
        _quizAnswered = false;
        var gradeRecorded = false;
        ClearContent();
        var entry = _engine.GetWord(_engine.GetQuizWord(quiz));
        var root = FocusLayout(entry, $"{(isRetry ? "错词再测" : "回忆检测")}  {quiz.QuizIndex + 1} / {quiz.Words.Count}", out _, out var body, phonetic: false);
        root.RowStyles[1].Height = 116;
        var input = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(8) };
        input.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); input.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); input.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var answerBox = new TextBox { Name = "RecallAnswer", Dock = DockStyle.Fill, Multiline = true,
            ScrollBars = ScrollBars.Vertical, Font = UiFont(13), BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "输入中文意思，大意也可以", AccessibleName = "中文意思", Margin = new Padding(8, 4, 8, 12) };
        var submit = UiTheme.Button("查看答案", true); submit.Name = "RevealRecallAnswer";
        var noIdea = UiTheme.Button("想不起来"); noIdea.Name = "RecallNoIdea";
        var inputTitle = UiTheme.Label("中文意思", 11); inputTitle.ForeColor = MutedColor; inputTitle.Margin = new Padding(8, 0, 0, 0);
        input.Controls.Add(inputTitle, 0, 0); input.Controls.Add(answerBox, 0, 1); input.Controls.Add(EqualActions(noIdea, submit), 0, 2);
        body.Controls.Add(input);
        var revealHost = new Panel { Name = "RecallReveal", Dock = DockStyle.Fill, BackColor = Color.White, Visible = false };
        var yourAnswer = DetailLabel("", "RecallYourAnswer");
        var correct = DetailLabel(entry.Meaning, "RecallMeaning", true);
        ScrollDetails(revealHost, correct, yourAnswer, DetailLabel(entry.Example, "RecallExample"));
        body.Controls.Add(revealHost);

        var gradeGood = UiTheme.Button("基本说对"); gradeGood.Name = "RecallGrade2";
        var gradePartial = UiTheme.Button("只想起一点"); gradePartial.Name = "RecallGrade1";
        var gradeBad = UiTheme.Button("完全忘了"); gradeBad.Name = "RecallGrade0";
        var grades = EqualActions(gradeBad, gradePartial, gradeGood); grades.Visible = false;
        gradeGood.Enabled = gradePartial.Enabled = gradeBad.Enabled = false;
        var next = UiTheme.Button(quiz.QuizIndex + 1 >= quiz.Words.Count ? (isRetry ? "返回今日结果" : "查看今日结果") : "下一题", true);
        next.Name = "RecallNext"; next.Enabled = false;
        var skip = BuildSkipWordButton(entry.Word, () =>
        {
            if (!isRetry) { ShowAfterWordSkipped(); return; }
            var removedIndex = quiz.Words.FindIndex(w => string.Equals(w, entry.Word, StringComparison.OrdinalIgnoreCase));
            if (removedIndex >= 0) { if (removedIndex < quiz.QuizIndex) quiz.QuizIndex--; quiz.Words.RemoveAt(removedIndex); }
            if (gradeRecorded && quiz.SelfGrades.TryGetValue(entry.Word, out var retryGrade))
            { if (retryGrade == 2) quiz.CorrectCount--; else if (retryGrade == 1) quiz.PartialCount--; }
            quiz.UserAnswers.Remove(entry.Word); quiz.SelfGrades.Remove(entry.Word);
            if (quiz.QuizIndex < quiz.Words.Count) ShowRecallCard(quiz, SavedSession(retrySource!));
            else ShowResults(SavedSession(retrySource!), newlyCompleted: false);
        });
        skip.Text = "跳过此词"; skip.Visible = false;
        var finish = UiTheme.Button("结束再测"); finish.Name = "FinishMistakeRetry";
        finish.Click += (_, _) => ShowResults(SavedSession(retrySource!), newlyCompleted: false);
        var nextRow = isRetry ? EqualActions(finish, skip, next) : EqualActions(skip, next);
        next.Visible = false;
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        actions.Controls.Add(grades, 0, 0); actions.Controls.Add(nextRow, 0, 1); root.Controls.Add(actions, 0, 1);

        void Reveal(bool forgotten)
        {
            if (_quizAnswered) return;
            var text = answerBox.Text.Trim();
            if (!forgotten && string.IsNullOrWhiteSpace(text))
            { SetStatus("请输入中文意思，或选择“想不起来”。", true); answerBox.Focus(); return; }
            HideStatus();
            _quizAnswered = true; answerBox.ReadOnly = true;
            yourAnswer.Text = forgotten ? "你的回答：想不起来" : "你的回答：" + text;
            yourAnswer.Visible = true;
            input.Visible = false; revealHost.Visible = true; revealHost.BringToFront();
            grades.Visible = true; skip.Visible = true; next.Visible = true;
            gradeGood.Enabled = gradePartial.Enabled = gradeBad.Enabled = true;
            if (forgotten) RecordGrade(0);
        }

        void RecordGrade(int grade)
        {
            if (!_quizAnswered || gradeRecorded) return;
            if (isRetry)
            {
                quiz.UserAnswers[entry.Word] = answerBox.Text.Trim(); quiz.SelfGrades[entry.Word] = grade;
                if (grade == 2) quiz.CorrectCount++; else if (grade == 1) quiz.PartialCount++;
                quiz.QuizIndex++;
            }
            else if (!TrySaveChange(candidate => _engine.SubmitSelfAssessment(candidate, candidate.ActiveSession!,
                         entry.Word, answerBox.Text, grade, _currentDate))) return;
            gradeRecorded = true;
            HideStatus();
            gradeGood.Enabled = gradePartial.Enabled = gradeBad.Enabled = false;
            (grade == 2 ? gradeGood : grade == 1 ? gradePartial : gradeBad).Text += " ✓";
            next.Enabled = true; next.Focus();
            if (!isRetry) RefreshMistakeWidget();
        }

        submit.Click += (_, _) => Reveal(false); noIdea.Click += (_, _) => Reveal(true);
        gradeGood.Click += (_, _) => RecordGrade(2); gradePartial.Click += (_, _) => RecordGrade(1); gradeBad.Click += (_, _) => RecordGrade(0);
        next.Click += (_, _) =>
        {
            if (!gradeRecorded) return;
            if (!isRetry) { ShowSessionStep(); return; }
            if (quiz.QuizIndex < quiz.Words.Count) ShowRecallCard(quiz, retrySource);
            else
            {
                ShowResults(SavedSession(retrySource!), newlyCompleted: false);
                SetStatus($"再测完成：基本说对 {quiz.CorrectCount} / {quiz.Words.Count} 词。", false);
            }
        };
        AddCenteredPage(root, "RecallPage", 760, 540, flexible: true);
        answerBox.Focus();
    }
}
