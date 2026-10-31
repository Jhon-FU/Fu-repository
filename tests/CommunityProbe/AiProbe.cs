using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Cet6WordMachine;

internal static partial class Program
{
    private const string FakeKey = "sk-fake-for-local-tests-not-a-credential";
    private static readonly WordEntry AiWord = new() { Word = "arrest", Phonetic = "əˈrest", Meaning = "v. 逮捕", Hint = "PRIVATE_HINT_NOT_TO_SEND", Example = "PRIVATE_EXAMPLE_NOT_TO_SEND" };
    private const string ExampleHint = "警察把 arrest 想成 a rest：给逃犯戴上手铐，宣布开启强制休息套餐！";
    private static AiSettings Config() => new() { ApiKey = FakeKey };
    private static string AiError(Action action)
    {
        try { action(); } catch (AiHintException ex) { _checks++; return ex.Message; }
        throw new InvalidOperationException("Expected a safe AI error");
    }
    private static async Task<string> AiErrorAsync(Func<Task> action)
    {
        try { await action(); } catch (AiHintException ex) { _checks++; return ex.Message; }
        throw new InvalidOperationException("Expected a safe asynchronous AI error");
    }
    private static void TestAi()
    {
        TestAiStore(); Task.Run(TestAiHttp).GetAwaiter().GetResult();
        // Async UI tests need a real application loop. A sequence of DoEvents
        // calls alone uninstalls WinForms' synchronization context between forms.
        Exception? failure = null;
        using var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        host.Shown += (_, _) => host.BeginInvoke(new Action(() =>
        {
            try { TestAiDialogs(); TestAiIntegration(); TestReviewHints(); }
            catch (Exception ex) { failure = ex; }
            finally { host.Close(); }
        }));
        Application.Run(host);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Groups.Add("optional AI HTTP contract privacy DPAPI errors cancellation editable drafts transactional saves and DPI");
    }

