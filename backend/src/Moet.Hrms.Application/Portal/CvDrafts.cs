using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Moet.Hrms.Application.Ai;
using Moet.Hrms.Application.Common;
using Moet.Hrms.Domain;
using Moet.Hrms.Domain.Common;
using Moet.Hrms.Domain.Hiring;
using Moet.Hrms.Domain.Operations;

namespace Moet.Hrms.Application.Portal;

/// <summary>
/// A CV given on the sign-up page before the account exists. It is checked like any CV, read in the background, and its
/// details (name, email, phone) fill in the sign-up form. When the person signs up with it, it becomes the default CV
/// in their library (with what was read); otherwise it is deleted with its file a day later.
/// </summary>
public sealed class CvDraftService(IAppDbContext db, IClock clock, ContentService content, IVirusScanner scanner, JobQueue jobs)
{
    public async Task<object> AddAsync(string? fileName, byte[]? data, CancellationToken ct = default)
    {
        if (data == null || data.Length == 0) throw DomainException.Invalid("cv_required");
        var s = await content.SettingsAsync(ct);
        if (!s.AllowSignup) throw DomainException.Forbidden("signup_closed");
        var ext = CvFile.Validate(fileName ?? "", data, s.MaxCvMb);
        await scanner.CheckAsync(data, ct);
        var now = clock.UtcNow;
        var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var name = TextUtil.Clip(Path.GetFileName(fileName ?? $"cv.{ext}"), 200);
        var file = new StoredFile { Id = Guid.NewGuid(), FileName = name, ContentType = CvFile.ContentTypes[ext], SizeBytes = data.Length, Sha256 = sha, Content = data, CreatedAt = now };
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync(ct);
        var draft = new CvDraft
        {
            Token = Guid.NewGuid(), StoredFileId = file.Id, FileName = name, SizeBytes = data.Length, Sha256 = sha, Status = "Pending",
            CreatedAt = now, ExpiresAt = now + CvDraft.Lifetime,
        };
        db.CvDrafts.Add(draft);
        await db.SaveChangesAsync(ct);
        await jobs.EnqueueAsync(JobTypes.ReadCvDraft, draft.Id, ct);
        await db.SaveChangesAsync(ct);
        return new { token = draft.Token };
    }

    /// <summary>What the sign-up form can take from the draft: "pending" while it is read.</summary>
    public async Task<object> StatusAsync(string? token, CancellationToken ct = default)
    {
        var d = await FindAsync(token, ct) ?? throw DomainException.NotFound("cv_not_found");
        return new
        {
            status = d.Status == "Pending" ? "pending" : d.Status == "Unreadable" ? "unreadable" : "ready",
            full_name = d.FullName, email = d.Email, phone = d.Phone, file_name = d.FileName,
        };
    }

    async Task<CvDraft?> FindAsync(string? token, CancellationToken ct) =>
        Guid.TryParse(token, out var t) ? await db.CvDrafts.FirstOrDefaultAsync(d => d.Token == t && d.ExpiresAt > clock.UtcNow, ct) : null;

