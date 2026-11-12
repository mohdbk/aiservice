using System.Globalization;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;

namespace Moet.Recruitment.Domain.Assessments;

public class Invitation
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int JobApplicationId { get; set; }
    public JobApplication? JobApplication { get; set; }
    public int AssessmentId { get; set; }
    public Assessment? Assessment { get; set; }
    public InvitationStatus Status { get; set; } = InvitationStatus.Invited;
    public DateTime DeadlineAt { get; set; }
    public byte AttemptsAllowed { get; set; } = 1;
    public byte AttemptsUsed { get; set; }
    public byte ExtraTimePercent { get; set; }
    public string LanguageCode { get; set; } = "ar";
    public decimal? Score { get; set; }
    public InvitationResult? Result { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByStaffId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<Attempt> Attempts { get; set; } = [];

    public bool DeadlinePassed(DateTime now) => DeadlineAt < now;

    /// <summary>"" when a new attempt may start, otherwise a reason code.</summary>
    public string CannotStartReason(DateTime now) => AttemptRules.CanStart(Status, AttemptsUsed, AttemptsAllowed, DeadlinePassed(now));

    /// <summary>What the candidate sees as the status.</summary>
    public string CandidateStatus(DateTime now) => Status switch
    {
        InvitationStatus.Invited when DeadlinePassed(now) => "expired",
        InvitationStatus.Invited => "to_do",
        InvitationStatus.InProgress => "in_progress",
        InvitationStatus.Submitted => "submitted",
        InvitationStatus.Marked => "completed",
        InvitationStatus.Expired => "expired",
        InvitationStatus.Cancelled => "cancelled",
        _ => "to_do",
    };

    /// <summary>Gives the candidate more attempts with a new deadline. Each attempt draws its own questions.</summary>
    public void AllowReexam(int extraAttempts, int deadlineDays, DateTime now)
    {
        if (Status == InvitationStatus.InProgress) throw DomainException.Conflict("attempt_in_progress");
        AttemptsAllowed = (byte)Math.Min(50, Math.Max(AttemptsUsed, AttemptsAllowed) + Math.Max(1, extraAttempts));
        DeadlineAt = now.AddDays(Math.Clamp(deadlineDays, 1, 90));
        if (Status is InvitationStatus.Expired or InvitationStatus.Cancelled) Status = InvitationStatus.Invited;
        UpdatedAt = now;
    }

    public void Cancel(DateTime now)
    {
        Status = InvitationStatus.Cancelled;
        UpdatedAt = now;
    }

    /// <summary>Recomputes the invitation's status, score and result from its attempts.</summary>
    public void Summarise(ScorePolicy policy, bool personality = false)
    {
        var ordered = Attempts.OrderBy(a => a.AttemptNo).ToList();
        if (ordered.Any(a => a.Status == AttemptStatus.InProgress))
        {
            Status = InvitationStatus.InProgress;
            return;
        }
        var marked = ordered.Where(a => a.Status == AttemptStatus.Marked).ToList();
        var pending = ordered.Any(a => a.Status == AttemptStatus.Submitted);
        Attempt? counted = marked.Count == 0 ? null
            : personality ? marked[^1]
            : policy == ScorePolicy.Highest ? marked.OrderByDescending(a => a.Percent).ThenBy(a => a.AttemptNo).First() : marked[^1];
        if (Status != InvitationStatus.Cancelled)
            Status = marked.Count > 0 ? InvitationStatus.Marked : pending ? InvitationStatus.Submitted : Status;
        Score = personality ? null : counted?.Percent;
        Result = counted != null ? personality ? InvitationResult.Completed : counted.Passed == true ? InvitationResult.Pass : InvitationResult.Fail
            : pending ? InvitationResult.AwaitingMarking : null;
    }
}