    private static void TestAiStore()
    {
        Check(AiHintPrompt.SystemMessage.Contains("80 字以内，不设最低字数") && AiHintPrompt.SystemMessage.Contains("故事内部要有逻辑") && AiHintPrompt.SystemMessage.Contains("不能把它说成真实词根"), "Prompt preserves creative logical mnemonics with no minimum length");
        var dir = Path.Combine(_root, "ai-config"); var store = new AiSettingsStore(dir);
        Check(store.Load().ApiKey == "" && !Directory.Exists(dir), "Unconfigured settings load performs no write");
        store.Save(new AiSettings()); Check(!Directory.Exists(dir), "Clearing never-configured settings is safe");
        store.Save(Config()); var path = Path.Combine(dir, "deepseek-settings.dat");
        Check(store.Load().ApiKey == FakeKey && store.Load().Model == AiSettings.DefaultModel, "DPAPI round trip with fake credential");
        var encrypted = File.ReadAllBytes(path);
        Check(!Encoding.UTF8.GetString(encrypted).Contains(FakeKey) && !Encoding.Unicode.GetString(encrypted).Contains(FakeKey), "No plaintext Key in settings file");
        Check(Directory.GetFiles(dir).Length == 1, "Atomic save leaves no temporary credential files");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            AiError(() => store.Load()); AiError(() => store.Save(new AiSettings { ApiKey = "sk-replacement-fake" }));
            AiError(() => store.Save(new AiSettings()));
        }
        Check(File.ReadAllBytes(path).SequenceEqual(encrypted), "Failed save and clear retain original configuration");
        File.WriteAllText(path, "corrupt-not-a-key");
        AiError(() => store.Load()); Check(File.ReadAllText(path) == "corrupt-not-a-key", "Corrupt settings retained for explicit replacement");
        File.WriteAllBytes(path, WindowsUserProtection.Protect(Encoding.UTF8.GetBytes("{\"ApiKey\":null}")));
        AiError(() => store.Load());
        store.Save(Config()); store.Save(new AiSettings()); store.Save(new AiSettings());
        Check(!File.Exists(path) && store.Load().ApiKey == "", "Clearing removes local protected configuration idempotently");
        AiError(() => new AiSettings { ApiKey = "bad key" }.Validate(true));
        AiError(() => new AiSettings { ApiKey = "bad\nkey" }.Validate(true));
        AiError(() => new AiSettings { ApiKey = FakeKey, Model = "unknown-model" }.Validate(true));
    }

    private sealed class MockHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return action(request, token); }
    }
    private static HttpResponseMessage Reply(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static string Completion(string? content, string finish = "stop") => JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = finish, message = new { content } } } });

    private static async Task TestAiHttp()
    {
        using var handler = new MockHttp(async (request, token) =>
        {
            Check(request.RequestUri!.AbsoluteUri == "https://api.deepseek.com/chat/completions" && request.Method == HttpMethod.Post, "Generation uses official HTTPS POST endpoint");
            Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == FakeKey, "Credential is sent only in authorization header");
            var body = await request.Content!.ReadAsStringAsync(token); using var json = JsonDocument.Parse(body); var root = json.RootElement;
            Check(root.GetProperty("model").GetString() == AiSettings.DefaultModel && root.GetProperty("thinking").GetProperty("type").GetString() == "disabled" && !root.GetProperty("stream").GetBoolean(), "Request selects supported model and non-thinking response");
            var messages = root.GetProperty("messages"); Check(messages.GetArrayLength() == 2 && messages[0].GetProperty("content").GetString() == AiHintPrompt.SystemMessage, "Request contains only system prompt and one input");
            using var input = JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
            Check(input.RootElement.EnumerateObject().Count() == 3 && input.RootElement.GetProperty("word").GetString() == AiWord.Word && input.RootElement.GetProperty("phonetic").GetString() == AiWord.Phonetic && input.RootElement.GetProperty("meaning").GetString() == AiWord.Meaning, "Only current word phonetic and meaning are sent");
            Check(!body.Contains(FakeKey) && !body.Contains("PRIVATE_"), "No credential history existing mnemonic or example in prompt");
            return Reply(Completion("戴上手铐，强制休息。"));
        });
        using var http = new HttpClient(handler); var client = new DeepSeekHintClient(http);
        Check(await client.GenerateAsync(AiWord, Config(), default) == "戴上手铐，强制休息。", "Short meaningful output below 30 characters is accepted");
        Check(handler.Calls == 1, "Exactly one generation request per call");
        AiError(() => client.GenerateAsync(AiWord, new AiSettings(), default).GetAwaiter().GetResult());
        AiError(() => client.GenerateAsync(new WordEntry { Word = "word" }, Config(), default).GetAwaiter().GetResult());
        Check(handler.Calls == 1, "Invalid input or absent Key never sends a request");
        foreach (var content in new[] { new string('词', 80), string.Concat(Enumerable.Repeat("😀", 80)) })
        {
            using var mock = new MockHttp((_, _) => Task.FromResult(Reply(Completion(content)))); using var h = new HttpClient(mock);
            Check(await new DeepSeekHintClient(h).GenerateAsync(AiWord, Config(), default) == content, "80 text elements accepted without splitting Unicode");
        }
        foreach (var body in new[] { Completion(new string('词', 81)), Completion(""), Completion(null), Completion("不完整", "length"), "{bad", "{}", "{\"choices\":[]}", "{\"choices\":{}}", "{\"choices\":[null]}", Completion("文本", "tool_calls"), new string('x', 66000) })
        {
            using var mock = new MockHttp((_, _) => Task.FromResult(Reply(body))); using var h = new HttpClient(mock);
            await AiErrorAsync(() => new DeepSeekHintClient(h).GenerateAsync(AiWord, Config(), default));
            Check(mock.Calls == 1, "Invalid output never triggers automatic retry or additional billing");
        }
        foreach (var status in new[] { 301, 400, 401, 402, 403, 404, 422, 429, 500, 503 })
        {
            using var mock = new MockHttp((_, _) => Task.FromResult(Reply("remote body " + FakeKey, (HttpStatusCode)status))); using var h = new HttpClient(mock);
            var message = await AiErrorAsync(() => new DeepSeekHintClient(h).GenerateAsync(AiWord, Config(), default));
            Check(!message.Contains(FakeKey) && !message.Contains("remote body") && mock.Calls == 1, "Server errors never leak returned credential data or retry");
        }
        using (var mock = new MockHttp((_, _) => throw new HttpRequestException(FakeKey)))
        using (var h = new HttpClient(mock))
            Check(!(await AiErrorAsync(() => new DeepSeekHintClient(h).GenerateAsync(AiWord, Config(), default))).Contains(FakeKey), "Network error is safe");
        using (var mock = new MockHttp(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Reply("{}"); }))
        using (var h = new HttpClient(mock))
        {
            Check((await AiErrorAsync(() => new DeepSeekHintClient(h, TimeSpan.FromMilliseconds(40)).GenerateAsync(AiWord, Config(), default))).Contains("超时"), "Request timeout is bounded and explained");
            using var cancel = new CancellationTokenSource(); cancel.CancelAfter(40);
            try { await new DeepSeekHintClient(h).GenerateAsync(AiWord, Config(), cancel.Token); throw new Exception("Cancellation lost"); }
            catch (OperationCanceledException) { Check(cancel.IsCancellationRequested, "Explicit cancellation is propagated"); }
        }
        using (var mock = new MockHttp((request, _) =>
        {
            Check(request.Method == HttpMethod.Get && request.RequestUri!.AbsoluteUri == "https://api.deepseek.com/models" && request.Content is null, "Connection test only reads model list");
            return Task.FromResult(Reply(JsonSerializer.Serialize(new { data = new[] { new { id = AiSettings.DefaultModel } } })));
        }))
        using (var h = new HttpClient(mock)) { await new DeepSeekHintClient(h).TestConnectionAsync(Config(), default); Check(mock.Calls == 1, "Connection test succeeds with available model"); }
        foreach (var body in new[] { "{\"data\":[]}", "{\"data\":null}", "{}", "{\"data\":[null]}" })
        {
            using var mock = new MockHttp((_, _) => Task.FromResult(Reply(body))); using var h = new HttpClient(mock);
            await AiErrorAsync(() => new DeepSeekHintClient(h).TestConnectionAsync(Config(), default));
        }
    }

    private sealed class MemoryAiStore : IAiSettingsStore
    {
        internal AiSettings Value = Config(); internal int Loads, Saves; internal bool FailLoad, FailSave;
        public AiSettings Load() { Loads++; if (FailLoad) throw new AiHintException("配置无法读取，请重新填写。"); return new AiSettings { ApiKey = Value.ApiKey, Model = Value.Model }; }
        public void Save(AiSettings value) { if (FailSave) throw new AiHintException("配置保存失败，请重试。"); value.Validate(false); Saves++; Value = value; }
    }
    private sealed class FakeGenerator : IHintGenerator
    {
        internal int Generations, Connections; internal CancellationToken Token;
        internal Func<CancellationToken, Task<string>> Generate = _ => Task.FromResult(ExampleHint);
        internal Func<CancellationToken, Task> Connect = _ => Task.CompletedTask;
        public Task<string> GenerateAsync(WordEntry entry, AiSettings settings, CancellationToken token)
        { Generations++; Token = token; return Generate(token); }
        public Task TestConnectionAsync(AiSettings settings, CancellationToken token)
        { settings.Validate(true); Connections++; Token = token; return Connect(token); }
    }
    private static void PumpUntil(Func<bool> finished)
    {
        var time = System.Diagnostics.Stopwatch.StartNew();
        while (!finished() && time.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents(); if (!finished()) throw new TimeoutException("UI operation did not finish");
    }

    private static void AiActionSizes(Form form)
    {
        foreach (var button in All(form).OfType<Button>())
            Check(button.Width >= UiLayout.Pixels(form, 100) && button.Height >= UiLayout.Pixels(form, 36), $"AI action retains useful dimensions: {button.Name} {button.Size}");
    }

    private static void TestAiDialogs()
    {
        var store = new MemoryAiStore(); var generator = new FakeGenerator(); var services = new AiServices(store, generator);
        using (var settings = new AiSettingsForm(services))
        {
            ShowOffscreen(settings);
            Check(Control<TextBox>(settings, "AiApiKey").UseSystemPasswordChar && generator.Connections == 0 && generator.Generations == 0, "Opening settings masks Key and makes no network calls");
            Control<CheckBox>(settings, "ShowAiKey").Checked = true; Check(!Control<TextBox>(settings, "AiApiKey").UseSystemPasswordChar, "Explicit show Key toggle works");
            Control<CheckBox>(settings, "ShowAiKey").Checked = false;
            AiActionSizes(settings); Screenshot(settings, "ai-settings"); settings.Size = settings.MinimumSize; Application.DoEvents(); AiActionSizes(settings);
            VisibleIn(settings, "TestAiConnection"); VisibleIn(settings, "SaveAiSettings"); VisibleIn(settings, "AiApiKey"); Screenshot(settings, "ai-settings-minimum");
            Click(settings, "TestAiConnection"); Check(generator.Connections == 1 && store.Saves == 0 && Control<Label>(settings, "AiSettingsStatus").Text.Contains("连接成功"), "Testing does not save settings or generate text");
            Control<TextBox>(settings, "AiApiKey").Text = "sk-unsaved"; settings.Close();
            Check(store.Value.ApiKey == FakeKey && store.Saves == 0, "Closing settings preserves saved Key");
        }
        using (var settings = new AiSettingsForm(services))
        {
            ShowOffscreen(settings); Control<TextBox>(settings, "AiApiKey").Text = "  sk-edited-fake  ";
            store.FailSave = true; Click(settings, "SaveAiSettings"); Check(!settings.IsDisposed && store.Saves == 0, "Settings save failure remains open");
            store.FailSave = false; Click(settings, "SaveAiSettings"); Check(store.Value.ApiKey == "sk-edited-fake" && store.Saves == 1, "Saving trims pasted Key and succeeds on retry");
        }
        using (var settings = new AiSettingsForm(services))
        {
            ShowOffscreen(settings); Control<TextBox>(settings, "AiApiKey").Clear(); Click(settings, "SaveAiSettings"); Check(store.Value.ApiKey == "", "UI supports explicitly clearing configuration");
        }
        store.FailLoad = true;
        using (var settings = new AiSettingsForm(services)) { ShowOffscreen(settings); Check(Control<Label>(settings, "AiSettingsStatus").Text.Length > 0, "Unreadable settings show an error"); settings.Close(); Check(store.Saves == 2, "Cancel does not replace unreadable settings"); }
        store.FailLoad = false; store.Value = Config();
        var connection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); generator.Connect = _ => connection.Task;
        using (var settings = new AiSettingsForm(services))
        {
            ShowOffscreen(settings); Click(settings, "TestAiConnection"); Check(!Control<Button>(settings, "SaveAiSettings").Enabled && !Control<TextBox>(settings, "AiApiKey").Enabled, "Connection request disables conflicting edits");
            Click(settings, "TestAiConnection"); Check(generator.Token.IsCancellationRequested && generator.Connections == 2, "Second connection click cancels instead of duplicating");
            connection.SetResult(); PumpUntil(() => Control<Button>(settings, "SaveAiSettings").Enabled);
            Check(Control<Label>(settings, "AiSettingsStatus").Text.Contains("取消"), "Late connection success cannot overwrite cancellation");
        }
        var loads = store.Loads;
        using (var editor = new HintEditorForm(AiWord, "我的原有联想", services))
        {
            ShowOffscreen(editor); Check(store.Loads == loads && generator.Generations == 0, "Opening editor does not read Key or call AI");
            Click(editor, "GenerateAiHint"); Check(editor.HintText == ExampleHint && editor.DialogResult != DialogResult.OK && store.Saves == 2, "AI output is an editable unsaved draft");
            Check(Control<Button>(editor, "GenerateAiHint").Text == "换一种" && !Control<TextBox>(editor, "HintInput").ReadOnly, "Success permits regeneration and manual editing");
            AiActionSizes(editor); Screenshot(editor, "ai-hint-generated"); editor.Size = editor.MinimumSize; Application.DoEvents(); AiActionSizes(editor);
            VisibleIn(editor, "GenerateAiHint"); VisibleIn(editor, "HintAiSettings"); VisibleIn(editor, "SaveHint"); VisibleIn(editor, "CancelHint"); Screenshot(editor, "ai-hint-minimum");
            Click(editor, "UndoAiHint"); Check(editor.HintText == "我的原有联想", "Undo restores manual draft before generation");
            generator.Generate = _ => throw new AiHintException("余额不足"); Click(editor, "GenerateAiHint");
            Check(editor.HintText == "我的原有联想" && Control<Label>(editor, "HintStatus").Text.Contains("余额不足"), "Failure retains draft and exposes safe error");
            generator.Generate = _ => Task.FromResult(ExampleHint); Click(editor, "GenerateAiHint");
            Control<TextBox>(editor, "HintInput").Text = "手动改过的联想"; Click(editor, "SaveHint"); Check(editor.DialogResult == DialogResult.OK && editor.HintText == "手动改过的联想", "Save accepts manual edits to AI draft");
        }
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); generator.Generate = _ => pending.Task;
        using (var editor = new HintEditorForm(AiWord, "保留草稿", services))
        {
            ShowOffscreen(editor); var count = generator.Generations; Click(editor, "GenerateAiHint");
            Check(Control<TextBox>(editor, "HintInput").ReadOnly && !Control<Button>(editor, "SaveHint").Enabled, "Generation prevents concurrent edits and save");
            Click(editor, "GenerateAiHint"); Check(generator.Token.IsCancellationRequested && generator.Generations == count + 1, "Repeated generation click only cancels");
            pending.SetResult("必须被丢弃"); PumpUntil(() => Control<Button>(editor, "SaveHint").Enabled);
            Check(editor.HintText == "保留草稿", "Cancelled late output cannot overwrite draft");
        }
        pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); generator.Generate = _ => pending.Task;
        using (var editor = new HintEditorForm(AiWord, "关闭前的草稿", services))
        {
            ShowOffscreen(editor); var changes = 0; Control<TextBox>(editor, "HintInput").TextChanged += (_, _) => changes++;
            Click(editor, "GenerateAiHint"); editor.Close(); Check(generator.Token.IsCancellationRequested, "Closing editor cancels in-flight request");
            pending.SetResult("过期输出"); PumpUntil(() => Field<CancellationTokenSource?>(editor, "_request") is null);
            Check(changes == 0 && editor.IsDisposed, "Late completion never writes to a disposed editor");
        }
        store.Value = new AiSettings(); generator.Generate = _ => Task.FromResult(ExampleHint);
        using (var editor = new HintEditorForm(AiWord, "可以手写", services))
        {
            ShowOffscreen(editor); var count = generator.Generations;
            editor.BeginInvoke(new Action(() => Application.OpenForms.OfType<AiSettingsForm>().Single().Close()));
            Click(editor, "GenerateAiHint"); Check(generator.Generations == count && editor.HintText == "可以手写", "Missing Key opens settings without an implicit paid request");
        }
    }

    private static void TestAiIntegration()
    {
        var previous = Environment.GetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR");
        Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR", Path.Combine(_root, "ai-learning-account"));
        try
        {
            var bank = Package("ai-fixture", ("arrest", "v. 逮捕")); var state = State(bank); state.CustomHints["arrest"] = "已保存的原联想"; StateStore.Save(state);
            var store = new MemoryAiStore(); var generator = new FakeGenerator();
            using var main = new MainForm(new StartupRegistration(new MemoryStartupEntryStore(), Environment.ProcessPath, false, blocked: true), new AiServices(store, generator));
            ShowOffscreen(main); Check(store.Loads == 0 && generator.Generations == 0, "App startup does not load credentials or call AI");
            Check(Field<ContextMenuStrip>(main, "_applicationMenu").Items.Cast<ToolStripItem>().Any(i => i.Name == "AiSettingsMenuItem"), "Menu exposes AI settings");
            var beforeSettings = File.ReadAllBytes(StateStore.StatePath);
            InModal<AiSettingsForm>(main, "AiSettingsHomeButton", settings =>
            {
                Check(!Field<System.Windows.Forms.Timer>(main, "_dayTimer").Enabled, "Home AI settings pause day transition while editing");
                Control<TextBox>(settings, "AiApiKey").Text = "";
                Click(settings, "CancelAiSettings");
            });
            Check(store.Saves == 0 && store.Value.ApiKey == FakeKey, "Cancelling home AI settings preserves existing configuration");
            InModal<AiSettingsForm>(main, "AiSettingsHomeButton", settings => Click(settings, "SaveAiSettings"));
            Check(store.Saves == 1 && generator.Generations == 0 && generator.Connections == 0, "Home settings save explicitly without making any network request");
            Check(Field<System.Windows.Forms.Timer>(main, "_dayTimer").Enabled && beforeSettings.SequenceEqual(File.ReadAllBytes(StateStore.StatePath)), "Home settings restore timer and preserve learning progress");
            Click(main, "StartLearningButton"); Click(main, "StudyRating1"); var saved = File.ReadAllBytes(StateStore.StatePath);
            main.BeginInvoke(new Action(() =>
            {
                var editor = Application.OpenForms.OfType<HintEditorForm>().Single(); Click(editor, "GenerateAiHint");
                Check(File.ReadAllBytes(StateStore.StatePath).SequenceEqual(saved), "Generation never writes learning storage"); editor.Close();
            }));
            Click(main, "EditStudyHint"); Check(File.ReadAllBytes(StateStore.StatePath).SequenceEqual(saved) && Control<Label>(main, "StudyHint").Text == "已保存的原联想", "Closing unsaved AI draft preserves disk and displayed hint");
            main.BeginInvoke(new Action(() => { var editor = Application.OpenForms.OfType<HintEditorForm>().Single(); Click(editor, "GenerateAiHint"); Click(editor, "SaveHint"); }));
            Click(main, "EditStudyHint"); Check(StateStore.Load().CustomHints["arrest"] == ExampleHint && Control<Label>(main, "StudyHint").Text == ExampleHint, "Explicit save uses existing transactional per-bank hint storage");
            Check(!File.ReadAllText(StateStore.StatePath).Contains(FakeKey), "Credential never enters learning storage");
            using var restart = new HintEditorForm(AiWord, StateStore.Load().CustomHints["arrest"], new AiServices(new MemoryAiStore { FailLoad = true }, generator));
            ShowOffscreen(restart); Check(restart.HintText == ExampleHint, "Saved hint remains available without a readable Key or network");
        }
        finally { Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR", previous); }
    }
}
