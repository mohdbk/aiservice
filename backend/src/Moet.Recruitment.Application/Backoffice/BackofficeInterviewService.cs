using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record InterviewInput(int ApplicationId, int RoundId, string Date, string From, string To, string? Location, string? Notes, IReadOnlyList<int>? Panelists);

public sealed record FeedbackInput(string Recommendation, string Comments, IReadOnlyList<RatingInput>? Ratings);

public sealed record RatingInput(string Skill, int Rating);

public sealed record OfferInput(int ApplicationId, string PositionTitle, string OfferDate, string? StartDate, decimal? MonthlySalary, string? Currency, string? Terms,
    int? OfferTemplateId = null);

public sealed class BackofficeInterviewService(IAppDbContext db, ICodeGenerator codes, ICurrentActor actor, IClock clock, OfferLetterService letters,
    PortalMailer mailer, ContentService content)
{
    static readonly Sorts<Interview> InterviewSorts = new Sorts<Interview>("starts", false)
        .Add("starts", i => i.StartsAt).Add("applicant", i => i.JobApplication!.FullName).Add("round", i => i.InterviewRound!.Name)
        .Add("status", i => i.Status).Add("code", i => i.Code);

    public async Task<object> ListAsync(ListQuery lq, string? scope, string? status, int? roundId, int? jobId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var q = db.Interviews.AsNoTracking().AsQueryable();
        q = scope switch
        {
            "mine" => q.Where(i => i.Panelists.Any(p => p.StaffUserId == actor.Id) && i.Status != InterviewStatus.Cancelled),
            "past" => q.Where(i => i.EndsAt < now),
            "review" => q.Where(i => i.Status == InterviewStatus.UnderReview),
            "all" => q,
            _ => q.Where(i => i.EndsAt >= now.AddHours(-12) && i.Status != InterviewStatus.Cancelled),
        };
        if (Enum.TryParse<InterviewStatus>(status, out var st)) q = q.Where(i => i.Status == st);
        if (roundId != null) q = q.Where(i => i.InterviewRoundId == roundId);
        if (jobId != null) q = q.Where(i => i.JobApplication!.JobOpeningId == jobId);
        if (lq.Term.Length > 0) q = q.Where(i => i.JobApplication!.FullName.Contains(lq.Term) || i.Code.Contains(lq.Term));
        var sorts = scope == "past" && lq.Sort == null ? new Sorts<Interview>("starts").Add("starts", i => i.StartsAt) : InterviewSorts;
        return await q.PageAsync(lq, sorts, i => new
        {
            id = i.Id, code = i.Code, starts_at = i.StartsAt, ends_at = i.EndsAt, status = i.Status, location = i.Location,
            round = i.InterviewRound!.Name, applicant_id = i.JobApplicationId, applicant = i.JobApplication!.FullName,
            job_title = i.JobApplication.JobOpening!.Title, panel = i.Panelists.Select(p => p.StaffUser!.FullName).ToList(),
            feedback = i.Feedback.Count,
        }, ct);
    }

    public async Task<object> GetAsync(int id, CancellationToken ct = default)
    {
        var i = await db.Interviews.AsNoTracking().Where(x => x.Id == id)
                    .Include(x => x.InterviewRound!).ThenInclude(r => r.Skills)
                    .Include(x => x.JobApplication!).ThenInclude(a => a.JobOpening)
                    .Include(x => x.Panelists).ThenInclude(p => p.StaffUser)
                    .Include(x => x.Feedback).ThenInclude(f => f.StaffUser)
                    .Include(x => x.Feedback).ThenInclude(f => f.Ratings)
                    .AsSplitQuery().FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
        return new
        {
            id = i.Id, code = i.Code, starts_at = i.StartsAt, ends_at = i.EndsAt, status = i.Status.ToString(), location = i.Location, notes = i.Notes,
            round = new { id = i.InterviewRoundId, name = i.InterviewRound!.Name, skills = i.InterviewRound.Skills.OrderBy(s => s.SortOrder).Select(s => s.Skill).ToList() },
            applicant = new { id = i.JobApplicationId, name = i.JobApplication!.FullName, job_title = i.JobApplication.JobOpening!.Title },
            panel = i.Panelists.Select(p => new { id = p.StaffUserId, name = p.StaffUser!.FullName }).ToList(),
            feedback = i.Feedback.OrderBy(f => f.SubmittedAt).Select(f => new
            {
                staff_id = f.StaffUserId, staff = f.StaffUser!.FullName, recommendation = f.Recommendation.ToString(), comments = f.Comments,
                submitted_at = f.SubmittedAt, ratings = f.Ratings.Select(r => new { r.Skill, rating = (int)r.Rating }).ToList(),
            }).ToList(),
            can_give_feedback = i.Panelists.Any(p => p.StaffUserId == actor.Id) && i.Status != InterviewStatus.Cancelled,
        };
    }

    public async Task<object> ScheduleAsync(int? id, InterviewInput input, CancellationToken ct = default)
    {
        if (!DateOnly.TryParseExact(input.Date, "yyyy-MM-dd", out var day) || !TimeOnly.TryParse(input.From, out var from) || !TimeOnly.TryParse(input.To, out var to))
            throw DomainException.Invalid("invalid_date");
        if (!await db.InterviewRounds.AnyAsync(r => r.Id == input.RoundId, ct)) throw DomainException.Invalid("round_required");
        var now = clock.UtcNow;
        Interview i;
        if (id == null)
        {
            if (!await db.JobApplications.AnyAsync(a => a.Id == input.ApplicationId, ct)) throw DomainException.NotFound();
            i = new Interview { Code = await codes.NextAsync(CodeKind.Interview, ct), JobApplicationId = input.ApplicationId, CreatedAt = now, CreatedByStaffId = actor.Id };
            db.Interviews.Add(i);
        }
        else
        {
            i = await db.Interviews.Include(x => x.Panelists).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        }
        i.InterviewRoundId = input.RoundId;
        i.StartsAt = clock.ToUtc(day, from);
        i.EndsAt = clock.ToUtc(day, to);
        i.Location = TextUtil.Clip(input.Location?.Trim(), 300);
        i.Notes = TextUtil.Clip(input.Notes?.Trim(), 2000);
        i.UpdatedAt = now;
        i.Validate();
        var panel = (input.Panelists ?? []).Distinct().ToList();
        var valid = await db.StaffUsers.Where(u => panel.Contains(u.Id) && u.IsEnabled).Select(u => u.Id).ToListAsync(ct);
        i.Panelists.RemoveAll(p => !valid.Contains(p.StaffUserId));
        foreach (var sid in valid.Where(v => i.Panelists.All(p => p.StaffUserId != v))) i.Panelists.Add(new InterviewPanelist { StaffUserId = sid });
        await db.SaveChangesAsync(ct);
        // A new interview tells the candidate when HR has switched on the "Interview Scheduled" email.
        var emailed = id == null && await EmailInterviewAsync(i, input, ct);
        return new { id = i.Id, code = i.Code, emailed };
    }

    async Task<bool> EmailInterviewAsync(Interview i, InterviewInput input, CancellationToken ct)
    {
        var app = await db.JobApplications.AsNoTracking().Include(a => a.JobOpening!).ThenInclude(j => j.Translations).FirstAsync(a => a.Id == i.JobApplicationId, ct);
        var round = await db.InterviewRounds.AsNoTracking().Where(r => r.Id == i.InterviewRoundId).Select(r => r.Name).FirstAsync(ct);
        var lang = await content.PortalLanguageAsync(app.LanguageCode, ct);
        var sent = await mailer.QueueAsync(app.Email, app.FullName, lang, "interview_scheduled", "", new Dictionary<string, string>
        {
            ["job_title"] = app.JobOpening!.In(lang).Title, ["round"] = round, ["date"] = input.Date, ["time"] = $"{input.From} - {input.To}",
            ["location"] = i.Location,
        }, ct);
        if (sent) await db.SaveChangesAsync(ct);
        return sent;
    }

    public async Task<object> SetStatusAsync(int id, string? status, CancellationToken ct = default)
    {
        var i = await db.Interviews.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        i.Status = BackofficeJobService.Parse<InterviewStatus>(status, "status");
        i.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { status = i.Status.ToString() };
    }

    public async Task<object> FeedbackAsync(int id, FeedbackInput input, CancellationToken ct = default)
    {
        var i = await db.Interviews.Include(x => x.Panelists).Include(x => x.Feedback).ThenInclude(f => f.Ratings)
                    .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        if (actor.Id is not { } staffId || i.Panelists.All(p => p.StaffUserId != staffId)) throw DomainException.Forbidden("not_on_panel");
        i.GiveFeedback(staffId, BackofficeJobService.Parse<Recommendation>(input.Recommendation, "recommendation"), input.Comments,
            (input.Ratings ?? []).Select(r => (r.Skill, (byte)Math.Clamp(r.Rating, 0, 9))), clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    // ---------------- offers ----------------

    static readonly Sorts<JobOffer> OfferSorts = new Sorts<JobOffer>("created")
        .Add("created", o => o.CreatedAt).Add("code", o => o.Code).Add("offer_date", o => o.OfferDate).Add("start_date", o => o.StartDate)
        .Add("salary", o => o.MonthlySalary).Add("status", o => o.Status).Add("applicant", o => o.JobApplication!.FullName);

    public async Task<object> OffersAsync(ListQuery lq, string? status, CancellationToken ct = default)
    {
        var q = db.JobOffers.AsNoTracking().AsQueryable();
        if (Enum.TryParse<OfferStatus>(status, out var st)) q = q.Where(o => o.Status == st);
        if (lq.Term.Length > 0) q = q.Where(o => o.JobApplication!.FullName.Contains(lq.Term) || o.Code.Contains(lq.Term) || o.PositionTitle.Contains(lq.Term));
        return await q.PageAsync(lq, OfferSorts, o => new
        {
            id = o.Id, code = o.Code, position_title = o.PositionTitle, status = o.Status, offer_date = o.OfferDate,
            start_date = o.StartDate, monthly_salary = o.MonthlySalary, currency = o.Currency,
            applicant_id = o.JobApplicationId, applicant = o.JobApplication!.FullName, job_title = o.JobApplication.JobOpening!.Title,
        }, ct);
    }

    public async Task<object> OfferAsync(int id, CancellationToken ct = default)
    {
        var o = await db.JobOffers.AsNoTracking().Include(x => x.JobApplication!).ThenInclude(a => a.JobOpening)
                    .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        return new
        {
            id = o.Id, code = o.Code, position_title = o.PositionTitle, status = o.Status.ToString(), offer_date = o.OfferDate,
            start_date = o.StartDate, monthly_salary = o.MonthlySalary, currency = o.Currency, terms = o.Terms, offer_template_id = o.OfferTemplateId,
            applicant = new { id = o.JobApplicationId, name = o.JobApplication!.FullName, job_title = o.JobApplication.JobOpening!.Title },
        };
    }

    public async Task<object> SaveOfferAsync(int? id, OfferInput input, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        JobOffer o;
        if (id == null)
        {
            if (!await db.JobApplications.AnyAsync(a => a.Id == input.ApplicationId, ct)) throw DomainException.NotFound();
            o = new JobOffer { Code = await codes.NextAsync(CodeKind.JobOffer, ct), JobApplicationId = input.ApplicationId, CreatedAt = now, CreatedByStaffId = actor.Id };
            db.JobOffers.Add(o);
        }
        else
        {
            o = await db.JobOffers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
            if (o.Status is not OfferStatus.Draft) throw DomainException.Conflict("offer_locked");
        }
        if (string.IsNullOrWhiteSpace(input.PositionTitle)) throw DomainException.Invalid("title_required");
        o.PositionTitle = TextUtil.Clip(input.PositionTitle.Trim(), 140);
        o.OfferDate = BackofficeJobService.ParseDate(input.OfferDate) ?? clock.Today();
        o.StartDate = BackofficeJobService.ParseDate(input.StartDate);
        o.MonthlySalary = input.MonthlySalary;
        o.Currency = string.IsNullOrWhiteSpace(input.Currency) ? "AED" : TextUtil.Clip(input.Currency.Trim().ToUpperInvariant(), 3);
        o.Terms = TextUtil.Clip(input.Terms, 20000);
        if (input.OfferTemplateId is { } tid)
        {
            if (!await db.OfferTemplates.AnyAsync(t => t.Id == tid && t.IsActive, ct)) throw DomainException.Invalid("offer_template_invalid");
            o.OfferTemplateId = tid;
        }
        else o.OfferTemplateId ??= await db.OfferTemplates.Where(t => t.IsDefault && t.IsActive).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
        o.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new { id = o.Id, code = o.Code };
    }

    public async Task<object> SetOfferStatusAsync(int id, string? status, CancellationToken ct = default)
    {
        var o = await db.JobOffers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        o.MoveTo(BackofficeJobService.Parse<OfferStatus>(status, "status"), clock.UtcNow);
        if (o.Status == OfferStatus.Accepted)
        {
            var app = await db.JobApplications.FirstAsync(a => a.Id == o.JobApplicationId, ct);
            app.Status = ApplicationStatus.Accepted;
            app.UpdatedAt = clock.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        // Sending the offer emails the letter to the candidate when HR has switched on the "Job Offer" email.
        var emailed = false;
        if (o.Status == OfferStatus.Sent && await letters.EmailOfferAsync(o.Id, ct))
        {
            await db.SaveChangesAsync(ct);
            emailed = true;
        }
        return new { status = o.Status.ToString(), emailed };
    }
}
