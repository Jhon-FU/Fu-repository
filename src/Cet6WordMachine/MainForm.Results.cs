namespace Cet6WordMachine;

internal sealed partial class MainForm
{
    private void ShowResults(DailySession session, bool newlyCompleted)
    {
        if (newlyCompleted)
        {
            _engine.CompleteSession(_state, session);
            StateStore.Save(_state);
        }
        ClearContent();
        _allowClose = true;
        var total = session.Words.Count;
        var reviewable = session.Words.Count(word => !_engine.IsSkipped(_state, word));
        var mistakes = session.Words.Count(word => !_engine.IsSkipped(_state, word) &&
            session.SelfGrades.TryGetValue(word, out var grade) && grade < 2);
        var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 5, BackColor = PageColor, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 64, 230, 24, 58, 58 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.Controls.Add(UiTheme.Label("今天的任务完成了", 23, true), 0, 0);
        var card = new SurfaceTable { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(28) };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); card.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2,
            BackColor = Color.White, Margin = new Padding(0, 0, 0, 24) };
        metrics.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var titles = new[] { "完成词数", "基本说对", "正确率" };
        var values = new[] { total.ToString(), session.CorrectCount.ToString(), total == 0 ? "—" : $"{(int)Math.Round(session.CorrectCount * 100.0 / total)}%" };
        for (var i = 0; i < 3; i++)
        {
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            var label = UiTheme.Label(titles[i], 11); label.ForeColor = MutedColor; label.TextAlign = ContentAlignment.MiddleCenter;
            var value = UiTheme.Label(values[i], 30); value.Name = "ResultMetric" + i; value.TextAlign = ContentAlignment.MiddleCenter;
            metrics.Controls.Add(label, i, 0); metrics.Controls.Add(value, i, 1);
        }
        card.Controls.Add(metrics, 0, 0);
        var finish = UiTheme.Button("结束今日学习", true); finish.Name = "FinishTodayButton"; finish.Dock = DockStyle.Fill; finish.Margin = Padding.Empty;
        finish.Click += (_, _) => Close(); card.Controls.Add(finish, 0, 1); root.Controls.Add(card, 0, 1);
        var review = UiTheme.Button("逐词复习"); review.Name = "ReviewTodayWords"; review.Enabled = reviewable > 0;
        review.Click += (_, _) => ShowWordReview(session, 0);
        var retry = UiTheme.Button($"再测错词 · {mistakes}"); retry.Name = "RetryMistakes"; retry.Enabled = mistakes > 0;
        retry.Click += (_, _) => ShowMistakeRetry(session);
        var records = UiTheme.Button("查看记录"); records.Name = "ViewTodayRecords";
        records.Click += (_, _) => ShowSessionRecords(session);
        root.Controls.Add(EqualActions(review, retry, records), 0, 3);
        var home = UiTheme.Button("返回首页"); home.Quiet = true; home.Anchor = AnchorStyles.Right; home.Name = "ResultsHome";
        home.Click += (_, _) => ShowHome(false); root.Controls.Add(home, 0, 4);
        AddCenteredPage(root, "ResultsPage", 680, 434);
        RefreshMistakeWidget(); RefreshChrome();
    }

    private void ShowSessionRecords(DailySession session)
    {
        var text = new System.Text.StringBuilder();
        if (session.Words.Count == 0) text.Append("今天没有需要检测的词。");
        foreach (var word in session.Words)
        {
            var entry = _engine.GetWord(word);
            var grade = session.SelfGrades.GetValueOrDefault(word, -1);
            text.AppendLine(entry.Word + (_engine.IsSkipped(_state, word) ? "（已跳过）" : ""));
            text.AppendLine("你的回答：" + (session.UserAnswers.TryGetValue(word, out var answer) && !string.IsNullOrWhiteSpace(answer) ? answer : "未作答"));
            text.AppendLine("标准含义：" + entry.Meaning);
            if (grade >= 0) text.AppendLine("自评：" + (grade == 2 ? "基本说对" : grade == 1 ? "只想起一点" : "完全忘了"));
            text.AppendLine();
        }
        UiInformation.Show(this, "今日检测记录", text.ToString());
    }

    private void ShowWordReview(DailySession session, int index)
    {
        session = SavedSession(session);
        var words = session.Words.Where(word => !_engine.IsSkipped(_state, word)).ToList();
        if (words.Count == 0) { ShowResults(session, false); return; }
        index = Math.Clamp(index, 0, words.Count - 1);
        var entry = _engine.GetWord(words[index]);
        ClearContent();
        var root = FocusLayout(entry, $"逐词复习  {index + 1} / {words.Count}", out _, out var body);
        var hint = DetailLabel(GetHint(entry), "ReviewHint");
        ScrollDetails(body, DetailLabel(entry.Meaning, "ReviewMeaning", true),
            hint, DetailLabel(entry.Example, "ReviewExample"));
        var edit = UiTheme.Button("我的联想"); edit.Name = "EditReviewHint";
        edit.Click += (_, _) => EditHint(entry, hint);
        var previous = UiTheme.Button("上一个"); previous.Name = "ReviewPrevious"; previous.Enabled = index > 0;
        previous.Click += (_, _) => ShowWordReview(session, index - 1);
        var next = UiTheme.Button("下一个", true); next.Name = "ReviewNext"; next.Enabled = index + 1 < words.Count;
        next.Click += (_, _) => ShowWordReview(session, index + 1);
        var back = UiTheme.Button("返回结果"); back.Name = "ReviewBack";
        back.Click += (_, _) => ShowResults(session, false);
        var skip = BuildSkipWordButton(entry.Word, () => ShowWordReview(SavedSession(session), index)); skip.Text = "跳过此词";
        root.Controls.Add(EqualActions(back, edit, skip, previous, next), 0, 1);
        AddCenteredPage(root, "WordReviewPage", 760, 500, flexible: true);
    }
}