public class Attempt
{
    public int Id { get; set; }
    public int InvitationId { get; set; }
    public Invitation? Invitation { get; set; }
    public byte AttemptNo { get; set; }
    public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;
    public DateTime StartedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public int CurrentSectionNo { get; set; }
    public DateTime? SectionStartedAt { get; set; }
    public DateTime? SectionEndsAt { get; set; }
    public decimal Earned { get; set; }
    public decimal Total { get; set; }
    public decimal Percent { get; set; }
    public bool? Passed { get; set; }
    public int TabSwitches { get; set; }
    public int Pastes { get; set; }
    /// <summary>Personality questionnaires: the trait profile (JSON list of TraitScore) worked out on submission.</summary>
    public string? TraitProfile { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<AttemptSection> Sections { get; set; } = [];

    public IEnumerable<AttemptSection> OrderedSections => Sections.OrderBy(s => s.SectionNo);

    public IEnumerable<AttemptQuestion> AllQuestions => OrderedSections.SelectMany(s => s.OrderedQuestions);

    public AttemptSection CurrentSection => Sections.Single(s => s.SectionNo == CurrentSectionNo);

    public void StartSection(int sectionNo, int extraTimePercent, DateTime now)
    {
        CurrentSectionNo = sectionNo;
        SectionStartedAt = now;
        var secs = AttemptRules.SectionSeconds(Sections.Single(s => s.SectionNo == sectionNo).Minutes, extraTimePercent);
        SectionEndsAt = secs > 0 ? now.AddSeconds(secs) : null;
    }

    /// <summary>Moves past every section whose time is up (may submit the attempt). Returns true when anything changed.</summary>
    public bool CatchUp(int extraTimePercent, DateTime now)
    {
        var changed = false;
        while (Status == AttemptStatus.InProgress && SectionEndsAt != null && SectionEndsAt <= now)
        {
            Advance(extraTimePercent, now);
            changed = true;
        }
        return changed;
    }

    /// <summary>Locks the current section and opens the next one, or submits after the last.</summary>
    public void Advance(int extraTimePercent, DateTime now)
    {
        var next = CurrentSectionNo + 1;
        if (Sections.Any(s => s.SectionNo == next)) StartSection(next, extraTimePercent, now);
        else Submit(now);
    }

    public void Submit(DateTime now)
    {
        Status = AttemptStatus.Submitted;
        SubmittedAt = now;
        SectionEndsAt = null;
        Score(firstTime: true);
    }

    /// <summary>Saves the candidate's answer to a question of the current section.</summary>
    public void SaveAnswer(string key, AnswerInput input, DateTime now)
    {
        if (Status != AttemptStatus.InProgress) throw DomainException.Conflict("section_closed");
        var q = CurrentSection.Questions.FirstOrDefault(x => x.QuestionKey == key) ?? throw DomainException.Conflict("question_closed");
        q.SetAnswer(input, now);
    }

    /// <summary>Scores every question; marker points win over automatic marks. Essays and files wait for a marker.</summary>
    public void Score(bool firstTime = false)
    {
        decimal total = 0, earned = 0;
        var pending = false;
        foreach (var q in AllQuestions)
        {
            total += q.Points;
            var (auto, needs) = AttemptRules.Auto(q);
            if (firstTime || q.MarkerPoints == null)
            {
                q.AutoPoints = auto;
                q.NeedsMarking = needs;
            }
            var got = q.MarkerPoints is { } m ? Math.Clamp(m, 0, q.Points) : q.AutoPoints;
            if (q.MarkerPoints == null && q.NeedsMarking) pending = true;
            earned += got;
        }
        Earned = Math.Round(earned, 2);
        Total = Math.Round(total, 2);
        Percent = total > 0 ? Math.Round(earned / total * 100, 1) : 0;
        if (Status != AttemptStatus.InProgress) Status = pending ? AttemptStatus.Submitted : AttemptStatus.Marked;
    }

    public IReadOnlyList<SectionScore> SectionScores() => OrderedSections.Select(s =>
    {
        var total = s.Questions.Sum(q => q.Points);
        var earned = s.Questions.Sum(q => q.MarkerPoints is { } m ? Math.Clamp(m, 0, q.Points) : q.AutoPoints);
        var pct = total > 0 ? Math.Round(earned / total * 100, 1) : 0;
        return new SectionScore(s.SectionNo, s.Title, Math.Round(earned, 2), Math.Round(total, 2), pct, s.PassMark > 0 ? pct >= s.PassMark : null);
    }).ToList();

    /// <summary>Passes when every answer is marked, the overall pass mark is met and every section pass mark is met.</summary>
    /// <summary>Works out the trait profile of a personality questionnaire; there is no pass or fail.</summary>
    public IReadOnlyList<TraitScore> ScorePersonality(PersonalityModel model, IReadOnlyDictionary<string, TraitNorm> norms)
    {
        var profile = Personality.Profile(AllQuestions, model, norms);
        TraitProfile = System.Text.Json.JsonSerializer.Serialize(profile);
        Passed = null;
        return profile;
    }

    public void DecidePass(decimal passMark)
    {
        Passed = Status == AttemptStatus.Marked ? Percent >= passMark && SectionScores().All(s => s.Passed != false) : null;
    }
}

public sealed record SectionScore(int SectionNo, string Title, decimal Earned, decimal Total, decimal Percent, bool? Passed);

public class AttemptSection
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public int SectionNo { get; set; }
    public string Title { get; set; } = "";
    public string Instructions { get; set; } = "";
    public int Minutes { get; set; }
    public decimal PassMark { get; set; }
    public List<AttemptSectionTranslation> Translations { get; set; } = [];
    public List<AttemptQuestion> Questions { get; set; } = [];

