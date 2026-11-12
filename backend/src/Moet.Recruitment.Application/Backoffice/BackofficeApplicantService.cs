using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Assessments;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record ApplicantFilter(int? JobId, string? Status, string? Screening, string? AiStatus, decimal? MinScore, string? Country, DateOnly? From, DateOnly? To, string? CvText = null);

/// <summary>The applicant pipeline: lists, the applicant's full record, status changes, notes and re-scoring.</summary>
public sealed class BackofficeApplicantService(IAppDbContext db, ICurrentActor actor, IRequestLanguage language, IClock clock, JobQueue jobs, AssessmentWorkflow workflow,
    PortalMailer mailer, ContentService content)
{
    static readonly Sorts<JobApplication> AppSorts = new Sorts<JobApplication>("created")
        .Add("created", a => a.CreatedAt).Add("name", a => a.FullName).Add("code", a => a.Code).Add("status", a => a.Status)
        .Add("score", a => a.CvAnalysis!.Score).Add("job", a => a.JobOpening!.Title).Add("screening", a => a.ScreeningOutcome);

    public async Task<object> ListAsync(ListQuery lq, ApplicantFilter f, CancellationToken ct = default)
    {
        var q = db.JobApplications.AsNoTracking().AsQueryable();
        if (f.JobId != null) q = q.Where(a => a.JobOpeningId == f.JobId);
        if (Enum.TryParse<ApplicationStatus>(f.Status, out var st)) q = q.Where(a => a.Status == st);
        if (Enum.TryParse<ScreeningOutcome>(f.Screening, out var so)) q = q.Where(a => a.ScreeningOutcome == so);
        if (Enum.TryParse<CvAnalysisStatus>(f.AiStatus, out var ai)) q = q.Where(a => a.CvAnalysis!.Status == ai);
        if (f.MinScore != null) q = q.Where(a => a.CvAnalysis!.Score >= f.MinScore);
        if (!string.IsNullOrEmpty(f.Country)) q = q.Where(a => a.Country == f.Country);
        if (f.From != null) q = q.Where(a => a.CreatedAt >= f.From.Value.ToDateTime(TimeOnly.MinValue));
        if (f.To != null) q = q.Where(a => a.CreatedAt < f.To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (!string.IsNullOrWhiteSpace(f.CvText))
        {
            var cv = f.CvText.Trim();
            q = q.Where(a => a.CvAnalysis!.Summary.Contains(cv) || a.CvAnalysis.ProfileJson!.Contains(cv));
        }
        if (lq.Term.Length > 0)
        {
            var t = lq.Term;
            q = q.Where(a => a.FullName.Contains(t) || a.Email.Contains(t) || a.Code.Contains(t) || a.Phone.Contains(t));
        }
        var en = language.English;
        return await q.PageAsync(lq, AppSorts, a => new
        {
            id = a.Id, code = a.Code, full_name = a.FullName, email = a.Email, job_id = a.JobOpeningId, job_code = a.JobOpening!.Code,
            job_title = (en ? a.JobOpening.Translations.Where(t => t.LanguageCode == "en" && t.Title != "").Select(t => t.Title).FirstOrDefault() ?? a.JobOpening.Title : a.JobOpening.Title), status = a.Status, screening = a.ScreeningOutcome, country = a.Country,
            score = a.CvAnalysis == null ? null : a.CvAnalysis.Score, ai_status = a.CvAnalysis == null ? (CvAnalysisStatus?)null : a.CvAnalysis.Status,
            created_at = a.CreatedAt,
            interviews = db.Interviews.Count(i => i.JobApplicationId == a.Id && i.Status != InterviewStatus.Cancelled),
        }, ct);
    }

    public async Task<object> GetAsync(int id, CancellationToken ct = default)
    {
        var a = await db.JobApplications.AsNoTracking().Where(x => x.Id == id)
                    .Include(x => x.JobOpening!).ThenInclude(j => j.Translations)
                    .Include(x => x.ScreeningAnswers)
                    .Include(x => x.CvAnalysis!).ThenInclude(c => c.CriterionScores)
                    .Include(x => x.Notes).ThenInclude(n => n.AuthorStaff)
                    .AsSplitQuery().FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
        var en = language.English;
        var cv = await db.StoredFiles.AsNoTracking().Where(f => f.Id == a.CvFileId)
            .Select(f => new { f.FileName, f.ContentType, f.SizeBytes }).FirstOrDefaultAsync(ct);
        var interviews = await db.Interviews.AsNoTracking().Where(i => i.JobApplicationId == id).Include(i => i.InterviewRound)
            .OrderByDescending(i => i.StartsAt).Select(i => new
            {
                id = i.Id, code = i.Code, round = (en && i.InterviewRound!.NameEn != "" ? i.InterviewRound!.NameEn : i.InterviewRound!.Name), starts_at = i.StartsAt, ends_at = i.EndsAt,
                status = i.Status, feedback = i.Feedback.Count,
            }).ToListAsync(ct);
        var offers = await db.JobOffers.AsNoTracking().Where(o => o.JobApplicationId == id).OrderByDescending(o => o.CreatedAt)
            .Select(o => new { id = o.Id, code = o.Code, position_title = o.PositionTitle, status = o.Status, offer_date = o.OfferDate })
            .ToListAsync(ct);
        var invitations = await db.Invitations.AsNoTracking().Where(i => i.JobApplicationId == id).Include(i => i.Assessment)
            .OrderByDescending(i => i.CreatedAt).Select(i => new
            {
                id = i.Id, code = i.Code, assessment_id = i.AssessmentId, title = (en ? i.Assessment!.Translations.Where(t => t.LanguageCode == "en" && t.Title != "").Select(t => t.Title).FirstOrDefault() ?? i.Assessment!.Title : i.Assessment!.Title), status = i.Status,
                personality_model = i.Assessment.PersonalityModel,
                deadline_at = i.DeadlineAt, attempts_used = (int)i.AttemptsUsed, attempts_allowed = (int)i.AttemptsAllowed,
                score = i.Score, result = i.Result,
                attempts = i.Attempts.OrderBy(t => t.AttemptNo).Select(t => new
                {
                    id = t.Id, attempt_no = (int)t.AttemptNo, status = t.Status, percent = t.Percent, passed = t.Passed,
                    tab_switches = t.TabSwitches, pastes = t.Pastes, submitted_at = t.SubmittedAt, trait_profile = t.TraitProfile,
                }).ToList(),
            }).ToListAsync(ct);
        var roles = invitations.Any(i => i.personality_model != PersonalityModel.None) ? await db.RoleProfiles.AsNoTracking().Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.NameEn).ToListAsync(ct) : [];
        // Per invitation: the 16-type letters and notes, and the fit with each role profile (from the latest completed attempt).
        var personality = invitations.Where(i => i.personality_model != PersonalityModel.None).ToDictionary(i => i.id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            i => RoleProfileService.Summary(i.personality_model, i.attempts.LastOrDefault(t => t.trait_profile != null)?.trait_profile, roles));
        var c = a.CvAnalysis;
        return new
        {
            id = a.Id, code = a.Code, reference = a.Reference, full_name = a.FullName, email = a.Email, phone = a.Phone, country = a.Country,
            cover_note = a.CoverNote, status = a.Status.ToString(), language = a.LanguageCode, created_at = a.CreatedAt,
            job = new { id = a.JobOpeningId, code = a.JobOpening!.Code, title = a.JobOpening.In(en ? "en" : "ar").Title },
            cv = cv == null ? null : new { file_name = cv.FileName, content_type = cv.ContentType, size = cv.SizeBytes, url = $"/api/backoffice/applicants/{a.Id}/cv" },
            screening = new
            {
                outcome = a.ScreeningOutcome.ToString(),
                answers = a.ScreeningAnswers.OrderBy(s => s.SortOrder).Select(s => new { question = s.QuestionText, answer = s.AnswerText, meets = s.MeetsRequirement }).ToList(),
            },
            analysis = c == null ? null : new
            {
                status = c.Status.ToString(), score = c.Score, summary = c.Summary, summary_ar = c.SummaryArabic, gaps = c.MustHaveGaps,
                years = c.YearsOfExperience, cv_language = c.CvLanguage, read_method = c.ReadMethod, model = c.Model, scored_at = c.ScoredAt,
                error = c.Error, profile = c.ProfileJson,
                criteria = c.CriterionScores.OrderBy(s => s.SortOrder).Select(s => new { s.Criterion, s.Weight, s.IsMustHave, s.Score, s.Evidence }).ToList(),
            },
            notes = a.Notes.OrderByDescending(n => n.CreatedAt).Select(n => new
            {
                id = n.Id, kind = n.Kind.ToString(), text = n.Text, created_at = n.CreatedAt,
                author = n.AuthorStaff?.FullName ?? (n.AuthorApplicantId != null ? a.FullName : "System"),
            }).ToList(),
            interviews, offers, invitations, personality,
        };
    }

    public async Task<(byte[] Content, string ContentType, string FileName)> CvAsync(int id, CancellationToken ct = default)
    {
        var fileId = await db.JobApplications.Where(a => a.Id == id).Select(a => (Guid?)a.CvFileId).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
        var f = await db.StoredFiles.AsNoTracking().FirstAsync(x => x.Id == fileId, ct);
        return (f.Content, f.ContentType, f.FileName);
    }

    public async Task<object> SetStatusAsync(int id, string? status, CancellationToken ct = default)
    {
        var a = await db.JobApplications.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        var next = BackofficeJobService.Parse<ApplicationStatus>(status, "status");
        if (a.Status == next) return new { status = a.Status.ToString() };
        var now = clock.UtcNow;
        db.ApplicationNotes.Add(new ApplicationNote
        {
            JobApplicationId = a.Id, Kind = NoteKind.System, Text = $"Status changed from {a.Status} to {next} by {actor.Name}.",
            AuthorStaffId = actor.Id, CreatedAt = now,
        });
        a.Status = next;
        a.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        // "Not Selected" email, when HR has switched it on.
        var emailed = false;
        if (next == ApplicationStatus.Rejected)
        {
            var job = await db.JobOpenings.AsNoTracking().Include(j => j.Translations).FirstAsync(j => j.Id == a.JobOpeningId, ct);
            var lang = await content.PortalLanguageAsync(a.LanguageCode, ct);
            emailed = await mailer.QueueAsync(a.Email, a.FullName, lang, "not_selected", "",
                new Dictionary<string, string> { ["job_title"] = job.In(lang).Title, ["reference"] = a.Reference }, ct);
            if (emailed) await db.SaveChangesAsync(ct);
        }
        return new { status = a.Status.ToString(), emailed };
    }

    public async Task<object> AddNoteAsync(int id, string? text, CancellationToken ct = default)
    {
        if (!await db.JobApplications.AnyAsync(a => a.Id == id, ct)) throw DomainException.NotFound();
        text = (text ?? "").Trim();
        if (text.Length is < 1 or > 4000) throw DomainException.Invalid("note_length");
        db.ApplicationNotes.Add(new ApplicationNote { JobApplicationId = id, Kind = NoteKind.Note, Text = text, AuthorStaffId = actor.Id, CreatedAt = clock.UtcNow });
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    public async Task<object> RescoreAsync(int id, CancellationToken ct = default)
    {
        var a = await db.JobApplications.Include(x => x.CvAnalysis).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        a.CvAnalysis ??= new CvAnalysis { JobApplicationId = a.Id };
        a.CvAnalysis.Status = CvAnalysisStatus.Pending;
        a.CvAnalysis.Error = "";
        a.CvAnalysis.UpdatedAt = clock.UtcNow;
        await jobs.EnqueueAsync(JobTypes.ScoreApplication, a.Id, ct);
        await db.SaveChangesAsync(ct);
        return new { queued = true };
    }

    public async Task<object> InviteAsync(int id, int assessmentId, int? deadlineDays, CancellationToken ct = default)
    {
        var a = await db.JobApplications.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        var inv = await workflow.InviteAsync(a, assessmentId, deadlineDays, true, actor.Id, ct);
        await db.SaveChangesAsync(ct);
        return new { id = inv.Id, code = inv.Code };
    }
}
