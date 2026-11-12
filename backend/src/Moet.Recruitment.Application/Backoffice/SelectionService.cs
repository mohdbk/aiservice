using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Assessments;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;

namespace Moet.Recruitment.Application.Backoffice;

/// <summary>
/// The combined selection score per applicant: CV, technical assessments, the interview panel's scorecards and the personality
/// fit with the job's role profile, weighted as set on the job (40/40/20 by default).
/// </summary>
public sealed class SelectionService(IAppDbContext db, IRequestLanguage language)
{
    sealed record Row(int Id, string Code, string Name, int JobId, ApplicationStatus Status, decimal? Cv, DateTime CreatedAt);

    /// <summary>Scores for the given applications (all of the same job or not).</summary>
    public async Task<Dictionary<int, (SelectionScore Score, RoleFit? Fit)>> ScoresAsync(IReadOnlyCollection<int> appIds, CancellationToken ct = default)
    {
        var result = new Dictionary<int, (SelectionScore, RoleFit?)>();
        if (appIds.Count == 0) return result;
        var apps = await db.JobApplications.AsNoTracking().Where(a => appIds.Contains(a.Id))
            .Select(a => new { a.Id, a.JobOpeningId, Cv = a.CvAnalysis == null ? null : a.CvAnalysis.Score }).ToListAsync(ct);
        var jobIds = apps.Select(a => a.JobOpeningId).Distinct().ToList();
        var jobs = await db.JobOpenings.AsNoTracking().Where(j => jobIds.Contains(j.Id)).ToDictionaryAsync(j => j.Id, ct);
        var invitations = await db.Invitations.AsNoTracking().Where(i => appIds.Contains(i.JobApplicationId))
            .Select(i => new
            {
                i.JobApplicationId, i.Score, i.Assessment!.PersonalityModel,
                Trait = i.Attempts.Where(t => t.TraitProfile != null).OrderByDescending(t => t.AttemptNo).Select(t => t.TraitProfile).FirstOrDefault(),
            }).ToListAsync(ct);
        var feedback = await db.Interviews.AsNoTracking().Where(i => appIds.Contains(i.JobApplicationId) && i.Status != InterviewStatus.Cancelled)
            .SelectMany(i => i.Feedback.Select(f => new { i.JobApplicationId, f.Recommendation, Ratings = f.Ratings.Select(r => r.Rating).ToList() }))
            .ToListAsync(ct);
        var profiles = jobs.Values.Any(j => j.RoleProfileId != null)
            ? await db.RoleProfiles.AsNoTracking().Where(r => r.IsActive).ToListAsync(ct) : [];
        foreach (var a in apps)
        {
            var job = jobs[a.JobOpeningId];
            var tests = Selection.TestsPercent(invitations.Where(i => i.JobApplicationId == a.Id && i.PersonalityModel == PersonalityModel.None).Select(i => i.Score));
            var interview = Selection.InterviewPercent(feedback.Where(f => f.JobApplicationId == a.Id).Select(f => (f.Recommendation, (IReadOnlyList<byte>)f.Ratings)));
            RoleFit? fit = null;
            if (job.RoleProfileId is { } rp && profiles.FirstOrDefault(p => p.Id == rp) is { } chosen)
            {
                foreach (var inv in invitations.Where(i => i.JobApplicationId == a.Id && i.PersonalityModel != PersonalityModel.None && i.Trait != null))
                {
                    var traits = JsonSerializer.Deserialize<List<TraitScore>>(inv.Trait!) ?? [];
                    // The job family's profile for this questionnaire: the chosen one, or its sibling with the same English name.
                    var profile = chosen.Fit(inv.PersonalityModel, traits) != null ? chosen
                        : profiles.FirstOrDefault(p => p.NameEn == chosen.NameEn && p.Fit(inv.PersonalityModel, traits) != null);
                    var f = profile?.Fit(inv.PersonalityModel, traits);
                    // With both questionnaires taken the Big Five fit is preferred: it is finer than the 4-letter match.
                    if (f != null && (fit == null || RoleProfile.IsBigFive(inv.PersonalityModel))) fit = f;
                }
            }
            result[a.Id] = (Selection.Combine(job.Weights, (double?)a.Cv, tests, interview, fit?.Percent), fit);
        }
        return result;
    }

