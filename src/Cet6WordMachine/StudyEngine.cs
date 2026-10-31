using System.Text.RegularExpressions;

namespace Cet6WordMachine;

internal sealed class StudyEngine
{
    public const int DefaultNewWordsPerDay = 5;
    public const int MaximumNewWordsPerDay = 50;
    public const int TargetRevisionDays = 10;
    private static readonly int[] ReviewIntervals = [1, 2, 4, 7, 15, 30, 60];

    private readonly List<WordEntry> _bank;
    private readonly Dictionary<string, WordEntry> _bankMap;

    public StudyEngine(List<WordEntry> bank)
    {
        _bank = bank;
        _bankMap = bank.ToDictionary(item => item.Word, StringComparer.OrdinalIgnoreCase);
    }

    public DailySession GetOrCreateSession(AppState state, DateOnly today)
    {
        var active = state.ActiveSession;
        if (active is not null && !active.Completed)
        {
            if (active.Date != today || HasAnswersFromEarlierDays(state, active))
            {
                // Keep the unfinished task at its original date. Its answered words
                // already have progress; today's task is built from today's due dates.
                if (!state.History.Contains(active)) state.History.Add(active);
                state.ActiveSession = null;
            }
            else
            {
                RestoreStudyFirstSession(active);
                RemoveSkippedPendingWords(state, active);
                if (active.Completed) return active;
                active.StudyIndex = Math.Clamp(active.StudyIndex, 0, active.Words.Count);
                active.QuizIndex = Math.Clamp(active.QuizIndex, 0, active.Words.Count);
                if (active.StudyIndex < active.Words.Count) active.StudyFinished = false;
                return active;
            }
        }

        // Older builds changed yesterday's task date and could incorrectly mark
        // it as completed today. Retain that record, but do not let it block today.
        var previous = state.History.LastOrDefault(item => item.Date == today && item.Completed &&
                                                         !HasAnswersFromEarlierDays(state, item));
        if (previous is not null)
        {
            return previous;
        }

        var session = new DailySession { Date = today };
        var progressMap = state.ProgressMap;

        var dueWords = state.Progress
            .Where(item => item.NextReview <= today && !IsSkipped(state, item.Word))
            .OrderBy(item => item.NextReview)
            .ThenBy(item => item.Stage)
            .Select(item => item.Word)
            .Where(_bankMap.ContainsKey)
            .ToList();

        var newWords = _bank
            .Where(item => !item.ExcludeFromNewLearning && !progressMap.ContainsKey(item.Word) &&
                           !IsSkipped(state, item.Word))
            .Take(Math.Max(0, Math.Clamp(state.DailyNewWords, 1, MaximumNewWordsPerDay) -
                             state.Progress.Count(item => item.IntroducedOn == today && !IsSkipped(state, item.Word))))
            .Select(item => item.Word);

        session.Words.AddRange(dueWords);
        session.Words.AddRange(newWords);
        session.Words = session.Words
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (session.Words.Count == 0)
        {
            session.StudyFinished = true;
            session.Completed = true;
            session.CompletedAt = DateTime.Now;
        }

        state.ActiveSession = session.Completed ? null : session;
        if (session.Completed)
        {
            state.History.Add(session);
        }

        return session;
    }

    public bool HasCompletedSession(AppState state, DateOnly today) =>
        state.History.Any(item => item.Date == today && item.Completed &&
                                  !HasAnswersFromEarlierDays(state, item));

    public bool CanChangeNewQuotaToday(AppState state, DateOnly today) =>
        !HasCompletedSession(state, today) &&
        (state.ActiveSession is null || state.ActiveSession.Date != today ||
         (state.ActiveSession.Confidence.Count == 0 && state.ActiveSession.QuizIndex == 0 &&
          state.ActiveSession.StudyIndex == 0 && !state.ActiveSession.StudyFinished &&
          state.ActiveSession.UserAnswers.Count == 0 && state.ActiveSession.SelfGrades.Count == 0));

