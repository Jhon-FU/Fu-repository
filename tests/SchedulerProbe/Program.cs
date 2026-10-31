using System.Text.Json;
using Cet6WordMachine;

var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
// Synthetic fixed-size data tests scheduling arithmetic without personal cards.
var bank = Enumerable.Range(0, 542).Select(i => new WordEntry
{
    Word = "fixture" + i.ToString("D4"), Meaning = "Synthetic meaning " + i,
}).ToList();

var engine = new StudyEngine(bank);
var state = new AppState();
var dayOne = new DateOnly(2026, 9, 12);
var dayTwo = new DateOnly(2026, 9, 13);

var firstSession = engine.GetOrCreateSession(state, dayOne);
Assert(firstSession.Words.Count == 5, "Day one should introduce 5 new words.");
foreach (var word in firstSession.Words)
{
    engine.RateCard(firstSession, word, 0);
    engine.SubmitAnswer(state, firstSession, word, "__wrong__", dayOne);
}
engine.CompleteSession(state, firstSession);

Assert(engine.DueCount(state, dayTwo) == 5, "All five missed words should be due on day two.");
Assert(engine.LearnedCount(state) == 5, "Five words should be recorded after day one.");

var secondSession = engine.GetOrCreateSession(state, dayTwo);
Assert(secondSession.Words.Count == 10, "Day two should contain 5 reviews and 5 new words.");
Assert(
    secondSession.Words.Take(5).All(word => state.Progress.Any(item => item.Word == word)),
    "Review words should be placed before new words.");
Assert(
    secondSession.Words.Skip(5).All(word => state.Progress.All(item => item.Word != word)),
    "The final five words should be new.");

Assert(engine.GetSessionStep(secondSession) == SessionStep.NewStudy && secondSession.StudyIndex == 0,
    "Reviews and new words must all be studied before testing.");
foreach (var word in secondSession.Words) engine.RateCard(secondSession, word, 1);
Assert(engine.GetSessionStep(secondSession) == SessionStep.NewQuiz,
    "The final combined quiz must follow all study cards.");
foreach (var word in secondSession.Words)
{
    Assert(engine.GetQuizWord(secondSession) == word, "The quiz must include reviews then new words.");
    engine.SubmitSelfAssessment(state, secondSession, word, "remembered", 2, dayTwo);
}
Assert(engine.GetSessionStep(secondSession) == SessionStep.Complete && secondSession.CorrectCount == 10,
    "All ten words must be counted exactly once.");

var oldState = new AppState
{
    ActiveSession = new DailySession
    {
        Date = dayTwo, Words = [bank[0].Word, bank[1].Word, bank[2].Word],
        ReviewWords = [bank[0].Word, bank[1].Word], StudyIndex = 2, QuizIndex = 1, CorrectCount = 1,
        SelfGrades = new() { [bank[0].Word] = 2 },
        UserAnswers = new() { [bank[0].Word] = "saved answer" },
    },
};
var migrated = engine.GetOrCreateSession(oldState, dayTwo);
Assert(migrated.ReviewWords is null && migrated.StudyIndex == 1 && migrated.QuizIndex == 1,
    "An unfinished review-first session must resume remaining study cards.");
Assert(migrated.UserAnswers[bank[0].Word] == "saved answer" && migrated.CorrectCount == 1,
    "Migration must preserve the saved answer and score.");
foreach (var word in migrated.Words.Skip(1)) engine.RateCard(migrated, word, 1);
Assert(engine.GetQuizWord(migrated) == bank[1].Word,
    "Already graded reviews must not be graded twice.");
var resumed = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(oldState))!;
Assert(engine.GetSessionStep(engine.GetOrCreateSession(resumed, dayTwo)) == SessionStep.NewQuiz,
    "Restarting must retain the unified quiz phase.");
var crossed = engine.GetOrCreateSession(resumed, dayTwo.AddDays(1));
Assert(resumed.History.Any(item => item.UserAnswers.GetValueOrDefault(bank[0].Word) == "saved answer"),
    "Crossing midnight must archive completed answers without changing yesterday's task date.");