    public IEnumerable<AttemptQuestion> OrderedQuestions => Questions.OrderBy(q => q.SortOrder);

    public (string Title, string Instructions) In(string lang)
    {
        var t = Translations.FirstOrDefault(x => x.LanguageCode == lang);
        return (string.IsNullOrWhiteSpace(t?.Title) ? Title : t.Title, string.IsNullOrWhiteSpace(t?.Instructions) ? Instructions : t.Instructions);
    }
}

public class AttemptSectionTranslation
{
    public int AttemptSectionId { get; set; }
    public string LanguageCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string Instructions { get; set; } = "";
}

public sealed record AnswerInput(string? Text, IReadOnlyList<string>? OptionKeys, Guid? FileId, string? FileName);

public class AttemptQuestion
{
    public int Id { get; set; }
    public int AttemptSectionId { get; set; }
    public string QuestionKey { get; set; } = "";
    public int SortOrder { get; set; }
    public int QuestionId { get; set; }
    public QuestionType QuestionType { get; set; }
    public string Text { get; set; } = "";
    public decimal Points { get; set; }
    public decimal Tolerance { get; set; }
    public string Rubric { get; set; } = "";
    public int MaxWords { get; set; }
    public string Trait { get; set; } = "";
    public bool ReverseKeyed { get; set; }
    public string? AnswerText { get; set; }
    public Guid? AnswerFileId { get; set; }
    public string? AnswerFileName { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public decimal AutoPoints { get; set; }
    public bool NeedsMarking { get; set; }
    public decimal? MarkerPoints { get; set; }
    public string MarkerComment { get; set; } = "";
    public int? MarkedByStaffId { get; set; }
    public DateTime? MarkedAt { get; set; }
    public decimal? SuggestedPoints { get; set; }
    public string SuggestionReason { get; set; } = "";
    public List<AttemptQuestionTranslation> Translations { get; set; } = [];
    public List<AttemptOption> Options { get; set; } = [];
    public List<AttemptAcceptedAnswer> AcceptedAnswers { get; set; } = [];

    public IEnumerable<AttemptOption> OrderedOptions => Options.OrderBy(o => o.SortOrder);

    public bool IsAnswered => AnswerText is { Length: > 0 } || AnswerFileId != null || Options.Any(o => o.IsSelected);

    public string TextIn(string lang) =>
        Translations.FirstOrDefault(t => t.LanguageCode == lang)?.Text is { Length: > 0 } s ? s : Text;

    /// <summary>Normalises and stores an answer; throws invalid_answer or too_many_words.</summary>
    public void SetAnswer(AnswerInput input, DateTime now)
    {
        var keys = Options.Select(o => o.OptionKey).ToHashSet();
        switch (QuestionType)
        {
            case QuestionType.SingleChoice or QuestionType.TrueFalse or QuestionType.Likert:
            {
                var k = input.OptionKeys?.Where(x => !string.IsNullOrEmpty(x)).ToList() ?? [];
                if (k.Count > 1 || k.Any(x => !keys.Contains(x))) throw DomainException.Invalid("invalid_answer");
                foreach (var o in Options) o.IsSelected = k.Contains(o.OptionKey);
                break;
            }
            case QuestionType.MultipleChoice:
            {
                var k = input.OptionKeys?.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList() ?? [];
                if (k.Any(x => !keys.Contains(x))) throw DomainException.Invalid("invalid_answer");
                foreach (var o in Options) o.IsSelected = k.Contains(o.OptionKey);
                break;
            }
            case QuestionType.Number:
            {
                var s = TextUtil.Clip((input.Text ?? "").Trim(), 40);
                if (s.Length > 0 && !decimal.TryParse(s.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    throw DomainException.Invalid("invalid_answer");
                AnswerText = s.Length > 0 ? s : null;
                break;
            }
            case QuestionType.ShortAnswer:
                AnswerText = string.IsNullOrWhiteSpace(input.Text) ? null : TextUtil.Clip(input.Text, 500);
                break;
            case QuestionType.Essay:
            {
                var s = string.IsNullOrWhiteSpace(input.Text) ? null : TextUtil.Clip(input.Text, 30000);
                if (s != null && MaxWords > 0 && AttemptRules.WordCount(s) > MaxWords * 1.1) throw DomainException.Invalid("too_many_words");
                AnswerText = s;
                break;
            }
            case QuestionType.FileUpload:
                if (input.FileId == null) throw DomainException.Invalid("invalid_answer");
                AnswerFileId = input.FileId;
                AnswerFileName = TextUtil.Clip(input.FileName, 200);
                break;
        }
        AnsweredAt = now;
    }

    /// <summary>Marker's points (clamped to the question's points) and comment.</summary>
    public void Mark(decimal? points, string comment, int staffId, DateTime now)
    {
        MarkerPoints = points == null ? null : Math.Clamp(points.Value, 0, Points);
        MarkerComment = TextUtil.Clip(comment?.Trim(), 1000);
        MarkedByStaffId = points == null ? null : staffId;
        MarkedAt = points == null ? null : now;
    }

    /// <summary>The answer as plain text, for markers.</summary>
    public string AnswerDisplay() => QuestionType switch
    {
        QuestionType.SingleChoice or QuestionType.MultipleChoice or QuestionType.TrueFalse or QuestionType.Likert =>
            string.Join(", ", OrderedOptions.Where(o => o.IsSelected).Select(o => o.Text)),
        QuestionType.FileUpload => AnswerFileName ?? "",
        _ => AnswerText ?? "",
    };
}

public class AttemptQuestionTranslation
{
    public int AttemptQuestionId { get; set; }
    public string LanguageCode { get; set; } = "";
    public string Text { get; set; } = "";
}

public class AttemptOption
{
    public int Id { get; set; }
    public int AttemptQuestionId { get; set; }
    public string OptionKey { get; set; } = "";
    public int SortOrder { get; set; }
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
    public bool IsSelected { get; set; }
    public List<AttemptOptionTranslation> Translations { get; set; } = [];

    public string TextIn(string lang) =>
        Translations.FirstOrDefault(t => t.LanguageCode == lang)?.Text is { Length: > 0 } s ? s : Text;
}

public class AttemptOptionTranslation
{
    public int AttemptOptionId { get; set; }
    public string LanguageCode { get; set; } = "";
    public string Text { get; set; } = "";
}

public class AttemptAcceptedAnswer
{
    public int Id { get; set; }
    public int AttemptQuestionId { get; set; }
    public string Answer { get; set; } = "";
}
