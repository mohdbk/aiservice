using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;

namespace Moet.Recruitment.Application.Ai;

/// <summary>
/// Reads the CV, removes personal details, parses a structured profile, and scores it against criteria derived
/// once from the job description (so every applicant to the job is scored against the same list).
/// </summary>
public sealed class CvScoringHandler(IAppDbContext db, ILanguageModel model, ICvReader reader, IClock clock) : IJobHandler, IJobFailureHandler
{
    public string JobType => JobTypes.ScoreApplication;

    public async Task RunAsync(int applicationId, CancellationToken ct)
    {
        var app = await db.JobApplications.Include(a => a.CvAnalysis!).ThenInclude(c => c.CriterionScores)
                      .FirstOrDefaultAsync(a => a.Id == applicationId, ct) ?? throw new InvalidOperationException("Application not found");
        var c = app.CvAnalysis ??= new CvAnalysis { JobApplicationId = app.Id };
        c.Status = CvAnalysisStatus.Processing;
        c.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        if (!model.IsConfigured)
        {
            await FailedAsync(applicationId, "The language model is not configured.", ct);
            return;
        }
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == app.CvFileId, ct);
        var cv = await reader.ReadAsync(file.Content, Path.GetExtension(file.FileName), ct);
        if (cv.Text.Trim().Length < 50)
        {
            await FailedAsync(applicationId, "The CV could not be read (no text found).", ct);
            return;
        }
        var redacted = TextUtil.Clip(CvScoring.Redact(cv.Text, app.FullName), 40000);
        var job = await db.JobOpenings.Include(j => j.Criteria).FirstAsync(j => j.Id == app.JobOpeningId, ct);

        var profile = await model.CompleteJsonAsync(Prompts.Profile, $"<cv>\n{redacted}\n</cv>", Prompts.ProfileSchema, "cv_profile", ct);
        var work = Arr(profile, "work").Select(w => (Str(w, "startDate"), Str(w, "endDate"))).ToList();
        var years = CvScoring.ResolveExperience(CvScoring.YearsOfExperience(work, clock.Today()), Num(profile, "stated_years_of_experience"));