Assert(crossed.Date == dayTwo.AddDays(1) && crossed.QuizIndex == 0 && crossed.StudyIndex == 0,
    "A new day must have independent study and quiz cursors.");

var unstarted = new DailySession
{
    Date = dayTwo, Words = [bank[0].Word, bank[1].Word], ReviewWords = [bank[0].Word], StudyIndex = 1,
};
Assert(engine.GetSessionStep(unstarted) == SessionStep.NewStudy && unstarted.StudyIndex == 0,
    "An unstarted review-first session must restore the first review card.");
var legacy = new DailySession { Date = dayTwo, Words = [bank[0].Word], StudyFinished = true };
Assert(engine.GetSessionStep(legacy) == SessionStep.NewQuiz,
    "Legacy completed learning must not restart.");

var yesterday = new DateOnly(2026, 10, 8);
var today = yesterday.AddDays(1);
var unfinished = new DailySession
{
    Date = yesterday, Words = bank.Take(10).Select(item => item.Word).ToList(),
    StudyIndex = 10, StudyFinished = true, QuizIndex = 8, CorrectCount = 8,
};
foreach (var word in unfinished.Words)
    unfinished.Confidence[word] = 2;
foreach (var word in unfinished.Words.Take(8))
{
    unfinished.SelfGrades[word] = 2;
    unfinished.UserAnswers[word] = "yesterday's saved answer";
}
var rollover = new AppState
{
    DailyNewWords = 6, ActiveSession = unfinished,
    Progress = bank.Take(10).Select(item => new WordProgress
    {
        Word = item.Word, IntroducedOn = yesterday.AddDays(-2),
        LastReviewedOn = yesterday, NextReview = today,
    }).ToList(),
};
var todaySession = engine.GetOrCreateSession(rollover, today);
Assert(todaySession.Words.Count == 16 && todaySession.QuizIndex == 0 && todaySession.StudyIndex == 0,
    "A carried quiz with two questions remaining must generate ten due reviews plus six new words today.");
Assert(rollover.History.Single().Date == yesterday && unfinished.QuizIndex == 8,
    "Yesterday's unfinished task must retain its date, answers, and cursor.");
Assert(ReferenceEquals(engine.GetOrCreateSession(rollover, today), todaySession),
    "Restarting today must reuse the new task rather than add duplicate new words.");
try
{
    engine.CompleteSession(rollover, todaySession);
    throw new Exception("An unfinished task was incorrectly completed.");
}
catch (InvalidOperationException) { }

// Reproduce an old build that already overwrote yesterday's date and completed it.
unfinished.Date = today;
unfinished.QuizIndex = unfinished.Words.Count;
unfinished.Completed = true;
rollover.ActiveSession = null;
var repaired = engine.GetOrCreateSession(rollover, today);
Assert(repaired.Words.Count == 16 && !repaired.Completed,
    "An incorrectly completed carryover record must not block today's reviews and new words.");
Assert(rollover.History.Contains(unfinished) && unfinished.UserAnswers.Count == 8,
    "Repair must leave the historical record intact.");

var finishedYesterday = new DailySession
{
    Date = yesterday, Words = bank.Take(10).Select(item => item.Word).ToList(),
    StudyFinished = true, StudyIndex = 10, QuizIndex = 10, Completed = true,
};
foreach (var word in finishedYesterday.Words) finishedYesterday.SelfGrades[word] = 2;
var completedRollover = new AppState
{
    DailyNewWords = 6, History = [finishedYesterday],
    Progress = bank.Take(10).Select(item => new WordProgress
    {
        Word = item.Word, IntroducedOn = yesterday.AddDays(-2), LastReviewedOn = yesterday, NextReview = today,
    }).ToList(),
};
var afterResults = engine.GetOrCreateSession(completedRollover, today);
Assert(afterResults.Words.Count == 16 && finishedYesterday.Date == yesterday && finishedYesterday.Completed,
    "Seeing and saving yesterday's results must still create a complete independent task today.");
foreach (var word in afterResults.Words) engine.RateCard(afterResults, word, 2);
foreach (var word in afterResults.Words)
    engine.SubmitSelfAssessment(completedRollover, afterResults, word, "today", 2, today);