    /// <summary>Moves a draft into a new account's CV library as its default CV, with what was read. Unknown or expired: nothing.</summary>
    internal async Task AdoptAsync(string? token, int accountId, CancellationToken ct)
    {
        var d = await FindAsync(token, ct);
        if (d == null) return;
        var cv = new CandidateCv
        {
            ApplicantAccountId = accountId, StoredFileId = d.StoredFileId, FileName = d.FileName, SizeBytes = d.SizeBytes, Sha256 = d.Sha256,
            IsDefault = true, Status = d.Status, Phone = d.Phone, YearsOfExperience = d.YearsOfExperience, Summary = d.Summary,
            ProfileJson = d.ProfileJson, Text = d.Text, ReadMethod = d.ReadMethod, UploadedAt = d.CreatedAt, ReadAt = d.ReadAt,
        };
        db.CandidateCvs.Add(cv);
        db.CvDrafts.Remove(d);
        await db.SaveChangesAsync(ct);
        if (cv.Status == "Pending")
        {
            await jobs.EnqueueAsync(JobTypes.ReadCandidateCv, cv.Id, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Deletes drafts nobody signed up with, and their files. Returns how many.</summary>
    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var old = await db.CvDrafts.Where(d => d.ExpiresAt <= now).Select(d => new { d.Id, d.StoredFileId }).Take(500).ToListAsync(ct);
        if (old.Count == 0) return 0;
        var ids = old.Select(o => o.Id).ToList();
        var files = old.Select(o => o.StoredFileId).ToList();
        await db.CvDrafts.Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync(ct);
        await db.StoredFiles.Where(f => files.Contains(f.Id)).ExecuteDeleteAsync(ct);
        return old.Count;
    }
}

public sealed class CvDraftReadHandler(IAppDbContext db, ILanguageModel model, ICvReader reader, IClock clock) : IJobHandler
{
    public string JobType => JobTypes.ReadCvDraft;

    public async Task RunAsync(int draftId, CancellationToken ct)
    {
        var d = await db.CvDrafts.FirstOrDefaultAsync(x => x.Id == draftId, ct);
        if (d == null) return;
        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == d.StoredFileId, ct);
        var r = await CvReading.ReadAsync(file?.Content, d.FileName, reader, model, clock, ct);
        d.ReadAt = clock.UtcNow;
        (d.Status, d.Text, d.ReadMethod, d.Phone, d.Email, d.FullName, d.YearsOfExperience, d.Summary, d.ProfileJson) =
            (r.Status, r.Text, r.Method, r.Phone, r.Email, r.FullName, r.Years, r.Summary, r.ProfileJson);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Suggested answers to a job's screening questions from the candidate's chosen CV, for them to check on the application
/// form. Questions about eligibility and personal circumstances (nationality, age, gender, salary, declarations and the
/// like) are never suggested, whatever the model returns. Without a language model: no suggestions.
/// </summary>
public sealed partial class ScreeningSuggestionService(IAppDbContext db, ILanguageModel model)
{
    [GeneratedRegex(@"nationalit|citizen|resident|residence|visa|passport|\bage\b|\bhow old\b|birth|gender|\bsex\b|married|marital|religio|health|disab|salar|\bpay\b|wage|notice period|availab|willing|relocat|consent|declar|\bagree|criminal|"
        + "جنسي|مواطن|إقامة|اقامة|تأشيرة|تاشيرة|جواز|العمر|عمرك|ميلاد|الجنس|متزوج|الزواج|الحالة الاجتماعية|الدين|ديانة|الصحة|صحية|إعاقة|اعاقة|راتب|الأجر|الإشعار|الاشعار|متاح|مستعد|استعداد|موافق|أوافق|إقرار|اقرار|أقر|الانتقال", RegexOptions.IgnoreCase)]
    private static partial Regex Personal();

    public static bool Allowed(ScreeningQuestion q) => !Personal().IsMatch(q.Text) && !Personal().IsMatch(q.Category)
        && !q.Translations.Any(t => Personal().IsMatch(t.Text));

    public async Task<object> SuggestAsync(int accountId, string? jobCode, int cvId, CancellationToken ct = default)
    {
        var empty = new { answers = new Dictionary<string, object>(), quotes = new Dictionary<string, string>() };
        if (!model.IsConfigured) return empty;
        var cv = await db.CandidateCvs.FirstOrDefaultAsync(c => c.ApplicantAccountId == accountId && c.Id == cvId, ct)
            ?? throw DomainException.NotFound("cv_not_found");
        if (cv.Text.Trim().Length < 50) return empty;
        var job = await db.JobOpenings.AsNoTracking()
            .Include(j => j.ScreeningQuestions).ThenInclude(s => s.ScreeningQuestion!).ThenInclude(q => q.Options)
            .Include(j => j.ScreeningQuestions).ThenInclude(s => s.ScreeningQuestion!).ThenInclude(q => q.Translations)
            .AsSplitQuery().FirstOrDefaultAsync(j => j.Code == jobCode && j.IsPublished, ct) ?? throw DomainException.NotFound("job_not_found");
        var asked = PortalSiteService.Asked(job).Where(a => Allowed(a.Question)).ToList();
        if (asked.Count == 0) return empty;

        // Worked out once per CV and job (and the job's questions as they are now): reopening the form asks the model again
        // only if HR changed the questions.
        var signature = jobCode + ":" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("|",
            asked.Select(x => $"{x.Key}/{x.Question.Id}/{x.Question.AnswerType}/{x.Question.Text}/{string.Join(",", x.Question.Options.Select(o => o.Id))}")))))[..16];
        var kept = Kept(cv.ScreeningSuggestionsJson);
        if (kept[signature] is JsonObject hit) return JsonSerializer.Deserialize<JsonElement>(hit.ToJsonString());

        var questions = asked.Select(a => new
        {
            key = a.Key, text = a.Question.Text, type = Labels.Wire(a.Question.AnswerType),
            options = a.Question.AnswerType == AnswerType.YesNo ? new[] { new { value = "Yes", label = "Yes" }, new { value = "No", label = "No" } }
                : a.Question.Options.OrderBy(o => o.SortOrder).Select(o => new { value = o.Id.ToString(CultureInfo.InvariantCulture), label = o.Text }).ToArray(),
        });
        JsonElement r;
        try
        {
            r = await model.CompleteJsonAsync(Prompts.ScreeningAnswers,
                $"<questions>\n{JsonSerializer.Serialize(questions)}\n</questions>\n<cv>\n{TextUtil.Clip(cv.Text, 30000)}\n</cv>",
                Prompts.ScreeningAnswersSchema, "screening_answers", ct);
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
        {
            return empty;
        }

        var answers = new Dictionary<string, object>();
        var quotes = new Dictionary<string, string>();
        foreach (var a in CvScoringHandler.Arr(r, "answers"))
        {
            var key = CvScoringHandler.Str(a, "key");
            var q = asked.FirstOrDefault(x => x.Key == key)?.Question;
            if (q == null || answers.ContainsKey(key)) continue;
            var value = Check(q, CvScoringHandler.Str(a, "answer").Trim(), CvScoringHandler.Arr(a, "answers").Select(x => x.GetString() ?? "").ToList());
            if (value == null) continue;
            answers[key] = value;
            quotes[key] = TextUtil.Clip(TextUtil.NoEmDash(CvScoringHandler.Str(a, "quote")), 300);
        }
        var found = new { answers, quotes };
        kept[signature] = JsonSerializer.SerializeToNode(found);
        while (kept.Count > 20) kept.Remove(kept.First().Key);
        cv.ScreeningSuggestionsJson = kept.ToJsonString();
        await db.SaveChangesAsync(ct);
        return found;
    }

    static JsonObject Kept(string? json)
    {
        try { return JsonNode.Parse(json ?? "{}") as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    /// <summary>The answer in the form the application form takes, or null when it does not fit the question.</summary>
    internal static object? Check(ScreeningQuestion q, string answer, List<string> many)
    {
        var ids = q.Options.Select(o => o.Id.ToString(CultureInfo.InvariantCulture)).ToHashSet();
        switch (q.AnswerType)
        {
            case AnswerType.YesNo:
                return answer.Equals("Yes", StringComparison.OrdinalIgnoreCase) ? "Yes" : answer.Equals("No", StringComparison.OrdinalIgnoreCase) ? "No" : null;
            case AnswerType.SingleChoice:
                return ids.Contains(answer) ? answer : null;
            case AnswerType.MultipleChoice:
                var picked = many.Where(ids.Contains).Distinct().ToList();
                return picked.Count > 0 ? picked : null;
            case AnswerType.Number:
                return decimal.TryParse(answer, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && n >= 0 && n < 1000
                    ? n.ToString(CultureInfo.InvariantCulture) : null;
            case AnswerType.Date:
                return DateOnly.TryParseExact(answer, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
            case AnswerType.ShortText:
                return answer.Length == 0 ? null : TextUtil.Clip(TextUtil.NoEmDash(answer), 300);
            case AnswerType.LongText:
                return answer.Length == 0 ? null : TextUtil.Clip(TextUtil.NoEmDash(answer), 2000);
            default:
                return null;
        }
    }
}
