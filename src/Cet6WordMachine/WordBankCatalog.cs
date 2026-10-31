using System.Reflection;
using System.Text.Json;

namespace Cet6WordMachine;

internal sealed class WordBankPackage
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "";
    public string License { get; set; } = "";
    public string Version { get; set; } = "";
    public List<WordEntry> Words { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsImported { get; set; }
}

internal sealed record WordBankCatalogResult(List<WordBankPackage> Banks, List<string> Warnings);

internal static class WordBankCatalog
{
    public const string DefaultId = "cet46-high";
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string ImportDirectory => Path.Combine(StateStore.DataDirectory, "wordbanks");

    public static WordBankCatalogResult Load()
    {
        var banks = new List<WordBankPackage>();
        var warnings = new List<string>();
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".bank.json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var bank = JsonSerializer.Deserialize<WordBankPackage>(stream, JsonOptions)
                ?? throw new InvalidDataException("内置词库为空。");
            Validate(bank);
            banks.Add(bank);
        }
        if (Directory.Exists(ImportDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(ImportDirectory, "*.json"))
            {
                try
                {
                    // The normalized installed JSON can be larger than its
                    // compact CSV/XLSX input. Keep those limits distinct.
                    if (new FileInfo(file).Length > 128 * 1024 * 1024)
                        throw new InvalidDataException("文件超过大小限制。");
                    var bank = JsonSerializer.Deserialize<WordBankPackage>(File.ReadAllText(file), JsonOptions)
                        ?? throw new InvalidDataException("词库为空。");
                    Validate(bank);
                    if (!bank.Id.StartsWith("import-", StringComparison.Ordinal) || banks.Any(b => b.Id == bank.Id))
                        throw new InvalidDataException("导入词库标识重复或无效。");
                    bank.IsImported = true;
                    banks.Add(bank);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
                {
                    warnings.Add($"{Path.GetFileName(file)}：{ex.Message}");
                }
            }
        }
        string[] order = ["cet4-pure", "cet4-high", "cet6-pure", "cet6-high", "cet46-high"];
        return new(banks.OrderBy(b => b.IsImported ? 100 : Array.IndexOf(order, b.Id)).ThenBy(b => b.Name).ToList(), warnings);
    }

    public static WordBankPackage GetDefault() => Load().Banks.FirstOrDefault(b => b.Id == DefaultId)
        ?? throw new InvalidDataException("未找到默认推荐词库，请重新安装完整程序。");

    public static void Validate(WordBankPackage bank)
    {
        if (bank.FormatVersion != 1) throw new InvalidDataException("不支持此词库版本。");
        if (string.IsNullOrWhiteSpace(bank.Id) || string.IsNullOrWhiteSpace(bank.Name))
            throw new InvalidDataException("词库缺少名称或标识。");
        if (bank.Words is null || bank.Words.Count == 0 || bank.Words.Count > WordBankImporter.MaximumWords)
            throw new InvalidDataException("词库词数必须在 1 至 50,000 之间。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in bank.Words)
        {
            WordBankImporter.ValidateEntry(word);
            if (!seen.Add(word.Word)) throw new InvalidDataException($"词库含重复单词：{word.Word}");
        }
    }

    public static WordBankPackage Install(WordBankImportResult preview, string name)
    {
        if (!preview.CanImport) throw new InvalidDataException("请修正导入错误后重试。");
        name = name.Trim();
        if (name.Length is 0 or > 80) throw new InvalidDataException("词库名称需为 1 至 80 个字符。");
        var package = new WordBankPackage
        {
            Id = "import-" + Guid.NewGuid().ToString("N"), Name = name,
            Description = "用户导入；顺序与导入文件一致。", Source = "用户自备词库",
            License = "由导入者确认内容使用权限", Version = DateTime.Now.ToString("yyyy-MM-dd"),
            Words = preview.Words, IsImported = true,
        };
        Validate(package);
        Directory.CreateDirectory(ImportDirectory);
        var target = Path.Combine(ImportDirectory, package.Id + ".json");
        var temporary = target + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, package, JsonOptions);
                stream.Flush(true);
            }
            File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return package;
    }

    public static AppState Switch(AppState current, WordBankPackage selected, bool reuseMatchingProgress)
    {
        Validate(selected);
        var next = StateStore.CopyForUpdate(current);
        if (next.ActiveWordEntries.Count == 0) next.ActiveWordEntries = StateStore.LoadWordBank(next);
        if (next.ActiveWordBankId == selected.Id)
        {
            next.HasChosenWordBank = true;
            return next;
        }
        next.SavedWordBanks[next.ActiveWordBankId] = new WordBankLearningState
        {
            Name = next.ActiveWordBankName, Entries = next.ActiveWordEntries,
            Progress = next.Progress, SkippedWords = next.SkippedWords,
            CustomHints = next.CustomHints, ActiveSession = next.ActiveSession, History = next.History,
        };
        if (!next.SavedWordBanks.Remove(selected.Id, out var restored))
        {
            restored = new WordBankLearningState
            {
                Name = selected.Name,
                Entries = JsonSerializer.Deserialize<List<WordEntry>>(JsonSerializer.Serialize(selected.Words, JsonOptions), JsonOptions)!,
            };
            if (reuseMatchingProgress)
            {
                // Carry progress only for exactly matching word AND meaning. A
                // different sense must not silently be treated as already learned.
                var sources = next.SavedWordBanks.Values.ToList();
                var matches = new Dictionary<string, (WordBankLearningState Bank, WordProgress Progress)>(StringComparer.OrdinalIgnoreCase);
                var targets = restored.Entries.ToDictionary(w => w.Word, StringComparer.OrdinalIgnoreCase);
                foreach (var source in sources)
                {
                    var entries = source.Entries.ToDictionary(w => w.Word, StringComparer.OrdinalIgnoreCase);
                    foreach (var progress in source.Progress)
                    {
                        if (!targets.TryGetValue(progress.Word, out var target) || !entries.TryGetValue(progress.Word, out var old)
                            || target.Meaning.Trim() != old.Meaning.Trim()) continue;
                        if (!matches.TryGetValue(progress.Word, out var previous) || progress.LastReviewedOn > previous.Progress.LastReviewedOn)
                            matches[progress.Word] = (source, progress);
                    }
                }
                foreach (var (word, match) in matches)
                {
                    restored.Progress.Add(JsonSerializer.Deserialize<WordProgress>(JsonSerializer.Serialize(match.Progress))!);
                    if (match.Bank.CustomHints.TryGetValue(word, out var hint)) restored.CustomHints[word] = hint;
                }
            }
        }
        next.ActiveWordBankId = selected.Id;
        next.ActiveWordBankName = restored.Name;
        next.ActiveWordEntries = restored.Entries;
        next.Progress = restored.Progress;
        next.SkippedWords = restored.SkippedWords;
        next.CustomHints = restored.CustomHints;
        next.ActiveSession = restored.ActiveSession;
        next.History = restored.History;
        next.HasChosenWordBank = true;
        return next;
    }
}