        // CVs are scored only against criteria HR has approved. Until then the CV details are read (so the candidate can check
        // them) and the application waits; approving the criteria scores every waiting CV.
        var criteria = await ApprovedCriteriaAsync(job, ct);
        if (criteria == null)
        {
            c.Status = CvAnalysisStatus.Waiting;
            c.YearsOfExperience = years;
            c.CvLanguage = TextUtil.Clip(Str(profile, "cv_language"), 10);
            c.ReadMethod = cv.Method;
            c.Model = TextUtil.Clip(model.ModelName, 100);
            c.ProfileJson = WithYears(profile, years);
            c.Error = "";
            c.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        var quality = CvScoring.ReadQuality(cv.Text, cv.Method);
        var list = string.Join("\n", criteria.Select((x, i) =>
            $"{i + 1}. [{(x.MustHave ? "ESSENTIAL" : "DESIRABLE")}] {x.Describe()}" + (x.Guide.Length > 0 ? $"\n   Score guide:\n   {x.Guide.Replace("\n", "\n   ")}" : "")));
        var corrections = await CorrectionsAsync(job.Id, ct);
        var user = $"<job>\nTitle: {job.Title}\n\n{TextUtil.Clip(TextUtil.StripHtml(job.Description), 12000)}\n</job>\n\n" +
                   $"Score these requirements, returning them in exactly this order:\n{list}\n\n" +
                   (corrections.Length > 0 ? $"<recruiter_corrections>\nRecruiters corrected earlier scores for this job. Follow the same judgement:\n{corrections}\n</recruiter_corrections>\n\n" : "") +
                   $"<structured_profile>\n{Brief(profile, years)}\n</structured_profile>\n" +
                   "The structured profile was extracted from the CV below; use both, and trust the CV text if they differ. Quotes must come from the CV text.\n\n" +
                   $"<cv>\n{redacted}\n</cv>";
        var result = await model.CompleteJsonAsync(Prompts.Score, user, Prompts.ScoreSchema, "cv_score", ct);
        var items = Items(result);
        var review = new List<string>();
        // Optionally score twice (CV_SCORE_TWICE=1): scores are averaged and a disagreement asks for a person to look.
        if (Environment.GetEnvironmentVariable("CV_SCORE_TWICE") is "1" or "true")
        {
            var second = await model.CompleteJsonAsync(Prompts.Score, user, Prompts.ScoreSchema, "cv_score", ct);
            var (merged, notes) = CvScoring.Merge(items, Items(second), criteria);
            items = merged.ToList();
            review.AddRange(notes);
        }
        var outcome = CvScoring.Evaluate(criteria, items, work, clock.Today(), redacted);
        review.AddRange(outcome.ReviewReasons);
        if (quality == "poor") review.Insert(0, "The CV text was hard to read (likely a scan): check the original CV.");
        // A recruiter's override for the same requirement is kept when the CV is scored again.
        var kept = c.CriterionScores.Where(x => x.OverrideScore != null).ToDictionary(x => x.Criterion, x => x);
        c.Status = CvAnalysisStatus.Scored;
        c.Summary = TextUtil.Clip(TextUtil.NoEmDash(Str(result, "summary")), 2000);
        c.SummaryArabic = TextUtil.Clip(TextUtil.NoEmDash(Str(result, "summary_ar")), 2000);
        c.MustHaveGaps = TextUtil.Clip(string.Join("; ", outcome.Gaps), 2000);
        c.YearsOfExperience = years;
        c.CvLanguage = TextUtil.Clip(Str(profile, "cv_language"), 10);
        c.ReadMethod = cv.Method;
        c.ReadQuality = quality;
        c.NeedsReview = review.Count > 0;
        c.ReviewReasons = TextUtil.Clip(TextUtil.NoEmDash(string.Join("\n", review.Distinct())), 1000);
        c.Model = TextUtil.Clip(model.ScoringModelName, 100);
        c.CriteriaVersion = job.CriteriaVersion;
        c.ProfileJson = WithYears(profile, years);
        c.Error = "";
        c.ScoredAt = clock.UtcNow;
        c.UpdatedAt = clock.UtcNow;
        c.CriterionScores.Clear();
        var n = 0;
        foreach (var row in outcome.Rows)
        {
            var text = TextUtil.Clip(row.Text, 300);
            var s = new CvCriterionScore
            {
                SortOrder = n++, Criterion = text, Weight = row.Weight, IsMustHave = row.MustHave, Category = row.Category,
                Score = (byte)row.Score, Evidence = TextUtil.Clip(TextUtil.NoEmDash(row.Evidence), 1000), Status = row.Status,
                Quote = TextUtil.Clip(row.Quote, 500), QuoteFound = row.QuoteFound, RelevantYears = row.RelevantYears,
                WhyNotHigher = TextUtil.Clip(TextUtil.NoEmDash(row.WhyNotHigher), 500),
            };
            if (kept.TryGetValue(text, out var o))
            {
                s.OverrideScore = o.OverrideScore; s.OverrideReason = o.OverrideReason; s.OverrideByStaffId = o.OverrideByStaffId; s.OverrideAt = o.OverrideAt;
            }
            c.CriterionScores.Add(s);
        }
        c.Score = CvScoring.Total(c.CriterionScores.Select(x => (x.Weight, (int)x.Effective)));
        await db.SaveChangesAsync(ct);
    }