    public NewWordPlan GetNewWordPlan(AppState state, DateOnly today)
    {
        var learned = state.Progress.Select(item => item.Word).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var canApplyToday = CanChangeNewQuotaToday(state, today);
        var fixedToday = !canApplyToday && state.ActiveSession is { Completed: false } current && current.Date == today
            ? current.Words.Where(word => !IsSkipped(state, word)).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        var remaining = _bank.Count(item => !learned.Contains(item.Word) && !IsSkipped(state, item.Word) &&
            (!item.ExcludeFromNewLearning || fixedToday.Contains(item.Word)));
        var firstDay = canApplyToday || today == DateOnly.MaxValue ? today : today.AddDays(1);
        var pending = !canApplyToday && state.ActiveSession is { Completed: false } active && active.Date == today
            ? active.Words.Distinct(StringComparer.OrdinalIgnoreCase).Count(word => _bankMap.ContainsKey(word) &&
                !learned.Contains(word) && !IsSkipped(state, word))
            : 0;
        var future = Math.Max(0, remaining - pending);
        var introduced = canApplyToday
            ? state.Progress.Count(item => item.IntroducedOn == today && !IsSkipped(state, item.Word)) : 0;
        DateOnly? examDate = DateOnly.TryParse(state.ExamDate, out var parsed) ? parsed : null;
        var custom = !string.IsNullOrWhiteSpace(state.NewWordCompletionDate);
        DateOnly? completionDate = DateOnly.TryParse(state.NewWordCompletionDate, out var completion) ? completion : null;
        var status = remaining == 0 ? NewWordPlanStatus.AllLearned :
            future == 0 ? NewWordPlanStatus.FinishCurrentSession : NewWordPlanStatus.Normal;
        var result = new NewWordPlan(today, examDate, firstDay, remaining, pending, future, introduced,
            0, TargetRevisionDays, 0, canApplyToday, status)
        {
            HasCustomDeadline = custom,
            CompletionDate = custom ? completionDate : null,
        };
        if (status == NewWordPlanStatus.AllLearned) return result;

        int MinimumQuota(int days)
        {
            var low = 1;
            var high = future + introduced;
            while (low < high)
            {
                var mid = low + (high - low) / 2;
                var capacity = Math.Max(0, mid - introduced) + (long)mid * (days - 1);
                if (capacity >= future) high = mid;
                else low = mid + 1;
            }
            return low;
        }

        // A chosen completion day is inclusive. It already represents the user's
        // revision buffer, so do not subtract another ten days or extend it silently.
        if (custom)
        {
            if (completionDate is null) return result with { Status = NewWordPlanStatus.InvalidDate };
            var days = Math.Max(0, completionDate.Value.DayNumber - firstDay.DayNumber + 1);
            result = result with
            {
                AvailableDays = days,
                RevisionDays = examDate is null ? 0 : Math.Max(0, examDate.Value.DayNumber - completionDate.Value.DayNumber - 1),
            };
            if (completionDate < today) return result with { Status = NewWordPlanStatus.NoStudyDays };
            if (status == NewWordPlanStatus.FinishCurrentSession) return result;
            if (days == 0) return result with { Status = NewWordPlanStatus.NoStudyDays };
            var quota = MinimumQuota(days);
            return result with
            {
                RequiredPerDay = quota,
                Status = quota <= MaximumNewWordsPerDay ? NewWordPlanStatus.CustomDeadline : NewWordPlanStatus.ExceedsDailyLimit,
            };
        }

        if (examDate is null) return result with { Status = NewWordPlanStatus.InvalidDate };
        if (examDate <= today) return result with { Status = NewWordPlanStatus.NoStudyDays, RevisionDays = 0 };
        if (status == NewWordPlanStatus.FinishCurrentSession) return result;
        // Exam day is not a new-word study day. Started tasks keep their cards;
        // their new quota can first be used tomorrow.
        var beforeExam = Math.Max(0, examDate.Value.DayNumber - firstDay.DayNumber);
        if (beforeExam == 0) return result with { Status = NewWordPlanStatus.NoStudyDays, RevisionDays = 0 };
        var withRevision = Math.Max(0, beforeExam - TargetRevisionDays);
        if (withRevision > 0)
        {
            var required = MinimumQuota(withRevision);
            if (required <= MaximumNewWordsPerDay)
                return result with { AvailableDays = withRevision, RequiredPerDay = required,
                    CompletionDate = examDate.Value.AddDays(-TargetRevisionDays - 1) };
        }
        // Explain when ten full revision days cannot be kept. Do not cap an
        // impossible requirement at 50 and incorrectly call that achievable.
        var beforeExamQuota = MinimumQuota(beforeExam);
        return result with
        {
            AvailableDays = beforeExam,
            CompletionDate = examDate.Value.AddDays(-1),
            RevisionDays = 0,
            RequiredPerDay = beforeExamQuota,
            Status = beforeExamQuota <= MaximumNewWordsPerDay
                ? NewWordPlanStatus.ReducedRevisionTime : NewWordPlanStatus.ExceedsDailyLimit,
        };
    }

