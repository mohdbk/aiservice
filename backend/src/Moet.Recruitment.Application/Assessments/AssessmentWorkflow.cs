using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Assessments;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;

namespace Moet.Recruitment.Application.Assessments;

/// <summary>Invitations and attempts, shared by the career portal (candidates) and the backoffice (HR).</summary>
public sealed class AssessmentWorkflow(IAppDbContext db, ContentService content, PortalMailer mailer, JobQueue jobs, ICodeGenerator codes, IClock clock)
{
    // ---------------- invitations ----------------

    /// <summary>Invites the applicant (or returns their open invitation to the same test) and emails them.</summary>
    public async Task<Invitation> InviteAsync(JobApplication app, int assessmentId, int? deadlineDays, bool sendEmail, int? staffId, CancellationToken ct = default)
    {
        var asm = await db.Assessments.Include(a => a.Translations).FirstOrDefaultAsync(a => a.Id == assessmentId, ct)
                  ?? throw DomainException.NotFound("assessment_not_found");
        if (asm.Status != AssessmentStatus.Published) throw DomainException.Conflict("assessment_not_published");
        var existing = await db.Invitations.FirstOrDefaultAsync(i => i.JobApplicationId == app.Id && i.AssessmentId == assessmentId
            && i.Status != InvitationStatus.Cancelled && i.Status != InvitationStatus.Expired, ct);
        if (existing != null) return existing;
        var now = clock.UtcNow;
        var inv = new Invitation
        {
            Code = await codes.NextAsync(CodeKind.Invitation, ct), JobApplicationId = app.Id, AssessmentId = asm.Id,
            Status = InvitationStatus.Invited, DeadlineAt = now.AddDays(Math.Clamp(deadlineDays ?? asm.DefaultDeadlineDays, 1, 90)),
            AttemptsAllowed = asm.MaxAttempts, LanguageCode = await content.PortalLanguageAsync(app.LanguageCode, ct),
            CreatedAt = now, CreatedByStaffId = staffId, UpdatedAt = now,
        };
        db.Invitations.Add(inv);
        if (sendEmail) await EmailAsync(inv, asm, app, "email.assessment_subject", "email.assessment_body", ct);
        return inv;
    }

    public async Task InviteOnApplyAsync(JobApplication app, CancellationToken ct = default)
    {
        var stages = await db.JobOpenings.AsNoTracking().Where(j => j.Id == app.JobOpeningId)
            .SelectMany(j => j.AssessmentStages).Where(s => s.SendWhen == SendWhen.OnApply).OrderBy(s => s.SortOrder).ToListAsync(ct);
        foreach (var st in stages)
        {
            try { await InviteAsync(app, st.AssessmentId, st.DeadlineDays, true, null, ct); }
            catch (DomainException) { /* an unpublished test is skipped; HR can invite later */ }
        }
    }

    public async Task EmailAsync(Invitation inv, Assessment asm, JobApplication app, string subjectKey, string bodyKey, CancellationToken ct)
    {
        var lang = await content.PortalLanguageAsync(inv.LanguageCode, ct);
        var link = await mailer.LinkAsync(lang, $"/assessments/{inv.Code}", ct);
        if (link.Length == 0) return;
        await mailer.QueueAsync(app.Email, app.FullName, lang, subjectKey, bodyKey, link,
            new Dictionary<string, string> { ["test"] = asm.In(lang).Title, ["deadline"] = clock.LocalHuman(inv.DeadlineAt) }, ct);
    }

