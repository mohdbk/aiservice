using Microsoft.EntityFrameworkCore;
using Moet.Hrms.Application.Ai;
using Moet.Hrms.Application.Common;
using Moet.Hrms.Domain;
using Moet.Hrms.Domain.Common;
using Moet.Hrms.Domain.Hiring;

namespace Moet.Hrms.Application.Backoffice;

#pragma warning disable IDE1006 // JSON names
public sealed record CriterionInput(string? criterion, decimal weight, bool must, string? category = null, int? min_years = null, string? level = null,
    bool? high = null, string? guide = null);
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
            criteria = j.Criteria.OrderBy(c => c.SortOrder).Select(c => new
            {
                criterion = c.Criterion, weight = c.Weight, must = c.IsMustHave, category = c.Category, min_years = (int?)c.MinYears, level = c.Level,
                high = c.IsHigh, guide = c.Guide,
                share = Math.Round(c.Weight / Math.Max(1m, j.Criteria.Sum(x => x.Weight)) * 100),
            }),
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
        // Structured requirements (with a type) get their weight from Essential or Desirable and Standard or High importance;
        // older callers that send a weight keep it.
        var items = (input.Criteria ?? []).Select(c =>
        {
            var structured = c.category != null;
            var category = CvScoring.CategoryOf(c.category);
            var high = c.high ?? false;
            return new JobCriterion
            {
                Criterion = TextUtil.NoEmDash(TextUtil.Clip(c.criterion?.Trim(), structured ? 120 : 300)), IsMustHave = c.must, Category = category, IsHigh = high,
                Weight = structured ? CvScoring.WeightFor(c.must, high) : c.weight,
                MinYears = category == "Experience" && c.min_years is > 0 and < 60 ? (byte)c.min_years : null,
                Level = category == "Language" ? TextUtil.Clip((c.level ?? "").Trim(), 40) : "",
                Guide = TextUtil.NoEmDash(TextUtil.Clip((c.guide ?? "").Trim(), 2000)),
            };
        }).Where(c => c.Criterion.Length > 0).ToList();
        if (items.Count == 0) throw DomainException.Invalid("criteria_required");
        if (items.Count > Max) throw DomainException.Invalid("criteria_too_many", new Dictionary<string, object?> { ["max"] = Max });
        if (items.Any(c => c.Weight is <= 0 or > 100)) throw DomainException.Invalid("criteria_weight");
        var unchanged = j.Criteria.Count == items.Count && j.Criteria.OrderBy(c => c.SortOrder).Zip(items)
            .All(p => p.First.Criterion == p.Second.Criterion && p.First.Weight == p.Second.Weight && p.First.IsMustHave == p.Second.IsMustHave
                && p.First.Category == p.Second.Category && p.First.MinYears == p.Second.MinYears && p.First.Level == p.Second.Level
                && p.First.IsHigh == p.Second.IsHigh && p.First.Guide == p.Second.Guide);
        if (!unchanged)
        {
            j.Criteria.Clear();
            var n = 0;
            foreach (var c in items) { c.SortOrder = n++; j.Criteria.Add(c); }
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

    /// <summary>Drafts a score guide (what 0, 4, 7 and 10 look like) for each requirement given, for HR to review.</summary>
    public async Task<object> GuidesAsync(int jobId, CriteriaInput input, CancellationToken ct = default)
    {
        if (!model.IsConfigured) throw DomainException.Conflict("ai_not_configured");
        var j = await db.JobOpenings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == jobId, ct) ?? throw DomainException.NotFound();
        var rows = (input.Criteria ?? []).Where(c => !string.IsNullOrWhiteSpace(c.criterion)).Take(Max).ToList();
        if (rows.Count == 0) throw DomainException.Invalid("criteria_required");
        var list = string.Join("\n", rows.Select((c, i) => $"{i + 1}. [{(c.must ? "Essential" : "Desirable")}] " +
            new CvScoring.Criterion(c.criterion!.Trim(), 1, c.must, CvScoring.CategoryOf(c.category), c.min_years, c.level ?? "").Describe()));
        var output = await model.CompleteJsonAsync(Prompts.Guides, $"<job>\nTitle: {j.Title}\n\n{TextUtil.Clip(TextUtil.StripHtml(j.Description), 6000)}\n</job>\n\nRequirements:\n{list}",
            Prompts.GuidesSchema, "criteria_guides", ct);
        var guides = CvScoringHandler.Arr(output, "guides").Select(g => TextUtil.NoEmDash(g.GetString() ?? "").Trim()).ToList();
        return new { guides = rows.Select((_, i) => i < guides.Count ? TextUtil.Clip(guides[i], 2000) : "").ToList() };
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