engine.CompleteSession(completedRollover, afterResults);
Assert(ReferenceEquals(engine.GetOrCreateSession(completedRollover, today), afterResults),
    "A correctly completed task today must not generate extra tasks on restart.");

// Exercise every existing stage and explicit/missing pre-reveal confidence.
int[] intervals = [1, 2, 4, 7, 15, 30, 60];
foreach (var stage in Enumerable.Range(0, intervals.Length))
foreach (var confidence in new[] { -1, 0, 1, 2 })
foreach (var grade in new[] { 0, 1, 2 })
{
    var word = bank[0].Word;
    var sample = new AppState { Progress = [new WordProgress {
        Word = word, IntroducedOn = dayOne.AddDays(-30), LastReviewedOn = dayOne.AddDays(-1),
        NextReview = dayOne, Stage = stage, CorrectCount = 10, WrongCount = 3 }] };
    var task = new DailySession { Date = dayOne, Words = [word] };
    if (confidence >= 0) task.Confidence[word] = confidence;
    engine.SubmitSelfAssessment(sample, task, word, "answer", grade, dayOne);
    var expectedStage = grade == 2 ? (confidence == 0 ? 0 : Math.Min(stage + 1, 6))
        : grade == 1 ? Math.Max(0, stage - 1) : 0;
    Assert(sample.Progress[0].Stage == expectedStage &&
           sample.Progress[0].NextReview == dayOne.AddDays(intervals[expectedStage]),
        "Completely forgotten words and explicitly unknown old words relearn next day; other schedules stay unchanged.");
    Assert(sample.Progress[0].CorrectCount == 10 + (grade == 2 ? 1 : 0) &&
           sample.Progress[0].WrongCount == 3 + (grade == 2 ? 0 : 1) && task.QuizIndex == 1,
        "Scheduling must not rewrite prior counts or count the new answer twice.");
    if (grade == 0)
    {
        task.StudyFinished = true;
        task.StudyIndex = task.Words.Count;
        engine.CompleteSession(sample, task);
        var restarted = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(sample))!;
        var nextDay = engine.GetOrCreateSession(restarted, dayOne.AddDays(1));
        Assert(nextDay.Words.Contains(word) && restarted.Progress[0].IntroducedOn == dayOne.AddDays(-30),
            "A forgotten word must actually reappear after restarting tomorrow, without erasing its history.");
        nextDay.Confidence[word] = 2;
        engine.SubmitSelfAssessment(restarted, nextDay, word, "remembered tomorrow", 2, dayOne.AddDays(1));
        Assert(restarted.Progress[0].Stage == 1 && restarted.Progress[0].NextReview == dayOne.AddDays(3),
            "A forgotten word recalled correctly tomorrow must resume the normal two-day interval.");
    }
}
foreach (var confidence in new[] { 0, 1, 2 })
foreach (var grade in new[] { 0, 1, 2 })
{
    var fresh = new AppState();
    var task = new DailySession { Date = dayOne, Words = [bank[0].Word],
        Confidence = new() { [bank[0].Word] = confidence } };
    engine.SubmitSelfAssessment(fresh, task, bank[0].Word, "answer", grade, dayOne);
    Assert(fresh.Progress[0].NextReview == dayOne.AddDays(grade == 2 && confidence == 2 ? 2 : 1),
        "First-time learning must keep its existing schedule.");
}
foreach (var stage in Enumerable.Range(0, intervals.Length))
foreach (var confidence in new[] { -1, 0, 1, 2 })
foreach (var correct in new[] { false, true })
{
    var word = bank[0].Word;
    var stateForChoice = new AppState { Progress = [new WordProgress {
        Word = word, Stage = stage, IntroducedOn = dayOne.AddDays(-100),
        LastReviewedOn = dayOne.AddDays(-1), NextReview = dayOne, CorrectCount = 8, WrongCount = 4 }] };
    var task = new DailySession { Date = dayOne, Words = [word] };
    if (confidence >= 0) task.Confidence[word] = confidence;
    var answer = correct ? StudyEngine.FormatOptionMeaning(bank[0].Meaning) : "__wrong__";
    Assert(engine.SubmitAnswer(stateForChoice, task, word, answer, dayOne) == correct,
        "The legacy choice path must still report the original correctness.");
    var expected = correct ? (confidence == 0 ? 0 : Math.Min(stage + 1, 6)) : 0;
    var progress = stateForChoice.Progress[0];
    Assert(progress.Stage == expected && progress.NextReview == dayOne.AddDays(intervals[expected]) &&
           progress.CorrectCount == 8 + (correct ? 1 : 0) && progress.WrongCount == 4 + (correct ? 0 : 1),
        "Legacy wrong answers must also return tomorrow; correct answers and lifetime counts stay consistent.");
}
Console.WriteLine("PASS: all stages and confidence levels schedule forgotten words for tomorrow; partial/correct and new-word rules unchanged");
Console.WriteLine("PASS: forgotten words survive restart, actually return tomorrow, and resume normal intervals after successful recall");

