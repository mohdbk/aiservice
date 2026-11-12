using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Assessments;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record ApplicantFilter(int? JobId, string? Status, string? Screening, string? Q, decimal? MinScore, string? Sort, int Page);

/// <summary>The applicant pipeline: lists, the applicant's full record, status changes, notes and re-scoring.</summary>
public sealed class BackofficeApplicantService(IAppDbContext db, ICurrentActor actor, IClock clock, JobQueue jobs, AssessmentWorkflow workflow)
{
    const int PageSize = 25;

    public async Task<object> ListAsync(ApplicantFilter f, CancellationToken ct = default)
    {
        var q = db.JobApplications.AsNoTracking().AsQueryable();
        if (f.JobId != null) q = q.Where(a => a.JobOpeningId == f.JobId);
        if (Enum.TryParse<ApplicationStatus>(f.Status, out var st)) q = q.Where(a => a.Status == st);
        if (Enum.TryParse<ScreeningOutcome>(f.Screening, out var so)) q = q.Where(a => a.ScreeningOutcome == so);
        if (f.MinScore != null) q = q.Where(a => a.CvAnalysis!.Score >= f.MinScore);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var t = f.Q.Trim();
            q = q.Where(a => a.FullName.Contains(t) || a.Email.Contains(t) || a.Code.Contains(t));
        }
        var total = await q.CountAsync(ct);
        q = f.Sort switch
        {
            "score" => q.OrderByDescending(a => a.CvAnalysis!.Score).ThenByDescending(a => a.CreatedAt),
            "oldest" => q.OrderBy(a => a.CreatedAt),
            "name" => q.OrderBy(a => a.FullName),
            _ => q.OrderByDescending(a => a.CreatedAt),
        };
        var page = Math.Max(1, f.Page);
        var rows = await q.Skip((page - 1) * PageSize).Take(PageSize).Select(a => new
        {
            id = a.Id, code = a.Code, full_name = a.FullName, email = a.Email, job_id = a.JobOpeningId, job_code = a.JobOpening!.Code,
            job_title = a.JobOpening.Title, status = a.Status, screening = a.ScreeningOutcome,
            score = a.CvAnalysis == null ? null : a.CvAnalysis.Score, ai_status = a.CvAnalysis == null ? (CvAnalysisStatus?)null : a.CvAnalysis.Status, created_at = a.CreatedAt,
            interviews = db.Interviews.Count(i => i.JobApplicationId == a.Id && i.Status != InterviewStatus.Cancelled),
        }).ToListAsync(ct);
        return new { applicants = rows, total, page, pages = Math.Max(1, (total + PageSize - 1) / PageSize) };
    }

    public async Task<object> GetAsync(int id, CancellationToken ct = default)
    {
        var a = await db.JobApplications.AsNoTracking().Where(x => x.Id == id)
                    .Include(x => x.JobOpening)
                    .Include(x => x.ScreeningAnswers)
                    .Include(x => x.CvAnalysis!).ThenInclude(c => c.CriterionScores)
                    .Include(x => x.Notes).ThenInclude(n => n.AuthorStaff)
                    .AsSplitQuery().FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
        var cv = await db.StoredFiles.AsNoTracking().Where(f => f.Id == a.CvFileId)
            .Select(f => new { f.FileName, f.ContentType, f.SizeBytes }).FirstOrDefaultAsync(ct);
        var interviews = await db.Interviews.AsNoTracking().Where(i => i.JobApplicationId == id).Include(i => i.InterviewRound)
            .OrderByDescending(i => i.StartsAt).Select(i => new
            {
                id = i.Id, code = i.Code, round = i.InterviewRound!.Name, starts_at = i.StartsAt, ends_at = i.EndsAt,
                status = i.Status, feedback = i.Feedback.Count,
            }).ToListAsync(ct);
        var offers = await db.JobOffers.AsNoTracking().Where(o => o.JobApplicationId == id).OrderByDescending(o => o.CreatedAt)
            .Select(o => new { id = o.Id, code = o.Code, position_title = o.PositionTitle, status = o.Status, offer_date = o.OfferDate })
            .ToListAsync(ct);
        var invitations = await db.Invitations.AsNoTracking().Where(i => i.JobApplicationId == id).Include(i => i.Assessment)
            .OrderByDescending(i => i.CreatedAt).Select(i => new
            {
                id = i.Id, code = i.Code, assessment_id = i.AssessmentId, title = i.Assessment!.Title, status = i.Status,
                deadline_at = i.DeadlineAt, attempts_used = (int)i.AttemptsUsed, attempts_allowed = (int)i.AttemptsAllowed,
                score = i.Score, result = i.Result,
                attempts = i.Attempts.OrderBy(t => t.AttemptNo).Select(t => new
                {
                    id = t.Id, attempt_no = (int)t.AttemptNo, status = t.Status, percent = t.Percent, passed = t.Passed,
                    tab_switches = t.TabSwitches, pastes = t.Pastes, submitted_at = t.SubmittedAt,
                }).ToList(),
            }).ToListAsync(ct);
        var c = a.CvAnalysis;
        return new
        {
            id = a.Id, code = a.Code, reference = a.Reference, full_name = a.FullName, email = a.Email, phone = a.Phone, country = a.Country,
            cover_note = a.CoverNote, status = a.Status.ToString(), language = a.LanguageCode, created_at = a.CreatedAt,
            job = new { id = a.JobOpeningId, code = a.JobOpening!.Code, title = a.JobOpening.Title },
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
            interviews, offers, invitations,
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
        return new { status = a.Status.ToString() };
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
