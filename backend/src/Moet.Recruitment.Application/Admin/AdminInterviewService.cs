using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;

namespace Moet.Recruitment.Application.Admin;

public sealed record InterviewInput(int ApplicationId, int RoundId, string Date, string From, string To, string? Location, string? Notes, IReadOnlyList<int>? Panelists);

public sealed record FeedbackInput(string Recommendation, string Comments, IReadOnlyList<RatingInput>? Ratings);

public sealed record RatingInput(string Skill, int Rating);

public sealed record OfferInput(int ApplicationId, string PositionTitle, string OfferDate, string? StartDate, decimal? MonthlySalary, string? Currency, string? Terms);

public sealed class AdminInterviewService(IAppDbContext db, ICodeGenerator codes, ICurrentActor actor, IClock clock)
{
    public async Task<object> ListAsync(string? scope, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var q = db.Interviews.AsNoTracking().AsQueryable();
        q = scope switch
        {
            "mine" => q.Where(i => i.Panelists.Any(p => p.StaffUserId == actor.Id) && i.Status != InterviewStatus.Cancelled),
            "past" => q.Where(i => i.EndsAt < now),
            "review" => q.Where(i => i.Status == InterviewStatus.UnderReview),
            _ => q.Where(i => i.EndsAt >= now.AddHours(-12) && i.Status != InterviewStatus.Cancelled),
        };
        var rows = await (scope == "past" ? q.OrderByDescending(i => i.StartsAt) : q.OrderBy(i => i.StartsAt)).Take(200).Select(i => new
        {
            id = i.Id, code = i.Code, starts_at = i.StartsAt, ends_at = i.EndsAt, status = i.Status, location = i.Location,
            round = i.InterviewRound!.Name, applicant_id = i.JobApplicationId, applicant = i.JobApplication!.FullName,
            job_title = i.JobApplication.JobOpening!.Title, panel = i.Panelists.Select(p => p.StaffUser!.FullName).ToList(),
            feedback = i.Feedback.Count,
        }).ToListAsync(ct);
        return new { interviews = rows };
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
        return new { id = i.Id, code = i.Code };
    }

    public async Task<object> SetStatusAsync(int id, string? status, CancellationToken ct = default)
    {
        var i = await db.Interviews.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        i.Status = AdminJobService.Parse<InterviewStatus>(status, "status");
        i.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { status = i.Status.ToString() };
    }

    public async Task<object> FeedbackAsync(int id, FeedbackInput input, CancellationToken ct = default)
    {
        var i = await db.Interviews.Include(x => x.Panelists).Include(x => x.Feedback).ThenInclude(f => f.Ratings)
                    .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        if (actor.Id is not { } staffId || i.Panelists.All(p => p.StaffUserId != staffId)) throw DomainException.Forbidden("not_on_panel");
        i.GiveFeedback(staffId, AdminJobService.Parse<Recommendation>(input.Recommendation, "recommendation"), input.Comments,
            (input.Ratings ?? []).Select(r => (r.Skill, (byte)Math.Clamp(r.Rating, 0, 9))), clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    // ---------------- offers ----------------

    public async Task<object> OffersAsync(string? status, CancellationToken ct = default)
    {
        var q = db.JobOffers.AsNoTracking().AsQueryable();
        if (Enum.TryParse<OfferStatus>(status, out var st)) q = q.Where(o => o.Status == st);
        var rows = await q.OrderByDescending(o => o.CreatedAt).Take(300).Select(o => new
        {
            id = o.Id, code = o.Code, position_title = o.PositionTitle, status = o.Status, offer_date = o.OfferDate,
            start_date = o.StartDate, monthly_salary = o.MonthlySalary, currency = o.Currency,
            applicant_id = o.JobApplicationId, applicant = o.JobApplication!.FullName, job_title = o.JobApplication.JobOpening!.Title,
        }).ToListAsync(ct);
        return new { offers = rows };
    }

    public async Task<object> OfferAsync(int id, CancellationToken ct = default)
    {
        var o = await db.JobOffers.AsNoTracking().Include(x => x.JobApplication!).ThenInclude(a => a.JobOpening)
                    .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        return new
        {
            id = o.Id, code = o.Code, position_title = o.PositionTitle, status = o.Status.ToString(), offer_date = o.OfferDate,
            start_date = o.StartDate, monthly_salary = o.MonthlySalary, currency = o.Currency, terms = o.Terms,
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
        o.OfferDate = AdminJobService.ParseDate(input.OfferDate) ?? clock.Today();
        o.StartDate = AdminJobService.ParseDate(input.StartDate);
        o.MonthlySalary = input.MonthlySalary;
        o.Currency = string.IsNullOrWhiteSpace(input.Currency) ? "AED" : TextUtil.Clip(input.Currency.Trim().ToUpperInvariant(), 3);
        o.Terms = TextUtil.Clip(input.Terms, 20000);
        o.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new { id = o.Id, code = o.Code };
    }

    public async Task<object> SetOfferStatusAsync(int id, string? status, CancellationToken ct = default)
    {
        var o = await db.JobOffers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        o.MoveTo(AdminJobService.Parse<OfferStatus>(status, "status"), clock.UtcNow);
        if (o.Status == OfferStatus.Accepted)
        {
            var app = await db.JobApplications.FirstAsync(a => a.Id == o.JobApplicationId, ct);
            app.Status = ApplicationStatus.Accepted;
            app.UpdatedAt = clock.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return new { status = o.Status.ToString() };
    }
}