foreach (var quota in new[] { 1, 15, 20, 50, 100 })
{
    var quotaState = new AppState { DailyNewWords = quota };
    var quotaSession = engine.GetOrCreateSession(quotaState, dayOne);
    Assert(quotaSession.Words.Count == Math.Min(quota, StudyEngine.MaximumNewWordsPerDay),
        "Daily quotas above ten must work and the configured maximum must be enforced.");
    Assert(engine.TodayNewCount(new AppState { DailyNewWords = quota }, dayOne) == quotaSession.Words.Count,
        "Home new-word count must agree with the actual session quota.");
}
var partialQuota = new AppState
{
    DailyNewWords = 15,
    Progress = bank.Take(4).Select(item => new WordProgress
    {
        Word = item.Word, IntroducedOn = dayOne, LastReviewedOn = dayOne, NextReview = dayTwo,
    }).ToList(),
};
Assert(engine.GetOrCreateSession(partialQuota, dayOne).Words.Count == 11,
    "Increasing the daily quota must still deduct words already introduced today.");
Console.WriteLine("PASS: 15/20/50-word quotas, upper bound, home counts and same-day deduplication");



var planDate = new DateOnly(2026, 10, 30);
AppState PlanningState(int learned, bool completedToday = false)
{
    var result = new AppState
    {
        ExamDate = "2026-12-12",
        Progress = bank.Take(learned).Select(item => new WordProgress
        {
            Word = item.Word, IntroducedOn = planDate.AddDays(-3),
            LastReviewedOn = planDate.AddDays(-1), NextReview = planDate.AddDays(2),
        }).ToList(),
    };
    if (completedToday) result.History.Add(new DailySession { Date = planDate, Completed = true, StudyFinished = true });
    return result;
}
var personal = PlanningState(253, completedToday: true);
var personalBefore = JsonSerializer.Serialize(personal);
var personalPlan = engine.GetNewWordPlan(personal, planDate);
Assert(personalPlan.RemainingWords == 289 && personalPlan.AvailableDays == 32 && personalPlan.RequiredPerDay == 10 &&
       personalPlan.FirstAdjustableDay == new DateOnly(2026, 10, 31) && personalPlan.ProjectedFinish(10) == new DateOnly(2026, 11, 28),
    "The completed real account must use tomorrow, its own 289 remaining words, and ten full revision days.");
var testAccount = PlanningState(123);
var testPlan = engine.GetNewWordPlan(testAccount, planDate);
Assert(testPlan.RemainingWords == 419 && testPlan.AvailableDays == 33 && testPlan.RequiredPerDay == 13,
    "Different progress must produce an independent recommendation.");
var later = engine.GetNewWordPlan(testAccount, new DateOnly(2026, 11, 20));
Assert(later.RequiredPerDay == 35 && later.RevisionDays == 10 && later.ProjectedFinish(35) == new DateOnly(2026, 12, 1),
    "Advancing the simulated date without learning must increase the recommendation.");
var reduced = engine.GetNewWordPlan(testAccount, new DateOnly(2026, 11, 30));
Assert(reduced.Status == NewWordPlanStatus.ReducedRevisionTime && reduced.RequiredPerDay == 35 && reduced.CanApply,
    "When ten revision days cannot be kept, use an explicit exam-only target rather than a false promise.");
