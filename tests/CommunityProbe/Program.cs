using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Cet6WordMachine;

internal static partial class Program
{
    private static int _checks;
    private static readonly DateOnly Today = new(2026, 10, 31);
    private static string _root = "";
    private static readonly List<string> Groups = [];

    [STAThread]
    private static int Main(string[] args)
    {
        _root = args.FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "WordMachineCommunityProbe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR", Path.Combine(_root, "account"));
        Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_TODAY", "2026-10-31");
        Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_SKIP_AUTOSTART", "1");
        try
        {
            if (args.Contains("--dpi-unaware")) Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            ApplicationConfiguration.Initialize();
            if (!args.Contains("--focus-only"))
            {
                TestCatalog(); TestImports(); TestStorage(); TestSwitching(); TestSmallBanks();
                StartupProbe.Run(Check, _root);
                Groups.Add("explicit startup registration isolated registry CMD removal and failure handling");
                TestUi();
                TestRedesignedUi();
                TestAi();
                TestMistakeRest();
            }
            TestInputFocus();
            var result = new { passed = true, checks = _checks, groups = Groups, mode =
#if TESTMODE
                "TESTMODE"
#else
                "production"
#endif
            };
            File.WriteAllText(Path.Combine(_root, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
    private static void Throws(Action action, string message)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or UnauthorizedAccessException) { _checks++; return; }
        throw new InvalidOperationException(message);
    }
    private static string Json(object value) => JsonSerializer.Serialize(value, WordBankCatalog.JsonOptions);
    private static WordBankPackage Package(string id, params (string Word, string Meaning)[] rows) => new()
    {
        Id = id, Name = id, Words = rows.Select(x => new WordEntry { Word = x.Word, Meaning = x.Meaning }).ToList(),
    };
    private static AppState State(WordBankPackage bank) => new()
    {
        ActiveWordBankId = bank.Id, ActiveWordBankName = bank.Name, ActiveWordEntries = bank.Words,
        HasChosenWordBank = true, DailyNewWords = 2,
    };

    private static void TestCatalog()
    {
        var catalog = WordBankCatalog.Load();
        Check(catalog.Banks.Count == 5 && catalog.Warnings.Count == 0, "Five valid recommended banks required");
        foreach (var bank in catalog.Banks)
        {
            WordBankCatalog.Validate(bank);
            Check(bank.Words.All(w => !string.IsNullOrWhiteSpace(w.Meaning)), bank.Id + " meaning coverage");
            Check(bank.Words.All(w => w.Word != "issue"), bank.Id + " school word regression");
            Check(bank.Words.Select(w => w.Word).Distinct(StringComparer.OrdinalIgnoreCase).Count() == bank.Words.Count, bank.Id + " unique words");
        }
        var sets = catalog.Banks.ToDictionary(b => b.Id, b => b.Words.Select(w => w.Word).ToHashSet());
        Check(!sets["cet4-pure"].Overlaps(sets["cet6-pure"]), "Pure levels must be disjoint");
        Check(sets["cet4-high"].IsSubsetOf(sets["cet4-pure"]), "CET4 high subset");
        Check(sets["cet6-high"].IsSubsetOf(sets["cet6-pure"]), "CET6 high subset");
        Check(sets["cet46-high"].SetEquals(sets["cet4-high"].Union(sets["cet6-high"])), "Combined exact deduplicated union");
        var account = Environment.GetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR");
        Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR", null);
        Environment.SetEnvironmentVariable("CET6_WORD_MACHINE_DATA_DIR", "C:\\DO_NOT_USE_PERSONAL_ACCOUNT");
        Check(StateStore.DataDirectory.EndsWith("WordMachineCommunity") && !StateStore.DataDirectory.Contains("DO_NOT_USE"), "Personal override must be ignored");
        Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR", account);
        Groups.Add("catalog and account isolation");
    }

    private static void TestImports()
    {
        var csv = WordBankImporter.ReadDelimited("\uFEFFword,meaning,phonetic,hint,example\r\nalpha,第一,a,提示,example\r\nbeta,第二,b,,\r\n");
        Check(csv.CanImport && csv.Words.Count == 2 && csv.Words[0].Hint == "提示", "CSV headers and BOM");
        var multiline = WordBankImporter.ReadDelimited("word,meaning\nalpha,\"第一,含逗号\n以及\"\"引号\"\"\"\n");
        Check(multiline.CanImport && multiline.Words[0].Meaning == "第一,含逗号\n以及\"引号\"", "Quoted multiline CSV");
        var paste = WordBankImporter.ReadDelimited("单词\t中文释义\nalpha\t第一\nbeta\t第二");
        Check(paste.CanImport && paste.Words.Count == 2, "Chinese tab headers");
        Check(WordBankImporter.ReadDelimited("alpha\t第一\nbeta\t第二").CanImport, "Headerless paste");
        Check(WordBankImporter.ReadDelimited("name\t姓名\nmeaning\t意思").Words.Count == 2, "Headerless real words named name and meaning are data");
        Check(WordBankImporter.ReadDelimited("issue\t议题").CanImport, "Custom imports are not silently filtered by the school list");
        Check(WordBankImporter.ReadDelimited("word;meaning\nalpha;第一").CanImport, "Semicolon delimiter");
        var duplicate = WordBankImporter.ReadDelimited("word,meaning\nalpha,第一\nALPHA,第一");
        Check(duplicate.CanImport && duplicate.Words.Count == 1 && duplicate.DuplicateCount == 1, "Identical duplicate rows collapse");
        var conflict = WordBankImporter.ReadDelimited("word,meaning\nalpha,第一\nALPHA,另一义");
        Check(!conflict.CanImport && conflict.Errors.Any(e => e.Contains("第 3 行")), "Conflicting duplicates block import with row number");
        Check(!WordBankImporter.ReadDelimited("word,meaning\nalpha,").CanImport, "Missing meaning blocks");
        Check(!WordBankImporter.ReadDelimited("word,hint\nalpha,test").CanImport, "Missing header blocks");
        Check(!WordBankImporter.ReadDelimited("\n  \n").CanImport, "Empty input blocks");
        Check(!WordBankImporter.ReadDelimited("word,meaning\n=cmd,test").CanImport, "Formula word rejected");
        string Letters(int index)
        {
            var word = "";
            do { word = (char)('a' + index % 26) + word; index /= 26; } while (index > 0);
            return "fixture" + word;
        }
        var oversized = WordBankImporter.ReadDelimited(string.Join('\n', Enumerable.Range(0, 50_001).Select(i => Letters(i) + ",测试释义")));
        Check(!oversized.CanImport && oversized.Errors.Any(e => e.Contains("50,000")), "Headerless input over limit is blocked in preview");
        Throws(() => WordBankImporter.ReadDelimited("word,meaning\nalpha,\"unfinished"), "Unclosed quotes reject");
        Throws(() => WordBankImporter.ReadJson("{bad}"), "Invalid JSON rejects");
        Check(!WordBankImporter.ReadJson("[null]").CanImport, "Null JSON row handled");
        Check(WordBankImporter.ReadJson("[{\"word\":\"alpha\",\"meaning\":\"第一\"}]").CanImport, "JSON array");
        Check(WordBankImporter.ReadJson("{\"formatVersion\":1,\"words\":[{\"word\":\"alpha\",\"meaning\":\"第一\"}]}").CanImport, "JSON package");
        Throws(() => WordBankImporter.ReadJson("{\"formatVersion\":2,\"words\":[]}"), "Unsupported package version");
        var path = Path.Combine(_root, "encoding.csv");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(path, "word,meaning\nalpha,释义", Encoding.GetEncoding("GB18030"));
        Check(WordBankImporter.ReadFile(path).Words[0].Meaning == "释义", "GB18030 CSV");
        File.WriteAllText(path, "word,meaning\nalpha,释义", Encoding.Unicode);
        Check(WordBankImporter.ReadFile(path).Words[0].Meaning == "释义", "UTF16 CSV");
        var xlsx = Path.Combine(_root, "import.xlsx");
        CreateXlsx(xlsx, false);
        var excel = WordBankImporter.ReadFile(xlsx);
        Check(excel.CanImport && excel.Words.Count == 1 && excel.Words[0].Meaning == "第一", "XLSX shared strings and sparse cells");
        var formulaXlsx = Path.Combine(_root, "formula.xlsx");
        CreateXlsx(formulaXlsx, true);
        Throws(() => WordBankImporter.ReadFile(formulaXlsx), "XLSX formulas must not be treated as text");
        var package = WordBankCatalog.Install(csv, "同名词库");
        var second = WordBankCatalog.Install(csv, "同名词库");
        Check(package.Id != second.Id && File.Exists(Path.Combine(WordBankCatalog.ImportDirectory, package.Id + ".json")), "Install uses independent IDs");
        Throws(() => WordBankCatalog.Install(conflict, "invalid"), "Invalid preview cannot save");
        var json = Path.Combine(_root, "export.json");
        WordBankImporter.Export(package, json);
        Check(Json(WordBankImporter.ReadFile(json).Words) == Json(package.Words), "JSON lossless round trip");
        var exported = Path.Combine(_root, "export.csv");
        WordBankImporter.Export(Package("formula-text", ("alpha", "=1+1")), exported);
        Check(File.ReadAllText(exported).Contains("'=1+1"), "CSV export formula protection");
        var invalidPath = Path.Combine(WordBankCatalog.ImportDirectory, "corrupt.json");
        File.WriteAllText(invalidPath, "{broken");
        var catalog = WordBankCatalog.Load();
        Check(catalog.Warnings.Count == 1 && File.ReadAllText(invalidPath) == "{broken", "Bad imported package retained with visible warning");
        File.Delete(invalidPath);
        Groups.Add("CSV TSV paste JSON XLSX encoding preview export validation");
    }

    private static void CreateXlsx(string path, bool formula)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Part(string name, string xml) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(xml); }
        Part("xl/workbook.xml", "<workbook xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Words' r:id='rId1'/></sheets></workbook>");
        Part("xl/_rels/workbook.xml.rels", "<Relationships><Relationship Id='rId1' Target='worksheets/sheet1.xml'/></Relationships>");
        Part("xl/sharedStrings.xml", "<sst><si><t>word</t></si><si><t>meaning</t></si><si><r><t>第</t></r><r><t>一</t></r></si></sst>");
        Part("xl/worksheets/sheet1.xml", "<worksheet><sheetData><row r='1'><c r='A1' t='s'><v>0</v></c><c r='C1' t='s'><v>1</v></c></row><row r='2'><c r='A2' t='inlineStr'><is><t>alpha</t></is></c><c r='C2' t='s'>" + (formula ? "<f>1+1</f>" : "") + "<v>2</v></c></row></sheetData></worksheet>");
    }

    private static void TestStorage()
    {
        var state = StateStore.Load();
        Check(state.Progress.Count == 0 && state.ActiveWordEntries.Count > 0, "Fresh independent account");
        StateStore.Save(state);
        state.DailyNewWords = 7; StateStore.Save(state);
        Check(File.Exists(StateStore.StatePath + ".bak") && StateStore.Load().DailyNewWords == 7, "Atomic persistence and backup");
        var before = File.ReadAllBytes(StateStore.StatePath);
        var candidate = StateStore.CopyForUpdate(state); candidate.DailyNewWords = 9;
        using (var locked = new FileStream(StateStore.StatePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Throws(() => StateStore.Save(candidate), "Locked account save must fail");
        Check(before.SequenceEqual(File.ReadAllBytes(StateStore.StatePath)) && state.DailyNewWords == 7, "Failed save preserves bytes and live state");
        StateStore.Save(candidate);
        Check(StateStore.Load().DailyNewWords == 9, "Retry after save failure succeeds");
        var valid = File.ReadAllText(StateStore.StatePath);
        File.WriteAllText(StateStore.StatePath, "not-json");
        Throws(() => StateStore.Load(), "Corrupt account cannot become empty account");
        Check(File.ReadAllText(StateStore.StatePath) == "not-json", "Corrupt account untouched");
        File.WriteAllText(StateStore.StatePath, valid);
        File.Delete(StateStore.StatePath);
        Throws(() => StateStore.Load(), "Missing main with backup cannot reset account");
        File.WriteAllText(StateStore.StatePath, valid);
        Groups.Add("atomic saves rollback retry corruption protection");
    }

    private static void TestSwitching()
    {
        var a = Package("bank-a", ("alpha", "第一"), ("beta", "第二"), ("gamma", "第三"));
        var b = Package("bank-b", ("alpha", "第一"), ("beta", "不同释义"), ("delta", "第四"));
        var state = State(a); var engine = new StudyEngine(a.Words);
        var session = engine.GetOrCreateSession(state, Today);
        foreach (var word in session.Words) engine.RateCard(session, word, 1);
        engine.SubmitSelfAssessment(state, session, "alpha", "个人回答", 2, Today);
        state.CustomHints["alpha"] = "我的联想";
        state.SkippedWords.Add("gamma");
        var snapshot = Json(state);
        var next = WordBankCatalog.Switch(state, b, true);
        Check(Json(state) == snapshot, "Switch builds candidate without mutating current state");
        Check(next.Progress.Count == 1 && next.Progress[0].Word == "alpha" && next.CustomHints["alpha"] == "我的联想", "First switch copies matching meaning progress");
        Check(next.ActiveSession is null && next.History.Count == 0 && next.SkippedWords.Count == 0, "New bank has independent tasks and skips");
        Check(Json(next.SavedWordBanks["bank-a"].ActiveSession!) == Json(session), "Pending task answers frozen");
        StateStore.Save(next);
        next = StateStore.Load();
        var restored = WordBankCatalog.Switch(next, a, false);
        Check(Json(restored.Progress) == Json(state.Progress) && Json(restored.ActiveSession!) == Json(session), "Restart and switch back restore exact task and progress");
        Check(restored.SkippedWords.SequenceEqual(state.SkippedWords) && restored.CustomHints["alpha"] == "我的联想", "Skip and hint preserved");
        var withoutReuse = WordBankCatalog.Switch(state, b, false);
        Check(withoutReuse.Progress.Count == 0, "Opt-out starts fresh bank without deleting source history");
        var changed = Package("bank-a", ("alpha", "updated externally"));
        var frozen = WordBankCatalog.Switch(next, changed, false);
        Check(frozen.ActiveWordEntries.Count == 3 && frozen.ActiveWordEntries[0].Meaning == "第一", "Previously used bank retains card snapshot");
        StateStore.Save(restored);
        var resumed = StateStore.Load();
        var task = new StudyEngine(resumed.ActiveWordEntries).GetOrCreateSession(resumed, Today);
        Check(task.QuizIndex == 1 && task.UserAnswers["alpha"] == "个人回答", "Resuming does not duplicate grading");
        Check(new StudyEngine(next.ActiveWordEntries).GetNewWordPlan(next, Today).RemainingWords == 2, "Plan follows selected bank remaining words");
        var before = File.ReadAllBytes(StateStore.StatePath);
        using (var locked = new FileStream(StateStore.StatePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Throws(() => StateStore.Save(WordBankCatalog.Switch(resumed, b, true)), "Switch save failure reported");
        Check(before.SequenceEqual(File.ReadAllBytes(StateStore.StatePath)) && resumed.ActiveWordBankId == "bank-a", "Failed switch preserves active bank");
        Groups.Add("bank switching snapshots same-sense carryover task resume");
    }

    private static void TestSmallBanks()
    {
        var bank = Package("single", ("alpha", "第一"));
        var state = State(bank); var engine = new StudyEngine(bank.Words);
        var task = engine.GetOrCreateSession(state, Today);
        Check(task.Words.Count == 1, "One-word bank schedules");
        engine.RateCard(task, "alpha", 0);
        engine.SubmitSelfAssessment(state, task, "alpha", "不知道", 0, Today);
        engine.CompleteSession(state, task);
        Check(state.Progress[0].NextReview == Today.AddDays(1), "Single word forgotten reviews tomorrow");
        Check(engine.BuildOptions(task, "alpha").Count == 4, "Tiny bank choice fallback terminates");
        var skipped = State(bank); var skipTask = engine.GetOrCreateSession(skipped, Today);
        engine.SkipWord(skipped, "alpha", Today);
        Check(skipTask.Completed && skipped.Progress.Count == 0 && engine.UnlearnedCount(skipped) == 0, "Small bank exhausted skip is not false learning");
        engine.RestoreWord(skipped, "alpha", Today);
        Check(engine.UnlearnedCount(skipped) == 1, "Skipped word can return");
        Groups.Add("small bank learning review skip exhaustion");
    }

    private static IEnumerable<Control> All(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls) foreach (var item in All(child)) yield return item;
    }
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
    private static void ShowOffscreen(Form form)
    {
        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
        form.Show(); Application.DoEvents();
    }
    private static void Screenshot(Form form, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(_root, name + ".png"));
    }
    private static void TestUi()
    {
        var initial = StateStore.Load();
        using (var manager = new WordBankManagerForm(initial))
        {
            ShowOffscreen(manager);
            ((ListBox)All(manager).Single(c => c.Name == "WordBankList")).SelectedItem = ((ListBox)All(manager).Single(c => c.Name == "WordBankList")).Items.Cast<WordBankPackage>().First(b => b.Id == "cet46-high");
            Check(All(manager).Any(c => c.Name == "UseSelectedWordBank" && c.Enabled), "Manager can select a bank");
            Screenshot(manager, "wordbanks-wide");
            manager.ClientSize = new Size(Math.Max(UiLayout.Pixels(manager, 780), manager.MinimumSize.Width), Math.Max(UiLayout.Pixels(manager, 560), manager.MinimumSize.Height)); Application.DoEvents();
            Screenshot(manager, "wordbanks-narrow");
            var apply = All(manager).Single(c => c.Name == "UseSelectedWordBank");
            Check(manager.RectangleToScreen(manager.ClientRectangle).Contains(apply.RectangleToScreen(apply.ClientRectangle)), "Narrow manager apply button remains visible");
            Check(All(manager).OfType<DataGridView>().Single().Height >= 100, "Narrow manager keeps word preview usable");
        }
        using (var import = new WordBankImportForm())
        {
            ShowOffscreen(import);
            All(import).Single(c => c.Name == "PasteWordBank").Text = "单词\t释义\nalpha\t第一\nbeta\t第二";
            ((Button)All(import).Single(c => c.Name == "ParsePastedWordBank")).PerformClick();
            Check(All(import).Single(c => c.Name == "ConfirmWordBankImport").Enabled, "Valid preview enables confirmation");
            Screenshot(import, "import-preview");
            All(import).Single(c => c.Name == "PasteWordBank").Text = "word,meaning\nalpha,";
            Check(!All(import).Single(c => c.Name == "ConfirmWordBankImport").Enabled, "Editing paste invalidates stale preview");
            ((Button)All(import).Single(c => c.Name == "ParsePastedWordBank")).PerformClick();
            Check(!All(import).Single(c => c.Name == "ConfirmWordBankImport").Enabled, "Bad preview cannot confirm");
            All(import).Single(c => c.Name == "PasteWordBank").Text = "word,meaning\nalpha,第一\nbeta,第二";
            ((Button)All(import).Single(c => c.Name == "ParsePastedWordBank")).PerformClick();
            ((Button)All(import).Single(c => c.Name == "ConfirmWordBankImport")).PerformClick();
            Check(import.Imported is { Words.Count: 2 }, "UI import persists confirmed rows");
        }
        initial.HasChosenWordBank = true; StateStore.Save(initial);
        var startupStore = new MemoryStartupEntryStore();
        using (var main = new MainForm(new StartupRegistration(startupStore, Environment.ProcessPath, true)))
        {
            ShowOffscreen(main);
            Check(All(main).Any(c => c.Name == "ApplicationMenuButton"), "Production form exposes application menu");
            var menu = Field<ContextMenuStrip>(main, "_applicationMenu");
            Check(menu.Items.Cast<ToolStripItem>().Any(c => c.Name == "WordBankManagerMenuItem"), "Application menu retains word bank manager");
            Check(startupStore.Writes == 0, "Showing production form does not register startup");
            var startupItem = menu.Items.Cast<ToolStripItem>().Single(c => c.Name == "EnableAutoStartMenuItem");
            Check(!menu.Items.Cast<ToolStripItem>().Any(c => c.Text?.Contains("撤销") == true || c.Text?.Contains("关闭自启动") == true), "No in-app startup removal button");
            startupItem.PerformClick();
            Check(startupStore.Writes == 1 && startupStore.Command!.EndsWith(" --portable"), "Startup menu click reaches registration");
            Check(!startupItem.Enabled && startupItem.Text?.Contains("已添加") == true, "Successful click refreshes startup menu state");
            Check(Field<Label>(main, "_statusLabel").Text.Contains(StartupRegistration.RemovalFileName), "Registration feedback identifies external removal CMD");
            startupStore.Command = null;
            typeof(MainForm).GetMethod("RefreshStartupMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
            Check(startupItem.Enabled && startupStore.Writes == 1, "Menu refresh reflects removal without registering again");
            Screenshot(main, "community-home");
            var wideSize = main.ClientSize;
            main.ClientSize = new Size(Math.Max(UiLayout.Pixels(main, 680), main.MinimumSize.Width),
                Math.Max(UiLayout.Pixels(main, 560), main.MinimumSize.Height)); Application.DoEvents();
            var menuButton = All(main).Single(c => c.Name == "ApplicationMenuButton");
            Screenshot(main, "community-home-narrow");
            Check(main.RectangleToScreen(main.ClientRectangle).Contains(menuButton.RectangleToScreen(menuButton.ClientRectangle)),
                $"Narrow main window keeps menu accessible: form={main.RectangleToScreen(main.ClientRectangle)}, menu={menuButton.RectangleToScreen(menuButton.ClientRectangle)}");
            main.ClientSize = wideSize;
            var oldId = Field<AppState>(main, "_state").ActiveWordBankId;
            main.BeginInvoke(new Action(() =>
            {
                var manager = Application.OpenForms.OfType<WordBankManagerForm>().Single();
                var list = (ListBox)All(manager).Single(c => c.Name == "WordBankList");
                list.SelectedItem = list.Items.Cast<WordBankPackage>().First(b => b.Id == "cet4-high");
                ((Button)All(manager).Single(c => c.Name == "UseSelectedWordBank")).PerformClick();
            }));
            typeof(MainForm).GetMethod("ShowWordBankManager", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
            var switched = Field<AppState>(main, "_state");
            Check(switched.ActiveWordBankId == "cet4-high" && switched.SavedWordBanks.ContainsKey(oldId), "Manager selection reaches production state transaction");
            Check(StateStore.Load().ActiveWordBankId == "cet4-high", "UI switch persisted to disk");
            Check(All(main).Single(c => c.Name == "CurrentWordBankLabel").Text.Contains(switched.ActiveWordBankName), "Current bank remains visible after changing status text");
            Screenshot(main, "community-selected-bank");
        }
        Groups.Add("production UI manager import responsive layout transactional switch");
    }
}
