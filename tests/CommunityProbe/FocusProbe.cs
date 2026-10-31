using System.Runtime.InteropServices;
using System.Text.Json;
using Cet6WordMachine;

internal static partial class Program
{
    [DllImport("user32.dll", EntryPoint = "GetFocus")]
    private static extern IntPtr NativeInputFocus();
    [DllImport("user32.dll", EntryPoint = "GetActiveWindow")]
    private static extern IntPtr NativeActiveWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int NativeWindowStyle(IntPtr window, int index);

    private static bool IsNativeTopmost(Form form) => (NativeWindowStyle(form.Handle, -20) & 0x8) != 0;

    private sealed record FocusObservation(string Scenario, int TimerTicks, int LostFocusEvents,
        bool RetainedNativeFocus, bool RetainedActiveWindow, string TypedText);

    private static void TestInputFocus()
    {
        var previous = Environment.GetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR");
        Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR", Path.Combine(_root, "focus-account"));
        var observations = new List<FocusObservation>();
        Exception? failure = null;
        using var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        host.Shown += (_, _) => host.BeginInvoke(new Action(() =>
        {
            try
            {
                TestDailyAndRestInputFocus(observations);
                TestPassiveWidgetFocus(observations);
            }
            catch (Exception ex) { failure = ex; }
            finally { host.Close(); }
        }));
        try { Application.Run(host); }
        finally
        {
            Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR", previous);
            File.WriteAllText(Path.Combine(_root, "input-focus.json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        foreach (var result in observations)
        {
            Check(result.TimerTicks >= 2, result.Scenario + ": real one-second widget timer ticks occurred");
            Check(result.LostFocusEvents == 0 && result.RetainedNativeFocus && result.RetainedActiveWindow,
                result.Scenario + ": focus must remain in the input throughout widget refresh");
            Check(result.TypedText == "中文意思可以连续输入", result.Scenario + ": native Chinese character input is uninterrupted");
        }
        Groups.Add("native keyboard focus and Chinese WM_CHAR input across real widget timer ticks in daily recall modal rest quiz cooldown and rest expiry");
    }

    private static void TestDailyAndRestInputFocus(List<FocusObservation> observations)
    {
        StateStore.Save(State(Package("focus-fixture", ("adapt", "适应"), ("benefit", "益处"))));
        using var main = new MainForm(new StartupRegistration(new MemoryStartupEntryStore(), Environment.ProcessPath, false, blocked: true));
        ShowOffscreen(main); Click(main, "StartLearningButton");
        for (var index = 0; index < 2; index++) { Click(main, "StudyRating0"); Click(main, "StudyNext"); }
        Click(main, "RecallNoIdea"); Click(main, "RecallNext");
        var widget = Field<MistakeWidgetForm>(main, "_mistakeWidget");
        Check(widget.Visible && IsNativeTopmost(widget), "Focus fixture contains an actual topmost mistake widget");
        var original = File.ReadAllBytes(StateStore.StatePath);
        observations.Add(ObserveInputFocus(main, Control<TextBox>(main, "RecallAnswer"), widget, "daily RecallAnswer"));
        Exception? modalFailure = null;
        widget.BeginInvoke(new Action(() =>
        {
            var quiz = Application.OpenForms.OfType<MistakeRestQuizForm>().Single();
            try
            {
                observations.Add(ObserveInputFocus(quiz, Control<TextBox>(quiz, "RestQuizAnswer"), widget, "modal RestQuizAnswer"));
            }
            catch (Exception ex) { modalFailure = ex; }
            finally { quiz.Close(); }
        }));
        Click(widget, "HideMistakeWidget");
        if (modalFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(modalFailure).Throw();
        Check(widget.Visible && !Control<Button>(widget, "HideMistakeWidget").Enabled, "Cancelling focus probe retains the visible two-minute cooldown");
        Check(IsNativeTopmost(widget), "Closing modal rest quiz preserves native widget topmost state");
        Check(original.SequenceEqual(File.ReadAllBytes(StateStore.StatePath)), "Typing and rest quiz focus checks do not change formal learning records");
    }

    private static void TestPassiveWidgetFocus(List<FocusObservation> observations)
    {
        using var editor = new Form { ShowInTaskbar = false };
        using var answer = new TextBox { Multiline = true, Dock = DockStyle.Fill };
        editor.Controls.Add(answer); ShowOffscreen(editor);
        var clock = new ManualWidgetClock();
        var policy = new MistakeRestPolicy(new MemoryRestStore(), clock);
        using var widget = new MistakeWidgetForm(policy);
        var items = new[] { new MistakeItem("adapt", "", "适应", "", 0) };
        observations.Add(ObserveInputFocus(editor, answer, widget, "first appearance", () => widget.UpdateItems(items)));
        answer.Clear();
        Check(policy.TryBegin() && policy.Finish(false), "Set up failed-quiz cooldown without waiting two minutes");
        observations.Add(ObserveInputFocus(editor, answer, widget, "cooldown countdown"));
        Check(widget.Visible && !Control<Button>(widget, "HideMistakeWidget").Enabled, "Countdown retains visibility and disables retry");
        clock.Advance(TimeSpan.FromMinutes(2));
        Check(policy.TryBegin() && policy.Finish(true), "Set up one-hour rest");
        widget.RefreshRestVisibility(); Check(!widget.Visible, "Passed quiz hides the widget");
        answer.Clear();
        observations.Add(ObserveInputFocus(editor, answer, widget, "one-hour rest expires", () => clock.Advance(TimeSpan.FromHours(1))));
        Check(widget.Visible && IsNativeTopmost(widget) && Control<Button>(widget, "HideMistakeWidget").Enabled,
            "Rest expiry restores topmost card and permits the next quiz");
        widget.CloseForApplication();
    }

    private static FocusObservation ObserveInputFocus(Form form, TextBox answer, MistakeWidgetForm widget, string scenario, Action? duringInput = null)
    {
        form.Activate(); answer.Focus(); Application.DoEvents();
        Check(NativeInputFocus() == answer.Handle && answer.Focused, scenario + ": input initially owns native keyboard focus");
        var ticks = 0; var losses = 0;
        var retainedFocus = true; var retainedActive = true;
        var timer = Field<System.Windows.Forms.Timer>(widget, "_restTimer");
        void InspectFocus()
        {
            retainedFocus &= NativeInputFocus() == answer.Handle && answer.Focused;
            retainedActive &= NativeActiveWindow() == form.Handle;
        }
        void OnTick(object? sender, EventArgs e) { ticks++; InspectFocus(); }
        void OnLostFocus(object? sender, EventArgs e) { losses++; }
        void Type(string text)
        {
            foreach (var character in text)
            {
                InspectFocus();
                var target = NativeInputFocus();
                // Route only to this test's currently focused input; never type into another app
                // or bypass missing focus by writing Text / messaging an unfocused textbox.
                if (target == answer.Handle) WidgetSendMessage(target, 0x102, (IntPtr)character, (IntPtr)1);
            }
        }
        timer.Tick += OnTick; answer.LostFocus += OnLostFocus;
        try
        {
            Type("中文"); duringInput?.Invoke(); InspectFocus();
            PumpUntil(() => ticks >= 1); Type("意思");
            PumpUntil(() => ticks >= 2); Type("可以连续输入");
            return new(scenario, ticks, losses, retainedFocus, retainedActive, answer.Text);
        }
        finally { timer.Tick -= OnTick; answer.LostFocus -= OnLostFocus; }
    }
}