var impossible = engine.GetNewWordPlan(testAccount, new DateOnly(2026, 12, 5));
Assert(impossible.RequiredPerDay == 60 && !impossible.CanApply && impossible.Status == NewWordPlanStatus.ExceedsDailyLimit,
    "An impossible plan must show the required 60, not silently cap it at 50 and claim feasibility.");
foreach (var date in new[] { new DateOnly(2026, 12, 12), new DateOnly(2026, 12, 15) })
    Assert(engine.GetNewWordPlan(testAccount, date).Status == NewWordPlanStatus.NoStudyDays,
        "Exam day and past deadlines must not be counted as available study days.");
var invalid = PlanningState(123); invalid.ExamDate = "not-a-date";
Assert(engine.GetNewWordPlan(invalid, planDate).Status == NewWordPlanStatus.InvalidDate,
    "Invalid dates must not silently become a fictional 90-day plan.");
Assert(engine.GetNewWordPlan(PlanningState(bank.Count), planDate).Status == NewWordPlanStatus.AllLearned,
    "A finished bank must not recommend additional new words.");

var inProgress = PlanningState(253);
inProgress.ActiveSession = new DailySession
{
    Date = planDate, Words = bank.Skip(253).Take(6).Select(item => item.Word).ToList(),
    StudyIndex = 1, Confidence = new() { [bank[253].Word] = 1 },
};
var activeBefore = JsonSerializer.Serialize(inProgress);
var activePlan = engine.GetNewWordPlan(inProgress, planDate);
Assert(activePlan.PendingNewWordsToday == 6 && activePlan.WordsForFutureQuota == 283 && !activePlan.CanApplyToday &&
       activePlan.FirstAdjustableDay == planDate.AddDays(1) && activePlan.RequiredPerDay == 9,
    "Already-started new words must stay in today's task and must not be counted twice in tomorrow's quota.");
var onlyToday = PlanningState(bank.Count - 2);
onlyToday.ActiveSession = new DailySession { Date = planDate, Words = bank.TakeLast(2).Select(w => w.Word).ToList(), StudyIndex = 1 };
Assert(engine.GetNewWordPlan(onlyToday, planDate).Status == NewWordPlanStatus.FinishCurrentSession,
    "When all remaining words are in today's fixed task, finish it rather than recommending fake new words.");
var partiallyIntroduced = PlanningState(253);
foreach (var progress in partiallyIntroduced.Progress.Take(20)) progress.IntroducedOn = planDate;
var firstDayPlan = engine.GetNewWordPlan(partiallyIntroduced, planDate);
Assert(firstDayPlan.IntroducedBeforeFirstDay == 20 && firstDayPlan.ProjectedFinish(15) == new DateOnly(2026, 11, 19),
    "Projection must account for a quota already consumed today, including a quota lower than today's consumed amount.");

foreach (var learnedCount in new[] { 0, 123, 253, 400, bank.Count - 1 })
foreach (var daysLeft in new[] { 1, 2, 7, 11, 12, 22, 43, 70 })
foreach (var completed in new[] { false, true })
{
    var stateForPlan = PlanningState(learnedCount, completed);
    stateForPlan.ExamDate = planDate.AddDays(daysLeft).ToString("yyyy-MM-dd");
    var check = engine.GetNewWordPlan(stateForPlan, planDate);
    if (!check.CanApply) continue;
    var finish = check.ProjectedFinish(check.RequiredPerDay)!.Value;
    Assert(finish < check.ExamDate!.Value.AddDays(-check.RevisionDays),
        "Every offered recommendation must actually finish within its advertised deadline.");
    if (check.RequiredPerDay > 1)
        Assert(check.ProjectedFinish(check.RequiredPerDay - 1) >= check.ExamDate.Value.AddDays(-check.RevisionDays),
            "The advertised minimum must not be higher than necessary.");
}
Assert(JsonSerializer.Serialize(personal) == personalBefore && JsonSerializer.Serialize(inProgress) == activeBefore,
    "Planning must be read-only for all progress, answers, histories and hints.");
Console.WriteLine("PASS: dynamic planning across 80 deadline/progress scenarios; real/test accounts, active tasks, invalid/past dates and impossible quotas");
Console.WriteLine($"PLAN examples: real={personalPlan.RequiredPerDay}/day, test={testPlan.RequiredPerDay}/day, test on Nov20={later.RequiredPerDay}/day, Dec05={impossible.RequiredPerDay}/day (blocked)");

