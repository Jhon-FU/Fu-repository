using System.Reflection;
using Cet6WordMachine;

internal static partial class Program
{
    private static void Invoke(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static T Control<T>(Control form, string name) where T : Control => (T)All(form).Single(c => c.Name == name);
    private static void Click(Control form, string name) { Control<Button>(form, name).PerformClick(); Application.DoEvents(); }
    private static void VisibleIn(Form form, string name)
    {
        var control = All(form).Single(c => c.Name == name);
        Check(control.Visible && form.RectangleToScreen(form.ClientRectangle).Contains(control.RectangleToScreen(control.ClientRectangle)),
            $"Visible action {name}: form={form.ClientRectangle}, control={control.Bounds}");
    }
    private static void DumpLayout(Form form, string name) => File.WriteAllLines(Path.Combine(_root, name + "-layout.txt"),
        All(form).Select(c => $"{c.GetType().Name} [{c.Name}] '{c.Text.Replace("\r", "").Replace("\n", " ")}' visible={c.Visible} bounds={c.Bounds} client={c.ClientSize} dpi={c.DeviceDpi}"));

    private static void TestRedesignedUi()
    {
        var directory = Environment.GetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR");
        Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR", Path.Combine(_root, "design-account"));
        try
        {
            var bank = Package("ui-fixture", ("adapt", "适应；调整"), ("benefit", "益处；使受益"), ("calm", "平静的"));
            bank.Words[0].Phonetic = "əˈdæpt";
            var state = State(bank); state.DailyNewWords = 2; StateStore.Save(state);
            TestPlanDialog(state);
            using var main = new MainForm(new StartupRegistration(new MemoryStartupEntryStore(), Environment.ProcessPath, true, blocked: true));
            ShowOffscreen(main);
            Invoke(main, "HideStatus");
            Check(!All(main).Any(c => c is NumericUpDown or DateTimePicker), "Home contains no plan input fields");
            Check(Control<Label>(main, "TodayNewCount").Text == "2" && Control<Label>(main, "TodayReviewCount").Text == "0", "Home task counts use real state");
            DumpLayout(main, "design-home"); Screenshot(main, "design-home");
            VisibleIn(main, "StartLearningButton");
            VisibleIn(main, "AiSettingsHomeButton");
            VisibleIn(main, "ApplicationTitle"); VisibleIn(main, "CurrentWordBankLabel");
            var wide = main.ClientSize;
            main.ClientSize = new Size(Math.Max(UiLayout.Pixels(main, 680), main.MinimumSize.Width), Math.Max(UiLayout.Pixels(main, 560), main.MinimumSize.Height));
            Application.DoEvents(); DumpLayout(main, "design-home-narrow"); Screenshot(main, "design-home-narrow");
            Check(!Field<Panel>(main, "_content").HorizontalScroll.Visible, "Narrow home does not scroll horizontally");
            VisibleIn(main, "LearningPlanButton");
            VisibleIn(main, "AiSettingsHomeButton");
            var aiButton = Control<Button>(main, "AiSettingsHomeButton");
            Check(TextRenderer.MeasureText(aiButton.Text, aiButton.Font).Width < aiButton.Width - UiLayout.Pixels(main, 16), "Home AI label fits at narrow width");
            main.ClientSize = wide;
            main.Close(); Application.DoEvents();
            Check(!main.IsDisposed && Field<bool>(main, "_persistentStatus"), "Ordinary close is blocked before completing tasks");
            Check(!Field<System.Windows.Forms.Timer>(main, "_statusTimer").Enabled, "Failure notice does not auto-hide");
            Invoke(main, "SetStatus", "已保存。", false);
            Check(Field<System.Windows.Forms.Timer>(main, "_statusTimer").Enabled, "Success notice expires automatically");
            Invoke(main, "HideStatus");
            Check(!Field<Panel>(main, "_statusPanel").Visible, "No permanent footer after notice is cleared");
            Click(main, "StartLearningButton");
            Check(!Control<Label>(main, "StudyMeaning").Visible, "Study initially hides meaning");
            Check(!Control<Button>(main, "StudyNext").Enabled, "Cannot advance without confidence choice");
            var firstWord = Control<Label>(main, "CardWord").Text;
            Screenshot(main, "design-study");
            Click(main, "StudyRating0");
            Check(Control<Label>(main, "CardWord").Visible && Control<Label>(main, "StudyMeaning").Visible, "Reveal retains word while showing meaning");
            Check(!Control<Label>(main, "StudyHint").Visible && !Control<Label>(main, "StudyExample").Visible, "Absent hint and example do not create empty sections");
            Check(Field<AppState>(main, "_state").ActiveSession!.StudyIndex == 0, "Confidence is committed only on Next");
            Screenshot(main, "design-reveal");
            main.BeginInvoke(new Action(() =>
            {
                var editor = Application.OpenForms.OfType<HintEditorForm>().Single();
                Control<TextBox>(editor, "HintInput").Text = "我自己的联想";
                Screenshot(editor, "design-hint"); Click(editor, "SaveHint");
            }));
            Click(main, "EditStudyHint");
            Check(StateStore.Load().CustomHints[firstWord] == "我自己的联想" && Control<Label>(main, "StudyHint").Visible, "Hint save persists and reveals custom text");
            using (var locked = new FileStream(StateStore.StatePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Click(main, "StudyNext");
                Check(Field<AppState>(main, "_state").ActiveSession!.StudyIndex == 0, "Failed study save preserves current card");
            }
            Click(main, "StudyNext");
            Check(Field<AppState>(main, "_state").ActiveSession!.Confidence[firstWord] == 0, "Retried save preserves chosen confidence");
            Click(main, "StudyRating2"); Click(main, "StudyNext");
            Check(All(main).Any(c => c.Name == "RecallAnswer"), "Finishing study opens recall");
            Screenshot(main, "design-recall");
            Click(main, "RevealRecallAnswer");
            Check(!Field<bool>(main, "_quizAnswered") && Field<bool>(main, "_persistentStatus"), "Blank recall cannot be submitted");
            Control<TextBox>(main, "RecallAnswer").Text = "我的回答";
            Click(main, "RevealRecallAnswer");
            Check(Control<Label>(main, "RecallMeaning").Visible && !Control<TextBox>(main, "RecallAnswer").Visible, "Recall reveals meaning after answer");
            using (var locked = new FileStream(StateStore.StatePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Click(main, "RecallGrade1");
                Check(Field<AppState>(main, "_state").ActiveSession!.QuizIndex == 0 && Control<Button>(main, "RecallGrade1").Enabled, "Failed grade save remains retryable");
            }
            Click(main, "RecallGrade1");
            var graded = Field<AppState>(main, "_state").ActiveSession!;
            Check(graded.QuizIndex == 1 && graded.UserAnswers.Values.Single() == "我的回答" && graded.PartialCount == 1, "Self grade and raw answer persist");
            Click(main, "RecallGrade1");
            Check(Field<AppState>(main, "_state").ActiveSession!.QuizIndex == 1, "Repeated grade cannot double count");
            Screenshot(main, "design-graded");
            Screenshot(Field<MistakeWidgetForm>(main, "_mistakeWidget"), "design-mistake-widget");
            Click(main, "RecallNext"); Click(main, "RecallNoIdea");
            Check(Field<AppState>(main, "_state").ActiveSession!.QuizIndex == 2 && Control<Button>(main, "RecallNext").Enabled, "No idea records zero grade and permits next");
            Click(main, "RecallNext");
            Check(Field<bool>(main, "_allowClose") && StateStore.Load().History.Any(s => s.Completed), "Completion persists and enables exit");
            Screenshot(main, "design-results"); VisibleIn(main, "FinishTodayButton");
            main.BeginInvoke(new Action(() =>
            {
                var records = Application.OpenForms.OfType<UiDialog>().Single();
                var content = All(records).OfType<TextBox>().Single().Text;
                Check(content.Contains("我的回答") && content.Contains("只想起一点") && content.Contains("适应；调整"), "Detailed results retain original answers grades and meanings");
                Screenshot(records, "design-records"); records.Close();
            }));
            Click(main, "ViewTodayRecords");
            var official = File.ReadAllBytes(StateStore.StatePath);
            Click(main, "RetryMistakes"); Click(main, "RecallNoIdea"); Click(main, "RecallNext");
            Click(main, "RecallNoIdea"); Click(main, "RecallNext");
            Check(official.SequenceEqual(File.ReadAllBytes(StateStore.StatePath)), "Mistake retry never changes official grades or review dates");
            Click(main, "ReviewTodayWords");
            Screenshot(main, "design-review");
            Check(!Control<Label>(main, "ReviewExample").Visible, "Review also omits empty example");
            Click(main, "ReviewBack");
            Click(main, "FinishTodayButton");
            Check(main.IsDisposed, "Completion exit button closes the app");
            TestLongCard();
            TestPlanIntegration(state);
            TestRecommendedVisual();
            Groups.Add("clean UI plans confidence recall persistence errors retry completion responsive cards");
        }
        finally { Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR", directory); }
    }

    private static void TestPlanDialog(AppState state)
    {
        var original = Json(state);
        using (var cancel = new LearningPlanForm(state, Today, (_, _, _) => throw new Exception("Cancel must not save")))
        {
            ShowOffscreen(cancel); Control<NumericUpDown>(cancel, "DailyNewWordsInput").Value = 30;
            VisibleIn(cancel, "SaveLearningPlan"); VisibleIn(cancel, "ApplyRecommendedQuota");
            Screenshot(cancel, "design-plan"); cancel.Close();
            Check(Json(state) == original, "Plan edits are isolated until save");
        }
        var saves = 0;
        using var plan = new LearningPlanForm(state, Today, (quantity, exam, completion) => { saves++; return saves > 1; });
        ShowOffscreen(plan);
        Control<DateTimePicker>(plan, "ExamDateInput").Value = Today.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        Click(plan, "SaveLearningPlan");
        Check(saves == 0 && Control<Label>(plan, "PlanError").Text.Length > 0, "Plan rejects past date before saving");
        Control<DateTimePicker>(plan, "ExamDateInput").Value = Today.AddDays(60).ToDateTime(TimeOnly.MinValue);
        Click(plan, "ApplyRecommendedQuota");
        Check(Control<NumericUpDown>(plan, "DailyNewWordsInput").Value == new StudyEngine(state.ActiveWordEntries).GetNewWordPlan(state, Today).RequiredPerDay, "Plan uses existing recommendation logic");
        Click(plan, "SaveLearningPlan");
        Check(!plan.IsDisposed && plan.DialogResult != DialogResult.OK && saves == 1 && Control<Label>(plan, "PlanError").Text.Length > 0, "Failed plan save stays open with error");
        Click(plan, "SaveLearningPlan"); Check(saves == 2 && plan.DialogResult == DialogResult.OK, "Plan save retries successfully");
    }

    private static void TestLongCard()
    {
        var bank = Package("long-card", ("extraordinarily-long-example", string.Concat(Enumerable.Repeat("这是一段较长的中文释义，必须完整保留并能滚动查看。", 100))));
        bank.Words[0].Hint = string.Concat(Enumerable.Repeat("联想记忆。", 100));
        bank.Words[0].Example = string.Concat(Enumerable.Repeat("An example sentence. ", 100));
        StateStore.Save(State(bank));
        using var main = new MainForm(new StartupRegistration(new MemoryStartupEntryStore(), Environment.ProcessPath, false, blocked: true));
        ShowOffscreen(main);
        main.ClientSize = new Size(Math.Max(UiLayout.Pixels(main, 680), main.MinimumSize.Width), Math.Max(UiLayout.Pixels(main, 600), main.MinimumSize.Height));
        Click(main, "StartLearningButton"); Click(main, "StudyRating1");
        Screenshot(main, "design-long-card"); DumpLayout(main, "design-long-card");
        var body = Control<Panel>(main, "CardBody");
        Check(body.VerticalScroll.Visible, "Long imported contents scroll vertically");
        Check(!body.HorizontalScroll.Visible, "Long imported contents wrap without horizontal scroll");
        VisibleIn(main, "StudyNext");
        Check(Control<Label>(main, "StudyMeaning").Text == bank.Words[0].Meaning, "Long meaning remains complete");
        main.ClientSize = new Size(main.MinimumSize.Width, main.MinimumSize.Height); Application.DoEvents();
        Screenshot(main, "design-minimum-card");
        VisibleIn(main, "StudyNext");
        Check(!Field<Panel>(main, "_content").HorizontalScroll.Visible, "Minimum main window does not scroll horizontally");
        Click(main, "StudyNext");
        VisibleIn(main, "RevealRecallAnswer");
        Click(main, "RecallNoIdea"); VisibleIn(main, "RecallNext");
        Screenshot(main, "design-minimum-recall");
    }

    private static void TestPlanIntegration(AppState original)
    {
        StateStore.Save(original);
        using var main = new MainForm(new StartupRegistration(new MemoryStartupEntryStore(), Environment.ProcessPath, true, blocked: true));
        ShowOffscreen(main);
        void SavePlan(int quantity, Action<LearningPlanForm>? afterClick = null)
        {
            main.BeginInvoke(new Action(() =>
            {
                var dialog = Application.OpenForms.OfType<LearningPlanForm>().Single();
                Control<NumericUpDown>(dialog, "DailyNewWordsInput").Value = quantity;
                Click(dialog, "SaveLearningPlan"); afterClick?.Invoke(dialog);
            }));
            Invoke(main, "ShowLearningPlan");
        }
        using (var locked = new FileStream(StateStore.StatePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            SavePlan(1, dialog => { Check(dialog.DialogResult != DialogResult.OK, "Main plan failure does not report success"); dialog.Close(); });
            Check(Field<AppState>(main, "_state").DailyNewWords == 2, "Main plan failed save leaves live quota unchanged");
        }
        SavePlan(1);
        Check(StateStore.Load().DailyNewWords == 1 && Control<Label>(main, "TodayNewCount").Text == "1", "Plan changes before starting update today's quota and home");
        Click(main, "StartLearningButton"); Click(main, "StudyRating2"); Click(main, "StudyNext");
        var session = Json(Field<AppState>(main, "_state").ActiveSession!);
        SavePlan(3);
        Check(StateStore.Load().DailyNewWords == 3 && Json(Field<AppState>(main, "_state").ActiveSession!) == session, "Plan changes after starting preserve today's task");
        Check(Control<Label>(main, "TodayNewCount").Text == "1", "Home shows fixed task count after next-day quota change");
        Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_TODAY", "2026-11-01");
        try
        {
            SavePlan(2, dialog =>
            {
                Check(dialog.DialogResult != DialogResult.OK && Control<Label>(dialog, "PlanError").Text.Length > 0, "Cross-day plan save requires reopening"); dialog.Close();
            });
            Check(StateStore.Load().DailyNewWords == 3, "Cross-day dialog cannot overwrite saved quota");
        }
        finally { Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_TODAY", "2026-10-31"); }
    }

    private static void TestRecommendedVisual()
    {
        var bank = WordBankCatalog.Load().Banks.Single(b => b.Id == "cet46-high");
        var state = State(bank); state.DailyNewWords = 5; StateStore.Save(state);
        using var main = new MainForm(new StartupRegistration(new MemoryStartupEntryStore(), Environment.ProcessPath, true, blocked: true));
        ShowOffscreen(main);
        Check(Control<Label>(main, "TodayNewCount").Text == "5" && Control<Label>(main, "HomeProgressLabel").Text.Contains("153"), "Recommended bank keeps its original count in the redesigned home");
        Screenshot(main, "ui-home");
        Click(main, "StartLearningButton"); Screenshot(main, "ui-study");
        Click(main, "StudyRating1"); Screenshot(main, "ui-meaning");
        main.BeginInvoke(new Action(() =>
        {
            var dialog = Application.OpenForms.OfType<SkipWordConfirmForm>().Single();
            Screenshot(dialog, "design-skip-confirmation");
            Check(ReferenceEquals(dialog.AcceptButton, dialog.CancelButton), "Enter still cancels permanent skip by default");
            Click(dialog, "ConfirmPermanentSkip");
        }));
        Click(main, "PermanentSkipButton");
        var stateAfterSkip = Field<AppState>(main, "_state");
        Check(stateAfterSkip.SkippedWords.Count == 1, "Redesigned skip action persists its confirmed change");
        Invoke(main, "ShowSkippedWords"); Screenshot(main, "design-skipped-list");
        Click(main, "RestoreSkippedWord");
        Check(StateStore.Load().SkippedWords.Count == 0, "Redesigned skipped list can restore a word");
    }
}