    public async Task<int> ExpireInvitationsAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        return await db.Invitations.Where(i => i.Status == InvitationStatus.Invited && i.DeadlineAt < now)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.Status, InvitationStatus.Expired).SetProperty(i => i.UpdatedAt, now), ct);
    }

    // ---------------- loading ----------------

    public async Task<Invitation> LoadForCandidateAsync(int applicantId, string? code, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(code)) throw DomainException.NotFound("assessment_not_found");
        var inv = await db.Invitations
                      .Include(i => i.JobApplication!).ThenInclude(a => a.JobOpening!).ThenInclude(j => j.Translations)
                      .Include(i => i.Assessment!).ThenInclude(a => a.Translations)
                      .Include(i => i.Assessment!).ThenInclude(a => a.Sections).ThenInclude(s => s.Translations)
                      .Include(i => i.Attempts)
                      .AsSplitQuery()
                      .FirstOrDefaultAsync(i => i.Code == code && i.JobApplication!.ApplicantAccountId == applicantId, ct);
        if (inv == null || inv.Status == InvitationStatus.Cancelled) throw DomainException.NotFound("assessment_not_found");
        return inv;
    }

    async Task<Attempt?> LoadAttemptAsync(int attemptId, CancellationToken ct) =>
        await db.Attempts
            .Include(a => a.Sections).ThenInclude(s => s.Translations)
            .Include(a => a.Sections).ThenInclude(s => s.Questions).ThenInclude(q => q.Translations)
            .Include(a => a.Sections).ThenInclude(s => s.Questions).ThenInclude(q => q.Options).ThenInclude(o => o.Translations)
            .Include(a => a.Sections).ThenInclude(s => s.Questions).ThenInclude(q => q.AcceptedAnswers)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);

    /// <summary>The attempt in progress, after moving past timed-out sections (which may submit it).</summary>
    public async Task<Attempt?> OpenAttemptAsync(Invitation inv, CancellationToken ct = default)
    {
        var last = inv.Attempts.OrderByDescending(a => a.AttemptNo).FirstOrDefault();
        if (last is not { Status: AttemptStatus.InProgress }) return null;
        var a = await LoadAttemptAsync(last.Id, ct) ?? throw DomainException.NotFound("assessment_not_found");
        if (a.CatchUp(inv.ExtraTimePercent, clock.UtcNow))
        {
            if (a.Status != AttemptStatus.InProgress) await FinaliseAsync(inv, a, ct);
            await db.SaveChangesAsync(ct);
        }
        return a.Status == AttemptStatus.InProgress ? a : null;
    }

    /// <summary>After submission or marking: pass or fail, the invitation's result, and mark suggestions when needed.</summary>
    public async Task FinaliseAsync(Invitation inv, Attempt a, CancellationToken ct)
    {
        var asm = inv.Assessment ?? await db.Assessments.FirstAsync(x => x.Id == inv.AssessmentId, ct);
        if (asm.IsPersonality) a.ScorePersonality(asm.PersonalityModel, Personality.ParseNorms(asm.TraitNorms));
        else a.DecidePass(asm.PassMark);
        inv.Summarise(asm.ScorePolicy, asm.IsPersonality);
        inv.UpdatedAt = clock.UtcNow;
        if (a.Status == AttemptStatus.Submitted && a.AllQuestions.Any(q => q.NeedsMarking && q.QuestionType == QuestionType.Essay))
            await jobs.EnqueueAsync(JobTypes.SuggestMarks, a.Id, ct);
    }

    // ---------------- taking a test ----------------

    public async Task<Attempt> StartAsync(Invitation inv, CancellationToken ct = default)
    {
        var open = await OpenAttemptAsync(inv, ct);
        if (open != null) return open;
        var now = clock.UtcNow;
        var reason = inv.CannotStartReason(now);
        if (reason.Length > 0) throw DomainException.Conflict(reason);
        var asm = await db.Assessments
            .Include(x => x.Translations)
            .Include(x => x.Sections).ThenInclude(s => s.Items)
            .Include(x => x.Sections).ThenInclude(s => s.Translations)
            .AsSplitQuery()
            .FirstAsync(x => x.Id == inv.AssessmentId, ct);
        var bank = await BankAsync(ct);
        var no = (byte)(inv.AttemptsUsed + 1);
        var approvedLangs = asm.Translations.Where(t => t.ReviewStatus == ReviewStatus.Approved).Select(t => t.LanguageCode).ToList();
        var sections = AttemptBuilder.Build(asm, bank, $"{inv.Code}:{no}:{Guid.NewGuid():N}", approvedLangs);
        if (sections.Count == 0) throw DomainException.Conflict("assessment_unavailable");
        var a = new Attempt { InvitationId = inv.Id, AttemptNo = no, Status = AttemptStatus.InProgress, StartedAt = now, Sections = sections };
        a.StartSection(sections[0].SectionNo, inv.ExtraTimePercent, now);
        db.Attempts.Add(a);
        inv.Attempts.Add(a);
        inv.AttemptsUsed = no;
        inv.Status = InvitationStatus.InProgress;
        inv.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return a;
    }

    public async Task<List<Question>> BankAsync(CancellationToken ct) =>
        await db.Questions.AsNoTracking().Where(q => q.IsActive)
            .Include(q => q.Options).ThenInclude(o => o.Translations)
            .Include(q => q.AcceptedAnswers)
            .Include(q => q.Translations)
            .AsSplitQuery().ToListAsync(ct);

    /// <summary>What the candidate's browser needs for the current section: no correct answers, accepted answers or rubrics.</summary>
    public object State(Attempt a, string lang)
    {
        var now = clock.UtcNow;
        var sec = a.CurrentSection;
        var (title, instructions) = sec.In(lang);
        int? remaining = a.SectionEndsAt == null ? null : Math.Max(0, (int)(a.SectionEndsAt.Value - now).TotalSeconds);
        var answers = new Dictionary<string, object?>();
        foreach (var q in sec.OrderedQuestions.Where(q => q.IsAnswered)) answers[q.QuestionKey] = WireAnswer(q);
        return new
        {
            attempt = (int)a.AttemptNo,
            section_index = sec.SectionNo,
            section_count = a.Sections.Count,
            sections = a.OrderedSections.Select(s => new { title = s.In(lang).Title, minutes = s.Minutes, questions = s.Questions.Count }).ToList(),
            section = new
            {
                title, instructions, remaining_seconds = remaining,
                questions = sec.OrderedQuestions.Select(q => new
                {
                    key = q.QuestionKey, type = Labels.Wire(q.QuestionType), text = q.TextIn(lang), points = q.Points, max_words = q.MaxWords,
                    options = q.OrderedOptions.Select(o => new { key = o.OptionKey, text = o.TextIn(lang) }).ToList(),
                }).ToList(),
            },
            answers,
        };
    }

    static object? WireAnswer(AttemptQuestion q) => q.QuestionType switch
    {
        QuestionType.SingleChoice or QuestionType.TrueFalse or QuestionType.Likert => q.Options.FirstOrDefault(o => o.IsSelected)?.OptionKey,
        QuestionType.MultipleChoice => q.OrderedOptions.Where(o => o.IsSelected).Select(o => o.OptionKey).ToList(),
        QuestionType.FileUpload => new { file = q.AnswerFileId?.ToString("N"), name = q.AnswerFileName },
        _ => q.AnswerText,
    };

    /// <summary>The answer the browser sent (JSON: a string, a list of option keys, or null).</summary>
    public static AnswerInput ParseAnswer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new AnswerInput(null, null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            return e.ValueKind switch
            {
                JsonValueKind.String => new AnswerInput(e.GetString(), [e.GetString() ?? ""], null, null),
                JsonValueKind.Number => new AnswerInput(e.GetRawText(), null, null, null),
                JsonValueKind.Array => new AnswerInput(null, e.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.GetRawText()).ToList(), null, null),
                JsonValueKind.Null => new AnswerInput(null, null, null, null),
                _ => throw DomainException.Invalid("invalid_answer"),
            };
        }
        catch (JsonException)
        {
            throw DomainException.Invalid("invalid_answer");
        }
    }

    public async Task SaveAnswerAsync(Invitation inv, Attempt a, int section, string key, AnswerInput answer, CancellationToken ct)
    {
        if (section != a.CurrentSectionNo) throw DomainException.Conflict("section_closed");
        a.SaveAnswer(key, answer, clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Locks the current section and opens the next (or submits after the last). Ignored for a stale page.</summary>
    public async Task<Attempt?> NextSectionAsync(Invitation inv, Attempt a, int section, CancellationToken ct)
    {
        if (section == a.CurrentSectionNo)
        {
            a.Advance(inv.ExtraTimePercent, clock.UtcNow);
            if (a.Status != AttemptStatus.InProgress) await FinaliseAsync(inv, a, ct);
            await db.SaveChangesAsync(ct);
        }
        return a.Status == AttemptStatus.InProgress ? a : null;
    }

    public async Task LogEventAsync(Attempt a, string kind, CancellationToken ct)
    {
        if (kind == "hidden") a.TabSwitches++;
        else if (kind == "paste") a.Pastes++;
        else return;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>One invitation as the candidate sees it.</summary>
    public async Task<object> CandidateSummaryAsync(Invitation inv, string lang, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var asm = inv.Assessment!;
        var (title, instructions) = asm.In(lang);
        var open = inv.Status == InvitationStatus.InProgress ? await OpenAttemptAsync(inv, ct) : null;
        var reason = inv.CannotStartReason(now);
        var showScore = asm.ShowScoreToCandidate && inv.Status == InvitationStatus.Marked;
        return new
        {
            id = inv.Code, title, type = Labels.AssessmentTypeName(asm.AssessmentType),
            status = open != null ? "in_progress" : inv.CandidateStatus(now),
            deadline = clock.LocalMinute(inv.DeadlineAt),
            attempts_allowed = (int)inv.AttemptsAllowed, attempts_used = (int)inv.AttemptsUsed,
            can_start = open != null || reason.Length == 0, reason = open != null ? "" : reason,
            minutes = asm.TotalMinutes, extra_time_percent = (int)inv.ExtraTimePercent,
            sections = asm.OrderedSections.Select(s => new { title = asm.SectionTitleIn(s, lang), minutes = s.Minutes }).ToList(),
            instructions,
            score = showScore ? inv.Score : null,
            result = showScore && inv.Result is { } r and not InvitationResult.AwaitingMarking ? r.ToString() : "",
            job_title = inv.JobApplication?.JobOpening?.In(lang).Title ?? "",
        };
    }
}