    private static bool HasAnswersFromEarlierDays(AppState state, DailySession session)
    {
        var progress = state.ProgressMap;
        return session.SelfGrades.Keys.Any(word => progress.TryGetValue(word, out var item) &&
                                                  item.LastReviewedOn < session.Date);
    }

    public SessionStep GetSessionStep(DailySession session)
    {
        if (session.Completed)
        {
            return SessionStep.Complete;
        }

        RestoreStudyFirstSession(session);
        if (!session.StudyFinished)
        {
            return SessionStep.NewStudy;
        }

        return session.QuizIndex < session.Words.Count
            ? SessionStep.NewQuiz
            : SessionStep.Complete;
    }

    public string GetQuizWord(DailySession session)
    {
        RestoreStudyFirstSession(session);
        return session.Words[session.QuizIndex];
    }

    private static void RestoreStudyFirstSession(DailySession session)
    {
        if (session.ReviewWords is null)
        {
            return;
        }

        // Preserve answers and learned cards from an unfinished review-first session.
        // Completed answers stay before the quiz cursor so they are never graded twice.
        var answered = session.Words.Where(session.SelfGrades.ContainsKey).ToList();
        var studied = session.Words.Where(word => !session.SelfGrades.ContainsKey(word) &&
                                                 session.Confidence.ContainsKey(word)).ToList();
        var remaining = session.Words.Where(word => !session.SelfGrades.ContainsKey(word) &&
                                                   !session.Confidence.ContainsKey(word)).ToList();
        session.Words = [.. answered, .. studied, .. remaining];
        session.QuizIndex = answered.Count;
        session.StudyIndex = answered.Count + studied.Count;
        session.StudyFinished = session.StudyIndex >= session.Words.Count;
        session.ReviewWords = null;
    }

