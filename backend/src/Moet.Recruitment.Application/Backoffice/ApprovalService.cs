using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record ApprovalSettingsInput(bool RequireJobApproval, bool RequireOfferApproval);
public sealed record DecisionInput(bool Approve, string? Comment);
#pragma warning disable IDE1006 // JSON names
public sealed record DecisionResult(int id, string status, string kind, int record_id, int? job_id);
#pragma warning restore IDE1006
public sealed record RequisitionInput(string? Title, string? TitleEn, int? DepartmentId, int? LocationId, string? EmploymentType, string? Grade,
    int Headcount, decimal? BudgetMonthly, string? Justification, string? NeededBy);

/// <summary>
/// The approvals inbox (publishing a job, sending an offer, opening a requisition) and requisitions. Recruitment admins
/// decide; a request from an admin needs no approval. Approving carries the step out in the same call.
/// </summary>
public sealed class ApprovalService(IAppDbContext db, ICurrentActor actor, ICodeGenerator codes, IClock clock, IRequestLanguage language)
{
    public async Task<object> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.PortalSettings.AsNoTracking().FirstAsync(ct);
        return new { require_job_approval = s.RequireJobApproval, require_offer_approval = s.RequireOfferApproval };
    }

    public async Task<object> SaveSettingsAsync(ApprovalSettingsInput input, CancellationToken ct = default)
    {
        var s = await db.PortalSettings.FirstAsync(ct);
        s.RequireJobApproval = input.RequireJobApproval;
        s.RequireOfferApproval = input.RequireOfferApproval;
        await db.SaveChangesAsync(ct);
        return await SettingsAsync(ct);
    }

    /// <summary>Whether this step needs an approval for the signed-in person (switched on, and they are not an admin).</summary>
    public async Task<bool> NeededAsync(ApprovalKind kind, CancellationToken ct = default)
    {
        if (actor.IsAdmin) return false;
        var s = await db.PortalSettings.AsNoTracking().FirstAsync(ct);
        return kind switch { ApprovalKind.JobPublish => s.RequireJobApproval, ApprovalKind.OfferSend => s.RequireOfferApproval, _ => true };
    }

    /// <summary>Adds a pending request (one per record and kind). Saved with the caller's changes.</summary>
    public async Task RequestAsync(ApprovalKind kind, int recordId, string title, string note, CancellationToken ct = default)
    {
        if (await db.ApprovalRequests.AnyAsync(a => a.Kind == kind && a.RecordId == recordId && a.Status == ApprovalStatus.Pending, ct)) return;
        var name = actor.Id is { } sid ? await db.StaffUsers.Where(u => u.Id == sid).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? actor.Name : actor.Name;
        db.ApprovalRequests.Add(new ApprovalRequest
        {
            Kind = kind, RecordId = recordId, Title = TextUtil.Clip(title, 300), Note = TextUtil.Clip(note, 2000), RequestedByStaffId = actor.Id ?? 0,
            RequestedByName = name, RequestedAt = clock.UtcNow,
        });
    }

    public async Task<object> ListAsync(ListQuery lq, string? status, CancellationToken ct = default)
    {
        var q = db.ApprovalRequests.AsNoTracking();
        if (!string.IsNullOrEmpty(status))
        {
            var st = BackofficeJobService.Parse<ApprovalStatus>(status, "status");
            q = q.Where(a => a.Status == st);
        }
        if (lq.Term.Length > 0) q = q.Where(a => a.Title.Contains(lq.Term) || a.RequestedByName.Contains(lq.Term));
        var sorts = new Sorts<ApprovalRequest>("requested").Add("requested", a => a.RequestedAt).Add("title", a => a.Title);
        var page = await q.PageAsync(lq, sorts, a => new
        {
            id = a.Id, kind = a.Kind.ToString(), record_id = a.RecordId, title = a.Title, note = a.Note, status = a.Status.ToString(),
            requested_by = a.RequestedByName, requested_at = a.RequestedAt, decided_by = a.DecidedByName, decided_at = a.DecidedAt, comment = a.Comment,
        }, ct);
        return new
        {
            page.Items, page.Total, page.Page, page.Size, page.Pages, page.Sort, page.Dir,
            pending = await db.ApprovalRequests.CountAsync(a => a.Status == ApprovalStatus.Pending, ct), can_decide = actor.IsAdmin,
        };
    }

    public async Task<DecisionResult> DecideAsync(int id, DecisionInput input, CancellationToken ct = default)
    {
        if (!actor.IsAdmin) throw DomainException.Forbidden();
        var a = await db.ApprovalRequests.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        if (a.RequestedByStaffId == actor.Id && input.Approve) throw DomainException.Conflict("own_request");
        var now = clock.UtcNow;
        a.Decide(input.Approve, actor.Id ?? 0, actor.Name, input.Comment, now);
        int? jobId = null;
        switch (a.Kind)
        {
            case ApprovalKind.JobPublish when input.Approve:
            {
                var j = await db.JobOpenings.FirstOrDefaultAsync(x => x.Id == a.RecordId, ct) ?? throw DomainException.NotFound();
                j.IsPublished = true;
                if (j.Status == JobStatus.Draft) j.Status = JobStatus.Open;
                j.PostedOn ??= clock.Today();
                j.UpdatedAt = now;
                j.UpdatedByStaffId = actor.Id;
                break;
            }
            case ApprovalKind.Requisition:
            {
                var r = await db.Requisitions.FirstOrDefaultAsync(x => x.Id == a.RecordId, ct) ?? throw DomainException.NotFound();
                r.Status = input.Approve ? RequisitionStatus.Approved : RequisitionStatus.Rejected;
                r.UpdatedAt = now;
                if (input.Approve) jobId = await DraftJobAsync(r, now, ct);
                break;
            }
        }
        await db.SaveChangesAsync(ct);
        return new DecisionResult(a.Id, a.Status.ToString(), a.Kind.ToString(), a.RecordId, jobId);
    }

    /// <summary>The draft job an approved requisition becomes (unpublished, for HR to finish and publish).</summary>
    async Task<int> DraftJobAsync(Requisition r, DateTime now, CancellationToken ct)
    {
        var loc = r.LocationId is { } lid ? await db.JobLocations.Include(l => l.Translations).FirstOrDefaultAsync(l => l.Id == lid, ct) : null;
        var j = new JobOpening
        {
            Code = await codes.NextAsync(CodeKind.JobOpening, ct), SourceLanguageCode = "ar", Title = r.Title, Description = "",
            DepartmentId = r.DepartmentId, LocationId = loc?.Id, Location = loc?.Name ?? "", EmploymentType = r.EmploymentType, Status = JobStatus.Draft,
            IsPublished = false, CreatedAt = now, UpdatedAt = now, CreatedByStaffId = actor.Id, UpdatedByStaffId = actor.Id,
        };
        if (r.TitleEn.Length > 0)
            j.Translations.Add(new JobOpeningTranslation
            {
                LanguageCode = "en", Title = r.TitleEn, Description = "", ReviewStatus = ReviewStatus.Approved, ApprovedByStaffId = actor.Id, ApprovedAt = now,
                Location = loc?.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name ?? "",
            });
        db.JobOpenings.Add(j);
        await db.SaveChangesAsync(ct);
        r.JobOpeningId = j.Id;
        return j.Id;
    }

    // Requisitions

    public async Task<object> RequisitionsAsync(ListQuery lq, string? status, CancellationToken ct = default)
    {
        var q = db.Requisitions.AsNoTracking();
        if (!string.IsNullOrEmpty(status))
        {
            var st = BackofficeJobService.Parse<RequisitionStatus>(status, "status");
            q = q.Where(r => r.Status == st);
        }
        if (lq.Term.Length > 0) q = q.Where(r => r.Title.Contains(lq.Term) || r.TitleEn.Contains(lq.Term) || r.Code.Contains(lq.Term));
        var en = language.English;
        var sorts = new Sorts<Requisition>("created").Add("created", r => r.CreatedAt).Add("title", r => r.Title).Add("needed", r => r.NeededBy);
        return await q.PageAsync(lq, sorts, r => new
        {
            id = r.Id, code = r.Code, title = en && r.TitleEn != "" ? r.TitleEn : r.Title, title_ar = r.Title, title_en = r.TitleEn,
            department_id = r.DepartmentId,
            department = db.Departments.Where(d => d.Id == r.DepartmentId)
                .Select(d => en ? d.Translations.Where(t => t.LanguageCode == "en" && t.Name != "").Select(t => t.Name).FirstOrDefault() ?? d.Name : d.Name).FirstOrDefault(),
            location_id = r.LocationId, employment_type = r.EmploymentType.ToString(), grade = r.Grade, headcount = r.Headcount,
            budget_monthly = r.BudgetMonthly, justification = r.Justification, needed_by = r.NeededBy, status = r.Status.ToString(),
            job_id = r.JobOpeningId, requested_by = r.RequestedByName, created_at = r.CreatedAt,
        }, ct);
    }

    public async Task<object> SaveRequisitionAsync(int? id, RequisitionInput input, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        Requisition r;
        if (id == null)
        {
            var name = actor.Id is { } sid ? await db.StaffUsers.Where(u => u.Id == sid).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? actor.Name : actor.Name;
            r = new Requisition { Code = await codes.NextAsync(CodeKind.Requisition, ct), RequestedByStaffId = actor.Id ?? 0, RequestedByName = name, CreatedAt = now };
            db.Requisitions.Add(r);
        }
        else
        {
            r = await db.Requisitions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
            if (r.Status != RequisitionStatus.Pending) throw DomainException.Conflict("requisition_locked");
            if (r.RequestedByStaffId != actor.Id && !actor.IsAdmin) throw DomainException.Forbidden();
        }
        r.Title = TextUtil.NoEmDash(TextUtil.Clip(input.Title?.Trim(), 140));
        r.TitleEn = TextUtil.NoEmDash(TextUtil.Clip(input.TitleEn?.Trim(), 140));
        if (input.DepartmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d, ct)) throw DomainException.Invalid("invalid_value", new Dictionary<string, object?> { ["field"] = "department_id" });
        if (input.LocationId is { } l && !await db.JobLocations.AnyAsync(x => x.Id == l, ct)) throw DomainException.Invalid("invalid_value", new Dictionary<string, object?> { ["field"] = "location_id" });
        r.DepartmentId = input.DepartmentId;
        r.LocationId = input.LocationId;
        r.EmploymentType = BackofficeJobService.Parse<EmploymentType>(input.EmploymentType ?? "FullTime", "employment_type");
        r.Grade = TextUtil.Clip(input.Grade?.Trim(), 50);
        r.Headcount = input.Headcount;
        r.BudgetMonthly = input.BudgetMonthly is < 0 ? throw DomainException.Invalid("invalid_value", new Dictionary<string, object?> { ["field"] = "budget_monthly" }) : input.BudgetMonthly;
        r.Justification = TextUtil.NoEmDash(TextUtil.Clip(input.Justification?.Trim(), 4000));
        r.NeededBy = BackofficeJobService.ParseDate(input.NeededBy);
        r.UpdatedAt = now;
        r.Validate();
        await db.SaveChangesAsync(ct);
        if (id == null)
        {
            await RequestAsync(ApprovalKind.Requisition, r.Id, $"{r.Code} {r.Title}", $"{r.Headcount} x {r.Title}. {r.Justification}", ct);
            await db.SaveChangesAsync(ct);
        }
        return new { id = r.Id, code = r.Code };
    }

    public async Task<object> CancelRequisitionAsync(int id, CancellationToken ct = default)
    {
        var r = await db.Requisitions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        if (r.Status != RequisitionStatus.Pending) throw DomainException.Conflict("requisition_locked");
        if (r.RequestedByStaffId != actor.Id && !actor.IsAdmin) throw DomainException.Forbidden();
        r.Status = RequisitionStatus.Cancelled;
        r.UpdatedAt = clock.UtcNow;
        foreach (var a in await db.ApprovalRequests.Where(a => a.Kind == ApprovalKind.Requisition && a.RecordId == id && a.Status == ApprovalStatus.Pending).ToListAsync(ct))
            a.Status = ApprovalStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return new { id, status = r.Status.ToString() };
    }
}
