using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Ai;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;

namespace Moet.Recruitment.Application.Backoffice;

#pragma warning disable IDE1006 // JSON names
public sealed record CriterionInput(string? criterion, decimal weight, bool must);
#pragma warning restore IDE1006
public sealed record CriteriaInput(IReadOnlyList<CriterionInput>? Criteria, bool Approve);

/// <summary>
/// The job's CV screening criteria, owned by HR: suggested from the description by the AI, edited (text, weight,
/// must-have) and approved by people. CVs are scored only against approved criteria; approving scores the CVs that were
/// waiting, and HR can score every CV again after a change.
/// </summary>
public sealed class CriteriaService(IAppDbContext db, ILanguageModel model, JobQueue jobs, ICurrentActor actor, IClock clock)
{
    public const int Max = 10;

    public async Task<object> GetAsync(int jobId, CancellationToken ct = default)
    {
        var j = await db.JobOpenings.AsNoTracking().Include(x => x.Criteria).FirstOrDefaultAsync(x => x.Id == jobId, ct) ?? throw DomainException.NotFound();
        var approver = j.CriteriaApprovedByStaffId is { } sid ? await db.StaffUsers.Where(u => u.Id == sid).Select(u => u.FullName).FirstOrDefaultAsync(ct) : null;
        var counts = await db.CvAnalyses.AsNoTracking().Where(c => db.JobApplications.Any(a => a.Id == c.JobApplicationId && a.JobOpeningId == jobId)).GroupBy(c => c.Status)
            .Select(g => new { g.Key, n = g.Count() }).ToListAsync(ct);
        return new
        {
            criteria = j.Criteria.OrderBy(c => c.SortOrder).Select(c => new { criterion = c.Criterion, weight = c.Weight, must = c.IsMustHave }),
            approved = j.CriteriaApprovedAt != null, approved_at = j.CriteriaApprovedAt, approved_by = approver,
            // The description changed since the criteria were made or edited: HR may want to review them.
            description_changed = j.Criteria.Count > 0 && j.CriteriaVersion != j.DescriptionVersion(),
            waiting = counts.Where(c => c.Key == CvAnalysisStatus.Waiting).Sum(c => c.n),
            scored = counts.Where(c => c.Key == CvAnalysisStatus.Scored).Sum(c => c.n),
            in_progress = counts.Where(c => c.Key is CvAnalysisStatus.Pending or CvAnalysisStatus.Processing).Sum(c => c.n),
            ai_available = model.IsConfigured,
        };
    }

    /// <summary>A fresh suggestion from the description (a draft for HR; not used until approved).</summary>
    public async Task<object> SuggestAsync(int jobId, CancellationToken ct = default)
    {
        if (!model.IsConfigured) throw DomainException.Conflict("ai_not_configured");
        var j = await db.JobOpenings.Include(x => x.Criteria).FirstOrDefaultAsync(x => x.Id == jobId, ct) ?? throw DomainException.NotFound();
        if (TextUtil.StripHtml(j.Description).Trim().Length < 20) throw DomainException.Invalid("description_too_short");
        try { await CvScoringHandler.SuggestCriteriaAsync(model, db, j, ct); }
        catch (InvalidOperationException) { throw DomainException.Conflict("criteria_not_suggested"); }
        return await GetAsync(jobId, ct);
    }

    public async Task<object> SaveAsync(int jobId, CriteriaInput input, CancellationToken ct = default)
    {
        var j = await db.JobOpenings.Include(x => x.Criteria).FirstOrDefaultAsync(x => x.Id == jobId, ct) ?? throw DomainException.NotFound();
        var items = (input.Criteria ?? []).Select(c => new CriterionInput(TextUtil.NoEmDash(TextUtil.Clip(c.criterion?.Trim(), 300)), c.weight, c.must))
            .Where(c => c.criterion!.Length > 0).ToList();
        if (items.Count == 0) throw DomainException.Invalid("criteria_required");
        if (items.Count > Max) throw DomainException.Invalid("criteria_too_many", new Dictionary<string, object?> { ["max"] = Max });
        if (items.Any(c => c.weight is <= 0 or > 100)) throw DomainException.Invalid("criteria_weight");
        var unchanged = j.Criteria.Count == items.Count && j.Criteria.OrderBy(c => c.SortOrder).Zip(items)
            .All(p => p.First.Criterion == p.Second.criterion && p.First.Weight == p.Second.weight && p.First.IsMustHave == p.Second.must);
        if (!unchanged)
        {
            j.Criteria.Clear();
            var n = 0;
            foreach (var c in items) j.Criteria.Add(new JobCriterion { SortOrder = n++, Criterion = c.criterion!, Weight = c.weight, IsMustHave = c.must });
            j.CriteriaVersion = j.DescriptionVersion();
        }
        var now = clock.UtcNow;
        var queued = 0;
        if (input.Approve)
        {
            j.CriteriaApprovedAt = now;
            j.CriteriaApprovedByStaffId = actor.Id;
            // The CVs that were waiting for criteria are scored now.
            var waiting = await db.CvAnalyses.Where(c => c.Status == CvAnalysisStatus.Waiting && db.JobApplications.Any(a => a.Id == c.JobApplicationId && a.JobOpeningId == jobId)).ToListAsync(ct);
            foreach (var c in waiting) { c.Status = CvAnalysisStatus.Pending; c.UpdatedAt = now; }
            queued = await jobs.EnqueueManyAsync(JobTypes.ScoreApplication, waiting.Select(c => c.JobApplicationId).ToList(), ct);
        }
        else if (!unchanged)
        {
            // Edited without approving: back to draft (CVs scored from now on wait for the approval).
            j.CriteriaApprovedAt = null;
            j.CriteriaApprovedByStaffId = null;
        }
        j.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new { queued, approved = j.CriteriaApprovedAt != null };
    }

    /// <summary>Scores every CV of the job again against the approved criteria (after they changed).</summary>
    public async Task<object> ScoreAgainAsync(int jobId, CancellationToken ct = default)
    {
        var j = await db.JobOpenings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == jobId, ct) ?? throw DomainException.NotFound();
        if (j.CriteriaApprovedAt == null) throw DomainException.Conflict("criteria_not_approved");
        var now = clock.UtcNow;
        var apps = await db.JobApplications.Where(a => a.JobOpeningId == jobId && a.AnonymisedAt == null).Include(a => a.CvAnalysis).ToListAsync(ct);
        foreach (var a in apps)
        {
            a.CvAnalysis ??= new CvAnalysis { JobApplicationId = a.Id };
            if (a.CvAnalysis.Status == CvAnalysisStatus.Processing) continue;
            a.CvAnalysis.Status = CvAnalysisStatus.Pending;
            a.CvAnalysis.Error = "";
            a.CvAnalysis.UpdatedAt = now;
        }
        var queued = await jobs.EnqueueManyAsync(JobTypes.ScoreApplication, apps.Select(a => a.Id).ToList(), ct);
        await db.SaveChangesAsync(ct);
        return new { queued };
    }
}