    public void RateCard(DailySession session, string word, int confidence)
    {
        if (session.Completed || !session.Words.Contains(word, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("该单词已不在当前学习任务中。");
        session.Confidence[word] = Math.Clamp(confidence, 0, 2);
        session.StudyIndex = Math.Min(session.StudyIndex + 1, session.Words.Count);
        if (session.StudyIndex >= session.Words.Count)
        {
            session.StudyFinished = true;
        }
    }

    public bool SubmitAnswer(
        AppState state,
        DailySession session,
        string word,
        string answerMeaning,
        DateOnly today)
    {
        if (IsSkipped(state, word))
            throw new InvalidOperationException("该单词已永久跳过，不再进行检测。");
        var entry = _bankMap[word];
        var correct = string.Equals(
            answerMeaning,
            FormatOptionMeaning(entry.Meaning),
            StringComparison.Ordinal);
        session.UserAnswers[word] = answerMeaning;
        session.SelfGrades[word] = correct ? 2 : 0;

        var progressMap = state.ProgressMap;
        if (!progressMap.TryGetValue(word, out var progress))
        {
            progress = new WordProgress
            {
                Word = word,
                IntroducedOn = today,
                Stage = 0,
            };
            state.Progress.Add(progress);
        }

        if (correct)
        {
            session.CorrectCount++;
            progress.CorrectCount++;
            progress.Stage = StageAfterCorrectAnswer(progress, session, word, today);
        }
        else
        {
            progress.WrongCount++;
            // A failed recall starts relearning, even for a previously mature word.
            progress.Stage = 0;
        }

        progress.LastReviewedOn = today;
        progress.NextReview = today.AddDays(ReviewIntervals[progress.Stage]);
        session.QuizIndex = Math.Min(session.QuizIndex + 1, session.Words.Count);
        return correct;
    }

    public void SubmitSelfAssessment(
        AppState state,
        DailySession session,
        string word,
        string answer,
        int selfGrade,
        DateOnly today)
    {
        if (IsSkipped(state, word))
            throw new InvalidOperationException("该单词已永久跳过，不再进行检测。");
        selfGrade = Math.Clamp(selfGrade, 0, 2);
        session.UserAnswers[word] = answer.Trim();
        session.SelfGrades[word] = selfGrade;

        var progressMap = state.ProgressMap;
        if (!progressMap.TryGetValue(word, out var progress))
        {
            progress = new WordProgress
            {
                Word = word,
                IntroducedOn = today,
                Stage = 0,
            };
            state.Progress.Add(progress);
        }

        if (selfGrade == 2)
        {
            session.CorrectCount++;
            progress.CorrectCount++;
            progress.Stage = StageAfterCorrectAnswer(progress, session, word, today);
        }
        else if (selfGrade == 1)
        {
            session.PartialCount++;
            progress.WrongCount++;
            progress.Stage = Math.Max(0, progress.Stage - 1);
        }
        else
        {
            progress.WrongCount++;
            // "Completely forgotten" always returns tomorrow, not weeks later.
            progress.Stage = 0;
        }

        progress.LastReviewedOn = today;
        progress.NextReview = today.AddDays(ReviewIntervals[progress.Stage]);
        session.QuizIndex = Math.Min(session.QuizIndex + 1, session.Words.Count);
    }

    private static int StageAfterCorrectAnswer(
        WordProgress progress, DailySession session, string word, DateOnly today)
    {
        // A correct answer after relearning is not evidence of retention across days.
        // Only an explicit "unknown" on an old word triggers this rule; older saves
        // without a confidence entry keep their existing scheduling behavior.
        if (progress.IntroducedOn < today &&
            session.Confidence.TryGetValue(word, out var confidence) && confidence == 0)
        {
            return 0;
        }

        return progress.LastReviewedOn == default
            ? (session.Confidence.GetValueOrDefault(word, 0) >= 2 ? 1 : 0)
            : Math.Min(progress.Stage + 1, ReviewIntervals.Length - 1);
    }

    public void CompleteSession(AppState state, DailySession session)
    {
        if (session.Completed)
        {
            return;
        }

        if (!session.StudyFinished || session.QuizIndex < session.Words.Count)
        {
            throw new InvalidOperationException("仍有学习或检测未完成，不能结束今日任务。");
        }

        session.Completed = true;
        session.CompletedAt = DateTime.Now;
        if (!state.History.Any(item =>
                item.Date == session.Date &&
                item.CompletedAt == session.CompletedAt))
        {
            state.History.Add(session);
        }

        state.ActiveSession = null;
    }

#if TESTMODE
    public void CompleteDayAsAllCorrect(
        AppState state,
        DailySession session,
        DateOnly today)
    {
        RemoveSkippedPendingWords(state, session);
        if (session.Completed) return;
        var progressMap = state.ProgressMap;
        foreach (var word in session.Words)
        {
            // Already submitted results remain historical even after opting out
            // of future reviews. Test-mode advancement must not re-grade them.
            if (IsSkipped(state, word)) continue;
            if (!progressMap.TryGetValue(word, out var progress))
            {
                progress = new WordProgress
                {
                    Word = word,
                    IntroducedOn = today,
                    Stage = 0,
                };
                state.Progress.Add(progress);
                progressMap[word] = progress;
            }

            if (progress.LastReviewedOn != today)
            {
                progress.Stage = progress.IntroducedOn == today
                    ? 1
                    : Math.Min(progress.Stage + 1, ReviewIntervals.Length - 1);
            }
            else
            {
                progress.Stage = Math.Max(progress.Stage, 1);
            }

            progress.CorrectCount++;
            progress.LastReviewedOn = today;
            progress.NextReview = today.AddDays(ReviewIntervals[progress.Stage]);
            session.UserAnswers[word] = "【测试模式自动视为答对】";
            session.SelfGrades[word] = 2;
        }

        session.StudyIndex = session.Words.Count;
        session.QuizIndex = session.Words.Count;
        session.CorrectCount = session.Words.Count(word => session.SelfGrades.GetValueOrDefault(word, -1) == 2);
        session.PartialCount = session.Words.Count(word => session.SelfGrades.GetValueOrDefault(word, -1) == 1);
        session.StudyFinished = true;
        CompleteSession(state, session);
    }
#endif

    public IReadOnlyList<string> BuildOptions(DailySession session, string word)
    {
        var correct = FormatOptionMeaning(_bankMap[word].Meaning);
        var random = new Random(StableSeed($"{session.Date:yyyyMMdd}-{word}-{session.QuizIndex}"));
        var distractors = _bank
            .Where(item => !item.Word.Equals(word, StringComparison.OrdinalIgnoreCase))
            .Select(item => FormatOptionMeaning(item.Meaning))
            .Where(item => !item.Equals(correct, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(_ => random.Next())
            .Take(3)
            .ToList();

        while (distractors.Count < 3)
        {
            distractors.Add("以上都不是");
        }

        var options = distractors.Append(correct).ToList();
        for (var index = options.Count - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (options[index], options[swapIndex]) = (options[swapIndex], options[index]);
        }

        return options;
    }

    public WordEntry GetWord(string word) => _bankMap[word];

    public bool IsSkipped(AppState state, string word) =>
        !string.IsNullOrWhiteSpace(word) && state.SkippedWords?.Any(skipped =>
            string.Equals(skipped?.Trim(), word.Trim(), StringComparison.OrdinalIgnoreCase)) == true;

    public int SkippedCount(AppState state) =>
        (state.SkippedWords ?? []).Where(word => !string.IsNullOrWhiteSpace(word))
            .Select(word => word.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    // Call only on a candidate copy, and commit that copy only after Save succeeds.
    // An opt-out never creates progress, awards a correct answer, or erases a review.
    public bool SkipWord(AppState state, string word, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(word)) throw new ArgumentException("单词不能为空。", nameof(word));
        var normalized = word.Trim();
        var canonical = _bankMap.TryGetValue(normalized, out var entry) ? entry.Word : normalized;
        state.SkippedWords ??= [];
        var added = !IsSkipped(state, canonical);
        if (added) state.SkippedWords.Add(canonical);
        if (state.ActiveSession is { Completed: false } active && active.Date == today)
        {
            RestoreStudyFirstSession(active);
            var replaceCurrentCard = added && active.Words.Contains(canonical, StringComparer.OrdinalIgnoreCase);
            RemoveSkippedPendingWords(state, active, completeIfFinished: false);
            if (replaceCurrentCard)
            {
                var learned = state.ProgressMap;
                var scheduled = active.Words.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var replacement = _bank.FirstOrDefault(item => !item.ExcludeFromNewLearning &&
                    !IsSkipped(state, item.Word) && !learned.ContainsKey(item.Word) &&
                    !scheduled.Contains(item.Word));
                if (replacement is not null)
                {
                    // Append after any remaining study cards. During a quiz the
                    // study cursor already points to this new tail card, so it
                    // must be studied before resuming the existing quiz cursor.
                    active.Words.Add(replacement.Word);
                    active.StudyFinished = false;
                }
            }
            if (active.StudyIndex >= active.Words.Count)
            {
                active.StudyFinished = true;
                if (active.QuizIndex >= active.Words.Count) CompleteSession(state, active);
            }
        }
        return added;
    }

    public bool RestoreWord(AppState state, string word, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(word) || state.SkippedWords is null) return false;
        var removed = state.SkippedWords.RemoveAll(skipped => string.Equals(skipped?.Trim(), word.Trim(),
            StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed) return false;
        // Restoring changes future scheduling only, never the task already in
        // progress. Retain the old stage and answer history; a future-dated
        // review becomes due today so it can return in the next generated task.
        var progress = state.Progress.FirstOrDefault(item =>
            string.Equals(item.Word, word.Trim(), StringComparison.OrdinalIgnoreCase));
        if (progress is not null && progress.NextReview > today) progress.NextReview = today;
        return true;
    }

    private void RemoveSkippedPendingWords(AppState state, DailySession session, bool completeIfFinished = true)
    {
        if (session.Completed) return;
        // Keep formally submitted results in the task's history so the score's
        // numerator and denominator retain their original meaning. Only pending
        // cards are removed; their old confidence/answer entries remain untouched.
        var studyLimit = Math.Clamp(session.StudyIndex, 0, session.Words.Count);
        var quizLimit = Math.Clamp(session.QuizIndex, 0, session.Words.Count);
        var kept = session.Words.Select((word, index) => (word, index))
            .Where(item => !IsSkipped(state, item.word) || session.SelfGrades.ContainsKey(item.word))
            .ToList();
        if (kept.Count == session.Words.Count) return;
        session.StudyIndex = kept.Count(item => item.index < studyLimit);
        session.QuizIndex = kept.Count(item => item.index < quizLimit);
        session.Words = kept.Select(item => item.word).ToList();
        session.StudyFinished = session.StudyIndex >= session.Words.Count;
        if (completeIfFinished && session.StudyFinished && session.QuizIndex >= session.Words.Count)
            CompleteSession(state, session);
    }

    public int DueCount(AppState state, DateOnly today) =>
        state.Progress.Count(item => item.NextReview <= today && !IsSkipped(state, item.Word));

    public int LearnedCount(AppState state) => state.Progress.Count;

    public int UnlearnedCount(AppState state)
    {
        var learned = state.Progress
            .Select(item => item.Word)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fixedSession = state.ActiveSession is { Completed: false } active
            ? active.Words.Where(word => !IsSkipped(state, word)).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        return _bank.Count(item => !learned.Contains(item.Word) && !IsSkipped(state, item.Word) &&
            (!item.ExcludeFromNewLearning || fixedSession.Contains(item.Word)));
    }

    public int LearningTargetCount(AppState state) => LearnedCount(state) + UnlearnedCount(state);

    public int TodayNewCount(AppState state, DateOnly today)
    {
        if (state.ActiveSession?.Date == today && !state.ActiveSession.Completed)
        {
            var progressWords = state.Progress
                .Select(item => item.Word)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return state.ActiveSession.Words.Count(word => !progressWords.Contains(word) && !IsSkipped(state, word));
        }

        var session = state.History.FirstOrDefault(item => item.Date == today && item.Completed);
        if (session is null)
        {
            return Math.Min(
                Math.Clamp(state.DailyNewWords, 1, MaximumNewWordsPerDay),
                UnlearnedCount(state));
        }

        return session.Words.Count;
    }

    public static string FormatMeaning(string value)
    {
        var text = value
            .Replace("adj·", "adj. ", StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
        return text.Length <= 72 ? text : text[..69] + "...";
    }

    public static string FormatOptionMeaning(string value)
    {
        var text = Regex.Replace(
            value,
            @"\b(?:adj|adv|prep|pron|conj|interj|num|art|n|vt|vi|v|a)\.",
            "",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\d+\s*[.、]\s*", "");
        text = Regex.Replace(text, @"\([^)]*\)|（[^）]*）", "");
        text = text.Replace("；", "，", StringComparison.Ordinal)
            .Replace(";", "，", StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim(' ', ',', '，', '.', '。');

        var parts = text
            .Split(['，', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Take(2);
        var concise = string.Join("，", parts);
        if (string.IsNullOrWhiteSpace(concise))
        {
            concise = "以上都不是";
        }

        return concise.Length <= 24 ? concise : concise[..23] + "…";
    }

    private static int StableSeed(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return (int)(hash & 0x7fffffff);
        }
    }
}
