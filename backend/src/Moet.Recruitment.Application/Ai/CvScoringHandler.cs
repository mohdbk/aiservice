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
        var criteria = await EnsureCriteriaAsync(job, ct);

        var profile = await model.CompleteJsonAsync(Prompts.Profile, $"<cv>\n{redacted}\n</cv>", Prompts.ProfileSchema, "cv_profile", ct);
        var work = Arr(profile, "work").Select(w => (Str(w, "startDate"), Str(w, "endDate"))).ToList();
        var years = CvScoring.ResolveExperience(CvScoring.YearsOfExperience(work, clock.Today()), Num(profile, "stated_years_of_experience"));

        var list = string.Join("\n", criteria.Select((x, i) => $"{i + 1}. {x.Text} (weight {x.Weight.ToString("0.##", CultureInfo.InvariantCulture)}{(x.MustHave ? ", MUST-HAVE" : "")})"));
        var user = $"<job>\nTitle: {job.Title}\n\n{TextUtil.Clip(TextUtil.StripHtml(job.Description), 12000)}\n</job>\n\n" +
                   $"Score these criteria, returning them in exactly this order:\n{list}\n\n" +
                   $"<structured_profile>\n{Brief(profile, years)}\n</structured_profile>\n" +
                   "The structured profile was extracted from the CV below; use both, and trust the CV text if they differ.\n\n" +
                   $"<cv>\n{redacted}\n</cv>";
        var result = await model.CompleteJsonAsync(Prompts.Score, user, Prompts.ScoreSchema, "cv_score", ct);
        var items = Arr(result, "criteria").Select(e => ((int)Num(e, "score"), TextUtil.NoEmDash(Str(e, "evidence")))).ToList();
        var (rows, total, gaps) = CvScoring.Combine(criteria, items);

        c.Status = CvAnalysisStatus.Scored;
        c.Score = total;
        c.Summary = TextUtil.Clip(TextUtil.NoEmDash(Str(result, "summary")), 2000);
        c.SummaryArabic = TextUtil.Clip(TextUtil.NoEmDash(Str(result, "summary_ar")), 2000);
        c.MustHaveGaps = TextUtil.Clip(string.Join("; ", gaps), 2000);
        c.YearsOfExperience = years;
        c.CvLanguage = TextUtil.Clip(Str(profile, "cv_language"), 10);
        c.ReadMethod = cv.Method;
        c.Model = TextUtil.Clip(model.ModelName, 100);
        c.CriteriaVersion = job.CriteriaVersion;
        c.ProfileJson = WithYears(profile, years);
        c.Error = "";
        c.ScoredAt = clock.UtcNow;
        c.UpdatedAt = clock.UtcNow;
        c.CriterionScores.Clear();
        var n = 0;
        foreach (var r in rows)
            c.CriterionScores.Add(new CvCriterionScore
            {
                SortOrder = n++, Criterion = TextUtil.Clip(r.Text, 300), Weight = r.Weight, IsMustHave = r.MustHave,
                Score = (byte)r.Score, Evidence = TextUtil.Clip(r.Evidence, 1000),
            });
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

    /// <summary>The job's criteria, derived again from the description when it changed since they were made.</summary>
    async Task<List<CvScoring.Criterion>> EnsureCriteriaAsync(JobOpening job, CancellationToken ct)
    {
        var version = job.DescriptionVersion();
        if (job.CriteriaVersion == version && job.Criteria.Count > 0)
            return job.Criteria.OrderBy(x => x.SortOrder).Select(x => new CvScoring.Criterion(x.Criterion, x.Weight, x.IsMustHave)).ToList();
        var output = await model.CompleteJsonAsync(Prompts.Criteria,
            $"<job>\nTitle: {job.Title}\n\n{TextUtil.Clip(TextUtil.StripHtml(job.Description), 12000)}\n</job>", Prompts.CriteriaSchema, "job_criteria", ct);
        var list = Arr(output, "criteria").Take(6)
            .Select(e => new CvScoring.Criterion(TextUtil.Clip(TextUtil.NoEmDash(Str(e, "criterion")).Trim(), 300), Num(e, "weight"),
                e.TryGetProperty("must", out var m) && m.ValueKind == JsonValueKind.True))
            .Where(x => x.Text.Length > 0 && x.Weight > 0).ToList();
        if (list.Count == 0) throw new InvalidOperationException("No screening criteria could be derived from the job description.");
        job.Criteria.Clear();
        var n = 0;
        foreach (var x in list) job.Criteria.Add(new JobCriterion { SortOrder = n++, Criterion = x.Text, Weight = x.Weight, IsMustHave = x.MustHave });
        job.CriteriaVersion = version;
        await db.SaveChangesAsync(ct);
        return list;
    }

    static string Brief(JsonElement p, decimal years)
    {
        var sb = new StringBuilder($"Total experience: {years.ToString(CultureInfo.InvariantCulture)} years\n");
        foreach (var w in Arr(p, "work"))
        {
            var org = Str(w, "name") is { Length: > 0 } n ? n : Str(w, "description") is { Length: > 0 } d ? d : "unnamed employer";
            var when = Str(w, "startDate").Length > 0 ? $"{Str(w, "startDate")} to {(Str(w, "endDate") is { Length: > 0 } e ? e : "present")}" : "dates not given";
            sb.AppendLine($"- {Str(w, "position")} at {org} ({when}): {Str(w, "summary")}");
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
