using System.Text.Json;

namespace Cet6WordMachine;

internal static class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string DataDirectory
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable(
                "WORD_MACHINE_COMMUNITY_DATA_DIR");
            return !string.IsNullOrWhiteSpace(overridePath)
                ? overridePath
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WordMachineCommunity");
        }
    }

    public static string StatePath => Path.Combine(DataDirectory, "progress.json");

    public static List<WordEntry> LoadWordBank(AppState? state = null)
    {
        if (state is { ActiveWordEntries.Count: > 0 }) return state.ActiveWordEntries;
        var package = WordBankCatalog.GetDefault();
        if (state is not null)
        {
            state.ActiveWordBankId = package.Id;
            state.ActiveWordBankName = package.Name;
            state.ActiveWordEntries = package.Words;
        }
        return package.Words;
    }

    public static AppState Load()
    {
        Directory.CreateDirectory(DataDirectory);
        try
        {
            var state = JsonSerializer.Deserialize<AppState>(
                File.ReadAllText(StatePath),
                JsonOptions) ?? throw new JsonException("存档内容为空。");
            state.Progress ??= [];
            state.SkippedWords = (state.SkippedWords ?? [])
                .Where(word => !string.IsNullOrWhiteSpace(word))
                .Select(word => word.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            state.CustomHints ??= [];
            state.History ??= [];
            state.SavedWordBanks ??= [];
            state.ActiveWordEntries ??= [];
            if (state.SchemaVersion != 2) throw new InvalidDataException("此存档版本不受支持。");
            if (state.ActiveWordEntries.Count == 0 && (state.Progress.Count > 0 || state.History.Count > 0 || state.ActiveSession is not null))
                throw new InvalidDataException("存档缺少对应词库快照，未重置记录。社区版不能直接载入个人版存档。");
            LoadWordBank(state);
            WordBankCatalog.Validate(new WordBankPackage { Id = state.ActiveWordBankId, Name = state.ActiveWordBankName, Words = state.ActiveWordEntries });
            ValidateState(state);
            // Correct the previous bundled default; all learning records stay intact.
            // NEEA: https://cet.neea.edu.cn/html1/report/2609/1-1.htm
            if (state.ExamDate == "2026-12-11") state.ExamDate = AppState.DefaultExamDate;
            return state;
        }
        catch (FileNotFoundException) when (!File.Exists(StatePath + ".bak"))
        {
            var state = new AppState { FirstRunAt = DateTime.Now };
            LoadWordBank(state);
            return state;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Never turn an unreadable existing account into a new, empty account.
            throw new InvalidDataException(
                $"学习记录无法读取，程序已停止载入，未重置或覆盖原存档。\n存档位置：{StatePath}\n" +
                "请检查文件是否被占用或损坏；如有 progress.json.bak，可用它协助恢复。",
                exception);
        }
    }

    public static AppState CopyForUpdate(AppState state) =>
        JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state, JsonOptions), JsonOptions)!;

    public static void Save(AppState state)
    {
        ValidateState(state);
        Directory.CreateDirectory(DataDirectory);
        var temporary = StatePath + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, state, JsonOptions);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(StatePath))
        {
            // Atomically install the new state and retain the previous saved version.
            File.Replace(temporary, StatePath, StatePath + ".bak");
        }
        else
        {
            File.Move(temporary, StatePath);
        }
    }

    private static void ValidateState(AppState state)
    {
        void ValidateBank(string id, string name, List<WordEntry>? entries, List<WordProgress>? progress,
            List<DailySession>? history, DailySession? session)
        {
            if (entries is null || progress is null || history is null) throw new InvalidDataException("存档词库结构不完整。");
            WordBankCatalog.Validate(new WordBankPackage { Id = id, Name = name, Words = entries });
            var words = entries.Select(e => e.Word).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (progress.Any(p => p is null || !words.Contains(p.Word)) || progress.Select(p => p.Word).Distinct(StringComparer.OrdinalIgnoreCase).Count() != progress.Count)
                throw new InvalidDataException("学习记录与词库快照不匹配，未覆盖原存档。");
            foreach (var task in history.Concat(session is null ? [] : new[] { session }))
            {
                if (task is null || task.Words is null || task.Confidence is null || task.UserAnswers is null || task.SelfGrades is null || task.Words.Any(w => !words.Contains(w)))
                    throw new InvalidDataException("任务记录与词库快照不匹配，未覆盖原存档。");
            }
        }
        if (state.SchemaVersion != 2 || state.SavedWordBanks is null) throw new InvalidDataException("存档版本或结构无效。");
        ValidateBank(state.ActiveWordBankId, state.ActiveWordBankName, state.ActiveWordEntries, state.Progress, state.History, state.ActiveSession);
        foreach (var (id, saved) in state.SavedWordBanks)
        {
            if (saved is null || saved.SkippedWords is null || saved.CustomHints is null || id == state.ActiveWordBankId)
                throw new InvalidDataException("已保存词库结构无效。");
            ValidateBank(id, saved.Name, saved.Entries, saved.Progress, saved.History, saved.ActiveSession);
        }
    }
}
