using System.Runtime.InteropServices;
using System.Text.Json;
using Cet6WordMachine;

internal static partial class Program
{
    private sealed class ManualWidgetClock : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 10, 31, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        internal void Advance(TimeSpan duration) => Now += duration;
    }
    private sealed class MemoryRestStore : IMistakeRestStore
    {
        internal MistakeRestState Value = new(); internal int Writes; internal bool FailLoad, FailSave;
        public MistakeRestState Load()
        {
            if (FailLoad) throw new IOException("test read failure");
            return new() { Version = Value.Version, HiddenUntilUtc = Value.HiddenUntilUtc, RetryAfterUtc = Value.RetryAfterUtc };
        }
        public void Save(MistakeRestState state)
        { if (FailSave) throw new IOException("test write failure"); Writes++; Value = state; }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr WidgetSendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    private static void TestMistakeRest()
    {
        TestWidgetBounds(); TestRestPersistence();
        Exception? failure = null;
        using var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        host.Shown += (_, _) => host.BeginInvoke(new Action(() =>
        {
            try { TestRestWidgetUi(); TestRestIntegration(); }
            catch (Exception ex) { failure = ex; }
            finally { host.Close(); }
        }));
        Application.Run(host);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Groups.Add("mistake widget screen clamps native moves random quiz self grading persisted 1h rest 2m cooldown and unchanged learning records");
    }

    private static void TestWidgetBounds()
    {
        foreach (var area in new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1920, 0, 1920, 1040), new Rectangle(0, -1080, 1280, 1020), new Rectangle(80, 40, 800, 560) })
        {
            foreach (var origin in new[] { new Point(area.Left - 300, area.Top - 200), new Point(area.Right + 200, area.Bottom + 300), new Point(area.Left, area.Top), new Point(area.Right - 100, area.Top + 50) })
            {
                var bounded = WidgetScreenBounds.Clamp(new Rectangle(origin, new Size(400, 200)), area);
                Check(area.Contains(bounded) && bounded.Size == new Size(400, 200), "All edges including negative monitor coordinates remain fully visible");
            }
            Check(WidgetScreenBounds.Clamp(new Rectangle(area.Left - 100, area.Top - 100, 4000, 4000), area) == area, "Oversized widget is fitted to the entire available work area");
        }
    }

    private static void TestRestPersistence()
    {
        var clock = new ManualWidgetClock(); var dir = Path.Combine(_root, "widget-policy"); var store = new MistakeRestStore(dir);
        var policy = new MistakeRestPolicy(store, clock);
        Check(policy.CanAttempt && !Directory.Exists(dir), "Reading default widget rest never creates data files");
        Check(policy.TryBegin() && !policy.CanAttempt && policy.RetryRemaining == TimeSpan.FromMinutes(2), "Attempt is reserved before question display");
        var path = Path.Combine(dir, "mistake-widget.json"); var begin = File.ReadAllBytes(path);
        Check(!policy.TryBegin() && begin.SequenceEqual(File.ReadAllBytes(path)), "Duplicate begin does not reset deadline or write again");
        var resumed = new MistakeRestPolicy(store, clock);
        Check(!resumed.CanAttempt && !resumed.IsHidden, "Restart cannot clear a pending attempt cooldown");
        clock.Advance(TimeSpan.FromSeconds(30)); policy.Finish(false);
        Check(policy.RetryRemaining == TimeSpan.FromMinutes(2), "Failure countdown starts at grading rather than question opening");
        clock.Advance(TimeSpan.FromMilliseconds(119999)); Check(!policy.CanAttempt && !policy.IsHidden, "Failure stays visible and locked just before 2 minutes");
        clock.Advance(TimeSpan.FromMilliseconds(1)); Check(policy.CanAttempt, "Retry unlocks at exactly 2 minutes");
        Check(policy.TryBegin() && policy.Finish(true) && policy.IsHidden && !policy.CanAttempt, "Passing grants one hour rest");
        var saved = File.ReadAllBytes(path);
        Check(new MistakeRestPolicy(store, clock).IsHidden, "One-hour rest survives restart");
        clock.Advance(TimeSpan.FromMilliseconds(3599999)); Check(policy.IsHidden, "Rest remains hidden just before the one hour boundary");
        clock.Advance(TimeSpan.FromMilliseconds(1)); Check(!policy.IsHidden && policy.CanAttempt, "Rest expires exactly after one hour");
        Check(saved.SequenceEqual(File.ReadAllBytes(path)), "Timer polling does not write learning or rest files");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        { Check(!policy.TryBegin() && policy.Error.Length > 0 && !policy.IsHidden && !policy.CanAttempt, "Save failure leaves card visible and waits rather than granting rest"); }
        Check(saved.SequenceEqual(File.ReadAllBytes(path)) && Directory.GetFiles(dir).Length == 1, "Failed atomic save preserves previous sidecar and cleans temporary file");
        File.WriteAllText(path, "{corrupt"); var corrupt = new MistakeRestPolicy(store, clock);
        Check(corrupt.NeedsReload && !corrupt.IsHidden && !corrupt.TryBegin() && File.ReadAllText(path) == "{corrupt", "Unreadable sidecar never hides or silently overwrites itself");
        store.Save(new()); Check(corrupt.TryBegin() && !corrupt.NeedsReload, "Fixed sidecar can be reloaded through the same button");
        File.WriteAllText(path, "{\"Version\":2}"); Check(new MistakeRestPolicy(store, clock).NeedsReload, "Unknown rest versions fail visibly");
        File.WriteAllText(path, JsonSerializer.Serialize(new MistakeRestState { HiddenUntilUtc = clock.Now, RetryAfterUtc = clock.Now }));
        Check(new MistakeRestPolicy(store, clock).NeedsReload, "Contradictory rest deadlines rejected");
    }

    private static void WidgetVisibleWithinScreen(MistakeWidgetForm widget)
    {
        Check(widget.Visible && Screen.AllScreens.Any(screen => screen.WorkingArea.Contains(widget.Bounds)), "Entire widget stays within one screen work area");
        VisibleIn(widget, "HideMistakeWidget");
        Check(Control<Button>(widget, "HideMistakeWidget").Width >= UiLayout.Pixels(widget, 145), "Rest button remains readable at current DPI");
    }

    private static void TestRestWidgetUi()
    {
        var clock = new ManualWidgetClock(); var store = new MemoryRestStore(); var policy = new MistakeRestPolicy(store, clock);
        var items = new[] { new MistakeItem("arrest", "əˈrest", "逮捕", "", 0), new MistakeItem("adapt", "əˈdæpt", "适应；调整", "改变", 1), new MistakeItem("benefit", "", "益处；使受益", "", 0) };
        var draws = 0;
        using var widget = new MistakeWidgetForm(policy, count => draws++ % count);
        widget.UpdateItems(items, "bank-a:today"); Application.DoEvents();
        WidgetVisibleWithinScreen(widget); Check(store.Writes == 0, "Displaying wrong words does not start a cooldown");
        Screenshot(widget, "widget-ready");
        var area = Screen.FromControl(widget).WorkingArea;
        widget.Location = new Point(area.Left - 5000, area.Top - 5000);
        WidgetVisibleWithinScreen(widget);
        widget.Location = new Point(area.Right + 5000, area.Bottom + 5000);
        WidgetVisibleWithinScreen(widget);
        SetWindowPos(widget.Handle, IntPtr.Zero, area.Right + 1000, area.Bottom + 1000, widget.Width, widget.Height, 0x14);
        WidgetVisibleWithinScreen(widget);
        var rectangle = new ProbeRect { Left = area.Left - 500, Top = area.Top - 500, Right = area.Left - 100, Bottom = area.Top - 300 };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<ProbeRect>());
        try
        {
            Marshal.StructureToPtr(rectangle, pointer, false);
            Check(WidgetSendMessage(widget.Handle, 0x216, IntPtr.Zero, pointer) == (IntPtr)1, "Native drag is constrained before applying proposed rectangle");
            var output = Marshal.PtrToStructure<ProbeRect>(pointer);
            Check(Screen.FromPoint(Cursor.Position).WorkingArea.Contains(Rectangle.FromLTRB(output.Left, output.Top, output.Right, output.Bottom)), "WM_MOVING respects cursor monitor work area");
        }
        finally { Marshal.FreeHGlobal(pointer); }
        widget.Close(); Check(!widget.IsDisposed && widget.Visible, "Ordinary close cannot bypass card challenge");
        WidgetSendMessage(widget.Handle, 0x112, (IntPtr)0xF020, IntPtr.Zero);
        Check(widget.WindowState == FormWindowState.Normal, "System minimize command cannot bypass card challenge");
        widget.BeginInvoke(new Action(() =>
        {
            var quiz = Application.OpenForms.OfType<MistakeRestQuizForm>().Single();
            Check(Control<Label>(quiz, "RestQuizWord").Text == items[0].Word && !Control<TextBox>(quiz, "RestQuizComparison").Visible, "Random question comes from displayed wrong words without revealing answer");
            Check(Control<Label>(widget, "MistakeQuizCover").Visible && !Field<FlowLayoutPanel>(widget, "_list").Visible && widget.Visible, "Only card answers are covered during recall; card stays present");
            Check(!Control<Button>(widget, "HideMistakeWidget").Enabled, "Cannot open a second quiz during the first");
            Click(quiz, "RevealRestQuiz"); Check(quiz.SelfGrade is null && Control<TextBox>(quiz, "RestQuizAnswer").Visible, "Blank answer cannot reveal and pass");
            Screenshot(quiz, "widget-quiz-input");
            Control<TextBox>(quiz, "RestQuizAnswer").Text = "抓起来"; Click(quiz, "RevealRestQuiz");
            Check(Control<TextBox>(quiz, "RestQuizComparison").Text.Contains("逮捕") && Control<TextBox>(quiz, "RestQuizComparison").Text.Contains("抓起来"), "Self grading presents both reference and typed meaning");
            VisibleIn(quiz, "RestQuizGrade2"); VisibleIn(quiz, "RestQuizGrade1"); VisibleIn(quiz, "CancelRestQuiz");
            Screenshot(quiz, "widget-quiz-grade"); quiz.Size = quiz.MinimumSize; Application.DoEvents();
            VisibleIn(quiz, "RestQuizGrade0"); VisibleIn(quiz, "RestQuizGrade2"); VisibleIn(quiz, "CancelRestQuiz"); Screenshot(quiz, "widget-quiz-minimum");
            Click(quiz, "RestQuizGrade1");
        }));
        Click(widget, "HideMistakeWidget");
        Check(policy.RetryRemaining == TimeSpan.FromMinutes(2) && widget.Visible && !Control<Button>(widget, "HideMistakeWidget").Enabled, "Partial answer enters visible two-minute cooldown");
        Screenshot(widget, "widget-cooldown"); var writes = store.Writes;
        Click(widget, "HideMistakeWidget"); Check(draws == 1 && store.Writes == writes, "Disabled button cannot draw or reset cooldown");
        widget.UpdateItems(items, "bank-a:today"); Check(!Control<Button>(widget, "HideMistakeWidget").Enabled, "Normal wrong-word refresh cannot reset cooldown");
        widget.Hide(); widget.RefreshRestVisibility(); WidgetVisibleWithinScreen(widget);
        clock.Advance(TimeSpan.FromSeconds(119)); widget.RefreshRestVisibility();
        Check(Control<Label>(widget, "MistakeRestStatus").Text.Contains("00:01") && !Control<Button>(widget, "HideMistakeWidget").Enabled, "Cooldown countdown uses exact elapsed time");
        clock.Advance(TimeSpan.FromSeconds(1)); widget.RefreshRestVisibility();
        Check(Control<Button>(widget, "HideMistakeWidget").Enabled && widget.Visible, "Two minutes enables retry without hiding card");
        widget.BeginInvoke(new Action(() =>
        {
            var quiz = Application.OpenForms.OfType<MistakeRestQuizForm>().Single();
            Check(Control<Label>(quiz, "RestQuizWord").Text == items[1].Word, "Each retry selects from the current pool again");
            Control<TextBox>(quiz, "RestQuizAnswer").Text = "适应"; Click(quiz, "RevealRestQuiz"); Click(quiz, "RestQuizGrade2");
        }));
        Click(widget, "HideMistakeWidget"); Check(!widget.Visible && policy.IsHidden, "Passing self assessment hides card for one hour");
        widget.UpdateItems(items.Append(new MistakeItem("calm", "", "平静的", "", 0)).ToArray(), "bank-a:today");
        Check(!widget.Visible, "New wrong words do not interrupt granted rest");
        clock.Advance(TimeSpan.FromSeconds(3599)); widget.RefreshRestVisibility(); Check(!widget.Visible, "Card does not return early");
        clock.Advance(TimeSpan.FromSeconds(1));
        PumpUntil(() => widget.Visible); // Let the real UI timer perform the return.
        WidgetVisibleWithinScreen(widget); Check(Control<Button>(widget, "HideMistakeWidget").Enabled, "Timer returns card automatically at rest expiration");
        widget.BeginInvoke(new Action(() => Application.OpenForms.OfType<MistakeRestQuizForm>().Single().Close()));
        Click(widget, "HideMistakeWidget"); Check(!policy.IsHidden && !policy.CanAttempt && widget.Visible, "Closing a question cannot reroll immediately or hide the card");
        clock.Advance(TimeSpan.FromMinutes(2)); widget.RefreshRestVisibility();
        widget.BeginInvoke(new Action(() => Click(Application.OpenForms.OfType<MistakeRestQuizForm>().Single(), "RestQuizNoIdea")));
        Click(widget, "HideMistakeWidget"); Check(policy.RetryRemaining == TimeSpan.FromMinutes(2) && widget.Visible, "No-idea choice fails and starts cooldown");
        clock.Advance(TimeSpan.FromMinutes(2)); widget.RefreshRestVisibility();
        widget.BeginInvoke(new Action(() =>
        {
            var quiz = Application.OpenForms.OfType<MistakeRestQuizForm>().Single();
            widget.UpdateItems(items, "bank-b:tomorrow");
            Check(quiz.WasInvalidated, "Bank/day change cancels stale challenge before it can grant rest");
        }));
        Click(widget, "HideMistakeWidget"); Check(widget.Visible && !policy.IsHidden, "Stale quiz cannot hide new bank's card");
        clock.Advance(TimeSpan.FromMinutes(2)); widget.RefreshRestVisibility();
        widget.BeginInvoke(new Action(() =>
        {
            var quiz = Application.OpenForms.OfType<MistakeRestQuizForm>().Single();
            Control<TextBox>(quiz, "RestQuizAnswer").Text = "回答"; Click(quiz, "RevealRestQuiz"); store.FailSave = true; Click(quiz, "RestQuizGrade2");
        }));
        Click(widget, "HideMistakeWidget"); Check(widget.Visible && !policy.IsHidden && policy.Error.Length > 0, "Failed rest save never hides the widget");
        store.FailSave = false;
        widget.UpdateItems([], "bank-b:tomorrow"); Check(!widget.Visible && !Field<System.Windows.Forms.Timer>(widget, "_restTimer").Enabled, "No remaining mistakes removes empty card and stops polling");
        var many = Enumerable.Range(0, 30).Select(i => new MistakeItem("extraordinarily-long-" + i, "", string.Concat(Enumerable.Repeat("较长的中文释义需要能滚动查看。", 8)), "", 0)).ToArray();
        widget.UpdateItems(many, "bank-b:tomorrow"); Application.DoEvents(); WidgetVisibleWithinScreen(widget);
        Check(Field<FlowLayoutPanel>(widget, "_list").VerticalScroll.Visible, "Many long errors scroll without pushing rest controls off screen"); Screenshot(widget, "widget-long-list");
        widget.CloseForApplication(); Check(widget.IsDisposed, "Normal app shutdown still closes the widget");
        using var unreadable = new MistakeWidgetForm(new MistakeRestPolicy(new MemoryRestStore { FailLoad = true }, clock));
        unreadable.UpdateItems(items); Check(unreadable.Visible && Control<Button>(unreadable, "HideMistakeWidget").Enabled, "Unreadable rest file leaves visible card with retry action");
        Click(unreadable, "HideMistakeWidget"); Check(unreadable.Visible && Application.OpenForms.OfType<MistakeRestQuizForm>().Count() == 0, "Unreadable state blocks quiz instead of hiding silently");
        var removedPolicy = new MistakeRestPolicy(new MemoryRestStore(), clock);
        using var removed = new MistakeWidgetForm(removedPolicy, _ => 0);
        removed.UpdateItems(items, "same-bank");
        removed.BeginInvoke(new Action(() =>
        {
            var quiz = Application.OpenForms.OfType<MistakeRestQuizForm>().Single();
            removed.UpdateItems(items.Skip(1).ToArray(), "same-bank");
            Check(quiz.WasInvalidated, "Removing selected wrong word invalidates its active question");
        }));
        Click(removed, "HideMistakeWidget"); Check(removed.Visible && !removedPolicy.IsHidden, "Removing question cannot accidentally grant rest");
        using var closing = new MistakeWidgetForm(new MistakeRestPolicy(new MemoryRestStore(), clock));
        closing.UpdateItems(items);
        closing.BeginInvoke(new Action(closing.CloseForApplication));
        Click(closing, "HideMistakeWidget");
        Check(closing.IsDisposed && Application.OpenForms.OfType<MistakeRestQuizForm>().Count() == 0, "Application shutdown closes active question and its timer without orphan windows");
    }

    private static void TestRestIntegration()
    {
        var previous = Environment.GetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR");
        Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR", Path.Combine(_root, "widget-learning-account"));
        try
        {
            var bank = Package("widget-fixture", ("arrest", "逮捕")); var state = State(bank); StateStore.Save(state);
            using var main = new MainForm(new StartupRegistration(new MemoryStartupEntryStore(), Environment.ProcessPath, false, blocked: true));
            ShowOffscreen(main); Click(main, "StartLearningButton"); Click(main, "StudyRating0"); Click(main, "StudyNext"); Click(main, "RecallNoIdea");
            var widget = Field<MistakeWidgetForm>(main, "_mistakeWidget"); Check(widget.Visible, "Actual failed daily recall shows rest-enabled widget");
            var original = File.ReadAllBytes(StateStore.StatePath);
            widget.BeginInvoke(new Action(() =>
            {
                var quiz = Application.OpenForms.OfType<MistakeRestQuizForm>().Single();
                Control<TextBox>(quiz, "RestQuizAnswer").Text = "逮捕"; Click(quiz, "RevealRestQuiz"); Click(quiz, "RestQuizGrade2");
            }));
            Click(widget, "HideMistakeWidget");
            Check(!widget.Visible && original.SequenceEqual(File.ReadAllBytes(StateStore.StatePath)), "Rest quiz success does not change official grades progress or answers");
            Invoke(main, "RefreshMistakeWidget"); Check(!widget.Visible, "Main form refresh respects rest instead of forcing card visible");
            Check(new MistakeRestPolicy(new MistakeRestStore(StateStore.DataDirectory)).IsHidden, "Actual app rest deadline is persisted in isolated account sidecar");
            Click(main, "RecallNext"); Check(!widget.Visible, "Daily completion refresh respects the granted hour");
            Click(main, "FinishTodayButton"); Check(main.IsDisposed && widget.IsDisposed, "App can close normally after completing daily task during rest");
        }
        finally { Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR", previous); }
    }
}
