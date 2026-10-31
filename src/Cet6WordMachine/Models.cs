using System.Text.Json.Serialization;

namespace Cet6WordMachine;

internal sealed class WordEntry
{
    [JsonPropertyName("word")]
    public string Word { get; set; } = "";

    [JsonPropertyName("phonetic")]
    public string Phonetic { get; set; } = "";

    [JsonPropertyName("meaning")]
    public string Meaning { get; set; } = "";

    [JsonPropertyName("hint")]
    public string Hint { get; set; } = "";

    [JsonPropertyName("example")]
    public string Example { get; set; } = "";

    [JsonPropertyName("frequencyRank")]
    public int FrequencyRank { get; set; }

    [JsonPropertyName("starred")]
    public bool Starred { get; set; }

    [JsonPropertyName("level")]
    public string Level { get; set; } = "";

    [JsonPropertyName("examMonthCount")]
    public int ExamMonthCount { get; set; }

    [JsonPropertyName("examPaperCount")]
    public int ExamPaperCount { get; set; }

    [JsonPropertyName("studyTier")]
    public string StudyTier { get; set; } = "";

    [JsonPropertyName("frequencyNote")]
    public string FrequencyNote { get; set; } = "";

    [JsonIgnore]
    public bool ExcludeFromNewLearning { get; set; }

    [JsonIgnore]
    public string FrequencyCaption => ExcludeFromNewLearning ? "" :
        !string.IsNullOrWhiteSpace(FrequencyNote) ? FrequencyNote :
        ExamMonthCount > 0 ? $"样本 {ExamMonthCount} 个考期" : "";

    [JsonIgnore]
    public string StudyCaption => string.Join(" · ", new[]
    {
        LevelCaption,
        ExcludeFromNewLearning ? "" : StudyTier,
        FrequencyCaption,
    }.Where(text => !string.IsNullOrWhiteSpace(text)));

    [JsonIgnore]
    public string LevelCaption => ExcludeFromNewLearning ? "高中范围·历史保留" : Level switch
    {
        "CET6" => "六级增量",
        "CET4" => "四级补漏",
        _ => "",
    };
}

internal sealed class WordProgress
{
    public string Word { get; set; } = "";
    public int Stage { get; set; }
    public DateOnly NextReview { get; set; }
    public DateOnly IntroducedOn { get; set; }
    public DateOnly LastReviewedOn { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
}

internal sealed class DailySession
{
    public DateOnly Date { get; set; }
    public List<string> Words { get; set; } = [];
    // Read only for migrating unfinished sessions from the review-first edition.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ReviewWords { get; set; }
    public Dictionary<string, int> Confidence { get; set; } = [];
    public Dictionary<string, string> UserAnswers { get; set; } = [];
    public Dictionary<string, int> SelfGrades { get; set; } = [];
    public int StudyIndex { get; set; }
    public int QuizIndex { get; set; }
    public int CorrectCount { get; set; }
    public int PartialCount { get; set; }
    public bool StudyFinished { get; set; }
    public bool Completed { get; set; }
    public DateTime? CompletedAt { get; set; }
}

internal enum SessionStep
{
    NewStudy,
    NewQuiz,
    Complete,
}

internal sealed class AppState
{
    public int SchemaVersion { get; set; } = 2;
    public string ActiveWordBankId { get; set; } = "cet46-high";
    public string ActiveWordBankName { get; set; } = "四六级高频词库（不含高中）";
    public bool HasChosenWordBank { get; set; }
    public List<WordEntry> ActiveWordEntries { get; set; } = [];
    public Dictionary<string, WordBankLearningState> SavedWordBanks { get; set; } = [];
    public const string DefaultExamDate = "2026-12-12";
    public string ExamDate { get; set; } = DefaultExamDate;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NewWordCompletionDate { get; set; }
    public int DailyNewWords { get; set; } = 5;
    public bool AutoStartAttempted { get; set; }
    public DateTime? FirstRunAt { get; set; }
    public List<WordProgress> Progress { get; set; } = [];
    // Personal scheduling preference; the shared dictionary and learning history
    // remain unchanged when a word is permanently skipped.
    public List<string> SkippedWords { get; set; } = [];
    public Dictionary<string, string> CustomHints { get; set; } = [];
    public DailySession? ActiveSession { get; set; }
    public List<DailySession> History { get; set; } = [];

    [JsonIgnore]
    public Dictionary<string, WordProgress> ProgressMap =>
        Progress.ToDictionary(item => item.Word, StringComparer.OrdinalIgnoreCase);
}

// A bank owns its cards and complete learning state. Switching never rewrites an
// unfinished task or its answers. Card snapshots also protect learned meanings.
internal sealed class WordBankLearningState
{
    public string Name { get; set; } = "";
    public List<WordEntry> Entries { get; set; } = [];
    public List<WordProgress> Progress { get; set; } = [];
    public List<string> SkippedWords { get; set; } = [];
    public Dictionary<string, string> CustomHints { get; set; } = [];
    public DailySession? ActiveSession { get; set; }
    public List<DailySession> History { get; set; } = [];
}

internal enum NewWordPlanStatus
{
    Normal,
    CustomDeadline,
    ReducedRevisionTime,
    ExceedsDailyLimit,
    NoStudyDays,
    InvalidDate,
    AllLearned,
    FinishCurrentSession,
}

internal sealed record NewWordPlan(
    DateOnly Today,
    DateOnly? ExamDate,
    DateOnly FirstAdjustableDay,
    int RemainingWords,
    int PendingNewWordsToday,
    int WordsForFutureQuota,
    int IntroducedBeforeFirstDay,
    int AvailableDays,
    int RevisionDays,
    int RequiredPerDay,
    bool CanApplyToday,
    NewWordPlanStatus Status)
{
    public DateOnly? CompletionDate { get; init; }
    public bool HasCustomDeadline { get; init; }
    public bool CanApply => Status is NewWordPlanStatus.Normal or NewWordPlanStatus.CustomDeadline or NewWordPlanStatus.ReducedRevisionTime;

    public DateOnly? ProjectedFinish(int dailyQuota)
    {
        if (RemainingWords == 0 || WordsForFutureQuota == 0) return Today;
        if (dailyQuota <= 0) return null;
        var firstCapacity = Math.Max(0, dailyQuota - IntroducedBeforeFirstDay);
        var extra = Math.Max(0, WordsForFutureQuota - firstCapacity);
        var additionalDays = extra == 0 ? 0 : (int)Math.Ceiling(extra / (double)dailyQuota);
        var dayNumber = (long)FirstAdjustableDay.DayNumber + additionalDays;
        return dayNumber > DateOnly.MaxValue.DayNumber ? null : DateOnly.FromDayNumber((int)dayNumber);
    }
}