var customState = PlanningState(253, completedToday: true);
customState.NewWordCompletionDate = "2026-11-15";
var customBefore = JsonSerializer.Serialize(customState);
var customPlan = engine.GetNewWordPlan(customState, planDate);
Assert(customPlan.HasCustomDeadline && customPlan.Status == NewWordPlanStatus.CustomDeadline &&
       customPlan.CompletionDate == new DateOnly(2026, 11, 15) && customPlan.AvailableDays == 16 &&
       customPlan.RequiredPerDay == 19 && customPlan.ProjectedFinish(19) == new DateOnly(2026, 11, 15),
    "A custom date includes its last day, starts tomorrow for a completed task, and is not shortened by a second revision buffer.");
Assert(JsonSerializer.Serialize(customState) == customBefore, "Custom planning must not mutate the account.");
customState.NewWordCompletionDate = "2026-10-31";
Assert(engine.GetNewWordPlan(customState, planDate) is { RequiredPerDay: 289, CanApply: false },
    "An impossible custom deadline must report the real required quota without extending the date.");
foreach (var deadline in new[] { "2026-10-29", "2026-10-30" })
{
    customState.NewWordCompletionDate = deadline;
    Assert(engine.GetNewWordPlan(customState, planDate).Status == NewWordPlanStatus.NoStudyDays,
        "A past date or completed today leaves no adjustable days.");
}
customState.NewWordCompletionDate = "invalid";
Assert(engine.GetNewWordPlan(customState, planDate).Status == NewWordPlanStatus.InvalidDate,
    "Malformed custom dates must not silently select an automatic plan.");
customState.NewWordCompletionDate = "2026-12-15";
Assert(engine.GetNewWordPlan(customState, planDate) is { CanApply: true, RevisionDays: 0, HasCustomDeadline: true },
    "A later custom target is allowed explicitly and must never invent pre-exam revision days.");
customState.ExamDate = "invalid";
Assert(engine.GetNewWordPlan(customState, planDate).CanApply,
    "Custom completion planning is independent of the exam date.");
onlyToday.NewWordCompletionDate = planDate.ToString("yyyy-MM-dd");
Assert(engine.GetNewWordPlan(onlyToday, planDate).Status == NewWordPlanStatus.FinishCurrentSession,
    "Today's fixed pending words can finish on an inclusive today deadline.");
inProgress.NewWordCompletionDate = "2026-11-15";
Assert(engine.GetNewWordPlan(inProgress, planDate) is { PendingNewWordsToday: 6, WordsForFutureQuota: 283, RequiredPerDay: 18 },
    "A custom target must preserve today's fixed cards and deduct them once.");
foreach (var learnedCount in new[] { 0, 123, 253, 400, bank.Count - 1 })
foreach (var daysAway in new[] { 0, 1, 7, 16, 32, 46 })
foreach (var completed in new[] { false, true })
foreach (var consumedToday in new[] { 0, 20 })
{
    var candidate = PlanningState(learnedCount, completed);
    foreach (var progress in candidate.Progress.Take(consumedToday)) progress.IntroducedOn = planDate;
    var deadline = planDate.AddDays(daysAway);
    candidate.NewWordCompletionDate = deadline.ToString("yyyy-MM-dd");
    var check = engine.GetNewWordPlan(candidate, planDate);
    if (!check.CanApply) continue;
    Assert(check.ProjectedFinish(check.RequiredPerDay) <= deadline,
        "Every offered custom recommendation must meet the exact selected inclusive deadline.");
    if (check.RequiredPerDay > 1)
        Assert(check.ProjectedFinish(check.RequiredPerDay - 1) > deadline,
            "Custom recommendations must be the actual minimum, accounting for consumed quota.");
}
Console.WriteLine("PASS: custom deadlines across 120 date/progress/quota scenarios, inclusive today, active sessions, invalid/past dates and limits");


Console.WriteLine("PASS: restored flow, migration, independent day tasks, falsely completed carryover and completion guard");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