    public static object View(SelectionScore s, RoleFit? fit) => new
    {
        total = s.Total, complete = s.Complete,
        parts = s.Parts.Select(p => new { key = p.Key, weight = p.Weight, score = p.Score }),
        fit = fit == null ? null : new { profile_id = fit.ProfileId, name_ar = fit.NameAr, name_en = fit.NameEn, percent = fit.Percent, level = fit.Level },
    };

    public async Task<object?> ForApplicantAsync(int appId, CancellationToken ct = default)
    {
        var all = await ScoresAsync([appId], ct);
        return all.TryGetValue(appId, out var s) ? View(s.Score, s.Fit) : null;
    }

    /// <summary>
    /// The job's applicants ranked by combined score (highest first; applicants without any score last). Rejected
    /// applicants are left out unless asked for.
    /// </summary>
    public async Task<object> RankingAsync(int jobId, ListQuery lq, bool all, CancellationToken ct = default)
    {
        var job = await db.JobOpenings.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw DomainException.NotFound();
        var q = db.JobApplications.AsNoTracking().Where(a => a.JobOpeningId == jobId);
        if (!all) q = q.Where(a => a.Status != ApplicationStatus.Rejected);
        if (lq.Term.Length > 0) q = q.Where(a => a.FullName.Contains(lq.Term) || a.Code.Contains(lq.Term) || a.Email.Contains(lq.Term));
        // Bounded: a job's applicant list is ranked in memory.
        var rows = await q.OrderBy(a => a.Id).Take(5000)
            .Select(a => new Row(a.Id, a.Code, a.FullName, a.JobOpeningId, a.Status, a.CvAnalysis == null ? null : a.CvAnalysis.Score, a.CreatedAt)).ToListAsync(ct);
        var scores = await ScoresAsync(rows.Select(r => r.Id).ToList(), ct);
        var ranked = rows.Select(r => (Row: r, S: scores[r.Id])).OrderByDescending(x => x.S.Score.Total ?? -1).ThenBy(x => x.Row.CreatedAt)
            .Select((x, i) => new RankRow(i + 1, x.Row.Id, x.Row.Code, x.Row.Name, x.Row.Status.ToString(), x.S.Score.Total, x.S.Score.Complete,
                Part(x.S.Score, Selection.Cv), Part(x.S.Score, Selection.Tests), Part(x.S.Score, Selection.Interview), Part(x.S.Score, Selection.Personality),
                x.S.Fit == null ? null : language.English ? x.S.Fit.NameEn : x.S.Fit.NameAr, x.S.Fit?.Level)).ToList();
        var sorts = new Dictionary<string, Func<RankRow, object?>>
        {
            ["rank"] = r => r.rank, ["name"] = r => r.full_name, ["total"] = r => r.total ?? -1, ["tests"] = r => r.tests ?? -1,
            ["interview"] = r => r.interview ?? -1, ["personality"] = r => r.personality ?? -1, ["cv"] = r => r.cv ?? -1, ["status"] = r => r.status,
        };
        var page = ranked.Page(lq, sorts, "rank");
        var w = job.Weights;
        return new
        {
            page.Items, page.Total, page.Page, page.Size, page.Pages, page.Sort, page.Dir,
            weights = new { cv = w.Cv, tests = w.Tests, interview = w.Interview, personality = w.Personality },
            role_profile_id = job.RoleProfileId,
        };
    }

    static double? Part(SelectionScore s, string key) => s.Parts.First(p => p.Key == key).Score;

#pragma warning disable IDE1006 // JSON names
    public sealed record RankRow(int rank, int id, string code, string full_name, string status, double? total, bool complete, double? cv, double? tests,
        double? interview, double? personality, string? role_profile, string? fit_level);
#pragma warning restore IDE1006
}
