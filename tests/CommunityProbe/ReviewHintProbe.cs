using Cet6WordMachine;

internal static partial class Program
{
    private static void InModal<T>(Form owner, string button, Action<T> action) where T : Form
    {
        Exception? failure = null;
        owner.BeginInvoke(new Action(() =>
        {
            var dialog = Application.OpenForms.OfType<T>().Single();
            try { action(dialog); }
            catch (Exception ex) { failure = ex; }
            finally { if (!dialog.IsDisposed) dialog.Close(); }
        }));
        Click(owner, button);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static string LearningWithoutHints()
    {
        var state = StateStore.Load(); state.CustomHints.Clear(); return Json(state);
    }

    private static void TestReviewHints()
    {
        var previous = Environment.GetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR");
        var previousDate = Environment.GetEnvironmentVariable("WORD_MACHINE_COMMUNITY_TODAY");
        Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR", Path.Combine(_root, "review-hints-account"));
        try
        {
            var bank = Package("review-hints", ("adapt", "适应；调整"), ("benefit", "益处；使受益"));
            bank.Words[0].Hint = "让椅子适应自己的坐姿。";
            var initial = State(bank); initial.CustomHints["adapt"] = "学习时保存的联想"; StateStore.Save(initial);
            var store = new MemoryAiStore(); var generator = new FakeGenerator();
            var services = new AiServices(store, generator);
            using var main = new MainForm(new StartupRegistration(new MemoryStartupEntryStore(), Environment.ProcessPath, false, blocked: true), services);
            ShowOffscreen(main); Click(main, "StartLearningButton");
            for (var i = 0; i < 2; i++) { Click(main, "StudyRating1"); Click(main, "StudyNext"); }
            for (var i = 0; i < 2; i++) { Click(main, "RecallNoIdea"); Click(main, "RecallNext"); }
            var official = LearningWithoutHints();
            Click(main, "ReviewTodayWords");
            Check(Control<Label>(main, "CardWord").Text == "adapt" && Control<Label>(main, "ReviewHint").Text == initial.CustomHints["adapt"], "Review shows the saved learning hint");
            VisibleIn(main, "EditReviewHint");
            Check(store.Loads == 0 && generator.Generations == 0, "Opening review does not read credentials or call AI");
            var saved = File.ReadAllBytes(StateStore.StatePath);
            InModal<HintEditorForm>(main, "EditReviewHint", editor =>
            {
                Check(editor.HintText == initial.CustomHints["adapt"], "Review editor starts from the displayed hint");
                Control<TextBox>(editor, "HintInput").Text = "未保存的修改";
                Click(editor, "CancelHint");
            });
            Check(saved.SequenceEqual(File.ReadAllBytes(StateStore.StatePath)) && Control<Label>(main, "ReviewHint").Text == initial.CustomHints["adapt"], "Cancel preserves both review display and disk");

            var longHint = string.Concat(Enumerable.Repeat("用荒诞但有逻辑的画面记住这个单词。", 80));
            InModal<HintEditorForm>(main, "EditReviewHint", editor =>
            {
                var input = Control<TextBox>(editor, "HintInput"); input.Clear();
                var widget = Field<MistakeWidgetForm>(main, "_mistakeWidget");
                Check(widget.Visible, "Review input is tested with the real mistake widget visible");
                var focus = ObserveInputFocus(editor, input, widget, "review HintInput");
                Check(focus.TimerTicks >= 2 && focus.LostFocusEvents == 0 && focus.RetainedNativeFocus && focus.RetainedActiveWindow && focus.TypedText == "中文意思可以连续输入", "Review editor accepts uninterrupted native Chinese input across widget ticks");
                input.Text = longHint; Click(editor, "SaveHint");
            });
            Check(StateStore.Load().CustomHints["adapt"] == longHint && Control<Label>(main, "ReviewHint").Text == longHint, "Manual review edit saves and updates the displayed hint immediately");
            main.Size = main.MinimumSize; Application.DoEvents();
            foreach (var name in new[] { "ReviewBack", "EditReviewHint", "PermanentSkipButton", "ReviewPrevious", "ReviewNext" })
            {
                VisibleIn(main, name);
                var button = Control<Button>(main, name);
                Check(TextRenderer.MeasureText(button.Text, button.Font).Width < button.Width - UiLayout.Pixels(main, 16), "Review action text fits at minimum size: " + name);
            }
            var body = Control<Panel>(main, "CardBody");
            Check(body.VerticalScroll.Visible && !body.HorizontalScroll.Visible, "Long edited review hint wraps and scrolls without hiding actions");
            Screenshot(main, "review-long-hint-minimum");
            Click(main, "ReviewNext"); Check(!Control<Label>(main, "ReviewHint").Visible, "Word without a hint leaves no empty review section");
            InModal<HintEditorForm>(main, "EditReviewHint", editor => { Control<TextBox>(editor, "HintInput").Text = "获益的小故事"; Click(editor, "SaveHint"); });
            Check(Control<Label>(main, "ReviewHint").Visible && StateStore.Load().CustomHints["benefit"] == "获益的小故事", "Adding a missing hint reveals it without changing another word");
            InModal<HintEditorForm>(main, "EditReviewHint", editor => { Control<TextBox>(editor, "HintInput").Clear(); Click(editor, "SaveHint"); });
            Check(!Control<Label>(main, "ReviewHint").Visible && !StateStore.Load().CustomHints.ContainsKey("benefit"), "Clearing a word without a built-in hint hides the empty section");
            Click(main, "ReviewPrevious");
            Check(Control<Label>(main, "ReviewHint").Text == longHint, "Review navigation retains edited hints and word alignment");
            saved = File.ReadAllBytes(StateStore.StatePath);
            using (var locked = new FileStream(StateStore.StatePath, FileMode.Open, FileAccess.Read, FileShare.None))
                InModal<HintEditorForm>(main, "EditReviewHint", editor => { Control<TextBox>(editor, "HintInput").Text = "写入应失败"; Click(editor, "SaveHint"); });
            Check(saved.SequenceEqual(File.ReadAllBytes(StateStore.StatePath)) && Control<Label>(main, "ReviewHint").Text == longHint && Field<AppState>(main, "_state").CustomHints["adapt"] == longHint && Field<bool>(main, "_persistentStatus"), "Failed review save retains disk, live state and display with a visible error");
            InModal<HintEditorForm>(main, "EditReviewHint", editor => { Control<TextBox>(editor, "HintInput").Clear(); Click(editor, "SaveHint"); });
            Check(!StateStore.Load().CustomHints.ContainsKey("adapt") && Control<Label>(main, "ReviewHint").Text == bank.Words[0].Hint, "Retry after unlock succeeds and empty save restores built-in hint");

            generator.Generate = _ => Task.FromResult("一把椅子长出轮子，主动调整位置适应新主人。");
            InModal<HintEditorForm>(main, "EditReviewHint", editor =>
            {
                Click(editor, "GenerateAiHint"); Check(!StateStore.Load().CustomHints.ContainsKey("adapt"), "Review AI generation stays an unsaved draft");
                Click(editor, "UndoAiHint"); Check(editor.HintText == bank.Words[0].Hint, "Review supports undoing AI generation");
                Click(editor, "GenerateAiHint"); Click(editor, "SaveHint");
            });
            var aiHint = StateStore.Load().CustomHints["adapt"];
            Check(generator.Generations == 2 && Control<Label>(main, "ReviewHint").Text == aiHint, "Explicit AI save updates review through the shared editor");
            Check(LearningWithoutHints() == official, "Review edits leave scores answers scheduling completion and bank contents unchanged");
            Invoke(main, "HideStatus"); Screenshot(main, "review-ai-hint");
            Click(main, "ReviewBack"); Click(main, "ReviewTodayWords");
            Check(Control<Label>(main, "ReviewHint").Text == aiHint, "Leaving and reentering review uses the latest saved hint");
            main.Dispose();

            var other = Package("review-other-bank", ("adapt", "不同释义"));
            var switched = WordBankCatalog.Switch(StateStore.Load(), other, false);
            Check(!switched.CustomHints.ContainsKey("adapt"), "Review edits do not leak to another bank");
            StateStore.Save(WordBankCatalog.Switch(switched, bank, false));
            Check(StateStore.Load().CustomHints["adapt"] == aiHint, "Switching back restores the edited hint");
            using var restarted = new MainForm(new StartupRegistration(new MemoryStartupEntryStore(), Environment.ProcessPath, false, blocked: true),
                new AiServices(new MemoryAiStore { FailLoad = true }, generator));
            ShowOffscreen(restarted); Click(restarted, "ReviewTodayWords");
            Check(Control<Label>(restarted, "ReviewHint").Text == aiHint, "Restarted review reads saved hints offline even when AI credentials are unavailable");
            InModal<HintEditorForm>(restarted, "EditReviewHint", editor =>
            {
                Control<TextBox>(editor, "HintInput").Text = "过期编辑不应保存";
                Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_TODAY", "2026-11-01");
                Click(editor, "SaveHint");
            });
            Check(StateStore.Load().CustomHints["adapt"] == aiHint && Field<DateOnly>(restarted, "_currentDate") == Today.AddDays(1), "Cross-day review edit is rejected while the app transitions to the new day");
            Check(Field<System.Windows.Forms.Timer>(restarted, "_dayTimer").Enabled, "Review editor always resumes day detection");
            Groups.Add("home AI discovery review hint editing native input cancellation rollback clearing navigation offline restart per-bank isolation and unchanged learning records");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR", previous);
            Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_TODAY", previousDate);
        }
    }
}