    public async Task FailedAsync(int applicationId, string error, CancellationToken ct)
    {
        var c = await db.CvAnalyses.FirstOrDefaultAsync(x => x.JobApplicationId == applicationId, ct);
        if (c == null) return;
        c.Status = CvAnalysisStatus.Failed;
        c.Error = TextUtil.Clip(error, 500);
        c.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The job's approved criteria, or null while HR has not approved any. A job without criteria gets a suggestion from
    /// the description for HR to review (saved as a draft, not used until approved).
    /// </summary>
    async Task<List<CvScoring.Criterion>?> ApprovedCriteriaAsync(JobOpening job, CancellationToken ct)
    {
        if (job.CriteriaApprovedAt != null && job.Criteria.Count > 0)
            return job.Criteria.OrderBy(x => x.SortOrder).Select(x => new CvScoring.Criterion(x.Criterion, x.Weight, x.IsMustHave, x.Category,
                x.MinYears, x.Level, x.Guide)).ToList();
        if (job.Criteria.Count == 0)
        {
            try { await SuggestCriteriaAsync(model, db, job, ct); }
            catch (InvalidOperationException) { /* no suggestion: HR writes the criteria */ }
        }
        return null;
    }

    /// <summary>Replaces the job's draft criteria with a suggestion derived from the description (not approved).</summary>
    public static async Task<List<JobCriterion>> SuggestCriteriaAsync(ILanguageModel model, IAppDbContext db, JobOpening job, CancellationToken ct)
    {
        var version = job.DescriptionVersion();
        var output = await model.CompleteJsonAsync(Prompts.Criteria,
            $"<job>\nTitle: {job.Title}\n\n{TextUtil.Clip(TextUtil.StripHtml(job.Description), 12000)}\n</job>", Prompts.CriteriaSchema, "job_criteria", ct);
        var list = Arr(output, "criteria").Take(8).Select(e =>
        {
            var category = CvScoring.CategoryOf(Str(e, "category"));
            var essential = e.TryGetProperty("essential", out var m) && m.ValueKind == JsonValueKind.True;
            var high = e.TryGetProperty("high", out var h) && h.ValueKind == JsonValueKind.True;
            var years = (int)Num(e, "min_years");
            return new JobCriterion
            {
                Criterion = TextUtil.Clip(TextUtil.NoEmDash(Str(e, "requirement")).Trim(), 120), Category = category, IsMustHave = essential, IsHigh = high,
                Weight = CvScoring.WeightFor(essential, high), MinYears = category == "Experience" && years is > 0 and < 60 ? (byte)years : null,
                Level = category == "Language" ? TextUtil.Clip(Str(e, "level").Trim(), 40) : "", Guide = TextUtil.Clip(TextUtil.NoEmDash(Str(e, "guide")).Trim(), 2000),
            };
        }).Where(x => x.Criterion.Length > 0).ToList();
        if (list.Count == 0) throw new InvalidOperationException("No screening requirements could be derived from the job description.");
        job.Criteria.Clear();
        var n = 0;
        foreach (var x in list) { x.SortOrder = n++; job.Criteria.Add(x); }
        job.CriteriaVersion = version;
        job.CriteriaApprovedAt = null;
        job.CriteriaApprovedByStaffId = null;
        await db.SaveChangesAsync(ct);
        return list;
    }

    static List<CvScoring.ModelItem> Items(JsonElement result) => Arr(result, "criteria").Select(e => new CvScoring.ModelItem(
        (int)Num(e, "score"), TextUtil.NoEmDash(Str(e, "evidence")), Str(e, "quote"), Str(e, "status"),
        Arr(e, "relevant_roles").Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _)).Select(x => x.GetInt32()).ToList(),
        Str(e, "why_not_higher"))).ToList();

    /// <summary>Recruiters' recent corrections on this job (up to 8), given to the model as examples of the judgement wanted.</summary>
    async Task<string> CorrectionsAsync(int jobId, CancellationToken ct)
    {
        var rows = await db.CvAnalyses.AsNoTracking()
            .Where(a => db.JobApplications.Any(x => x.Id == a.JobApplicationId && x.JobOpeningId == jobId))
            .SelectMany(a => a.CriterionScores).Where(x => x.OverrideScore != null && x.OverrideReason != "")
            .OrderByDescending(x => x.OverrideAt).Take(8)
            .Select(x => new { x.Criterion, x.Score, x.OverrideScore, x.OverrideReason }).ToListAsync(ct);
        return string.Join("\n", rows.Select(x => $"- {x.Criterion}: the AI gave {x.Score}, a recruiter set {x.OverrideScore} because \"{x.OverrideReason}\""));
    }

    static string Brief(JsonElement p, decimal years)
    {
        var sb = new StringBuilder($"Total experience: {years.ToString(CultureInfo.InvariantCulture)} years\n");
        var k = 0;
        foreach (var w in Arr(p, "work"))
        {
            k++;
            var org = Str(w, "name") is { Length: > 0 } n ? n : Str(w, "description") is { Length: > 0 } d ? d : "unnamed employer";
            var when = Str(w, "startDate").Length > 0 ? $"{Str(w, "startDate")} to {(Str(w, "endDate") is { Length: > 0 } e ? e : "present")}" : "dates not given";
            sb.AppendLine($"- Role {k}: {Str(w, "position")} at {org} ({when}): {Str(w, "summary")}");
        }
        foreach (var e in Arr(p, "education"))
            sb.AppendLine($"- Education: {Str(e, "studyType")} {Str(e, "area")}, {Str(e, "institution")} ({Str(e, "endDate")})");
        var skills = Arr(p, "skills").SelectMany(s => new[] { Str(s, "name") }.Concat(Arr(s, "keywords").Select(k => k.GetString() ?? "")))
            .Where(x => x.Length > 0).Distinct().ToList();
        if (skills.Count > 0) sb.AppendLine("- Skills: " + string.Join(", ", skills));
        var certs = Arr(p, "certificates").Select(x => Str(x, "name")).Where(x => x.Length > 0).ToList();
        if (certs.Count > 0) sb.AppendLine("- Certificates: " + string.Join(", ", certs));
        return sb.ToString();
    }

    static string WithYears(JsonElement profile, decimal years)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(profile.GetRawText()) ?? [];
        var o = dict.ToDictionary(k => k.Key, k => (object)k.Value);
        o["years_of_experience"] = years;
        return JsonSerializer.Serialize(o);
    }

    internal static IEnumerable<JsonElement> Arr(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    internal static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    internal static decimal Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : 0;
}
