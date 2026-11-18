using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Ai;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Portal;

/// <summary>
/// A candidate's CV library (up to five, one the default): added by the candidate in the portal, by HR for them, or when
/// they apply with a new file; read in the background; used to fill in an application and to find people in the talent
/// pool by their CV. Applications keep their own copy of the file, so deleting a CV here never changes an application.
/// </summary>
public sealed partial class CandidateCvService(IAppDbContext db, IClock clock, ContentService content, IVirusScanner scanner, JobQueue jobs)
{
    public async Task<object> ListAsync(int accountId, CancellationToken ct = default) => new
    {
        cvs = await db.CandidateCvs.AsNoTracking().Where(c => c.ApplicantAccountId == accountId).OrderByDescending(c => c.IsDefault).ThenByDescending(c => c.UploadedAt)
            .Select(c => new
            {
                id = c.Id, file_name = c.FileName, size = c.SizeBytes, is_default = c.IsDefault, status = c.Status, uploaded_at = c.UploadedAt,
                by_staff = c.UploadedByStaffId != null, years = c.YearsOfExperience, summary = c.Summary,
            }).ToListAsync(ct),
        max = CandidateCv.MaxPerAccount,
    };

    /// <summary>Adds a CV (checked like an application CV). The first one becomes the default. Returns the new id.</summary>
    public async Task<int> AddAsync(int accountId, string? fileName, byte[]? data, int? staffId, bool makeDefault, CancellationToken ct = default)
    {
        if (data == null || data.Length == 0) throw DomainException.Invalid("cv_required");
        var s = await content.SettingsAsync(ct);
        var ext = CvFile.Validate(fileName ?? "", data, s.MaxCvMb);
        var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var existing = await db.CandidateCvs.FirstOrDefaultAsync(c => c.ApplicantAccountId == accountId && c.Sha256 == sha, ct);
        if (existing != null)
        {
            if (makeDefault) await SetDefaultAsync(accountId, existing.Id, ct);
            return existing.Id;
        }
        if (await db.CandidateCvs.CountAsync(c => c.ApplicantAccountId == accountId, ct) >= CandidateCv.MaxPerAccount)
            throw new DomainException("cv_limit", 409, new Dictionary<string, object?> { ["max"] = CandidateCv.MaxPerAccount });
        await scanner.CheckAsync(data, ct);
        var now = clock.UtcNow;
        var name = TextUtil.Clip(Path.GetFileName(fileName ?? $"cv.{ext}"), 200);
        var file = new StoredFile { Id = Guid.NewGuid(), FileName = name, ContentType = CvFile.ContentTypes[ext], SizeBytes = data.Length, Sha256 = sha, Content = data, CreatedAt = now };
        db.StoredFiles.Add(file);
        // The file first: the CV row refers to it (no navigation for the database to order the inserts by).
        await db.SaveChangesAsync(ct);
        var first = !await db.CandidateCvs.AnyAsync(c => c.ApplicantAccountId == accountId, ct);
        if (makeDefault && !first)
            await db.CandidateCvs.Where(c => c.ApplicantAccountId == accountId && c.IsDefault).ForEachAsync(c => c.IsDefault = false, ct);
        var cv = new CandidateCv
        {
            ApplicantAccountId = accountId, StoredFileId = file.Id, FileName = name, SizeBytes = data.Length, Sha256 = sha, IsDefault = first || makeDefault,
            Status = "Pending", UploadedByStaffId = staffId, UploadedAt = now,
        };
        db.CandidateCvs.Add(cv);
        await db.SaveChangesAsync(ct);
        await jobs.EnqueueAsync(JobTypes.ReadCandidateCv, cv.Id, ct);
        await db.SaveChangesAsync(ct);
        return cv.Id;
    }

    public async Task<object> SetDefaultAsync(int accountId, int cvId, CancellationToken ct = default)
    {
        var all = await db.CandidateCvs.Where(c => c.ApplicantAccountId == accountId).ToListAsync(ct);
        if (all.All(c => c.Id != cvId)) throw DomainException.NotFound("cv_not_found");
        foreach (var c in all) c.IsDefault = c.Id == cvId;
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    /// <summary>Removes a CV and its file (applications keep their own copies); the newest left becomes the default.</summary>
    public async Task<object> DeleteAsync(int accountId, int cvId, CancellationToken ct = default)
    {
        var cv = await db.CandidateCvs.FirstOrDefaultAsync(c => c.ApplicantAccountId == accountId && c.Id == cvId, ct) ?? throw DomainException.NotFound("cv_not_found");
        db.CandidateCvs.Remove(cv);
        await db.SaveChangesAsync(ct);
        if (!await db.JobApplications.AnyAsync(a => a.CvFileId == cv.StoredFileId, ct))
            await db.StoredFiles.Where(f => f.Id == cv.StoredFileId).ExecuteDeleteAsync(ct);
        if (cv.IsDefault && await db.CandidateCvs.OrderByDescending(c => c.UploadedAt).FirstOrDefaultAsync(c => c.ApplicantAccountId == accountId, ct) is { } next)
        {
            next.IsDefault = true;
            await db.SaveChangesAsync(ct);
        }
        return new { ok = true };
    }

    public async Task<(byte[] Content, string ContentType, string FileName)> FileAsync(int accountId, int cvId, CancellationToken ct = default)
    {
        var cv = await db.CandidateCvs.AsNoTracking().FirstOrDefaultAsync(c => c.ApplicantAccountId == accountId && c.Id == cvId, ct) ?? throw DomainException.NotFound("cv_not_found");
        var f = await db.StoredFiles.AsNoTracking().FirstAsync(x => x.Id == cv.StoredFileId, ct);
        return (f.Content, f.ContentType, cv.FileName);
    }

    /// <summary>
    /// What the application form can take from a CV: the phone number, and (once read) the latest role, years of experience,
    /// education, skills and languages, for the candidate to check. Status "pending" while it is still being read.
    /// </summary>
    public async Task<object> PrefillAsync(int accountId, int cvId, CancellationToken ct = default)
    {
        var cv = await db.CandidateCvs.AsNoTracking().FirstOrDefaultAsync(c => c.ApplicantAccountId == accountId && c.Id == cvId, ct) ?? throw DomainException.NotFound("cv_not_found");
        string latestRole = "", employer = "";
        var education = new List<string>();
        var skills = new List<string>();
        var languages = new List<string>();
        if (cv.ProfileJson is { Length: > 0 } json)
        {
            using var doc = JsonDocument.Parse(json);
            var p = doc.RootElement;
            var work = CvScoringHandler.Arr(p, "work").FirstOrDefault();
            if (work.ValueKind == JsonValueKind.Object) { latestRole = CvScoringHandler.Str(work, "position"); employer = CvScoringHandler.Str(work, "name"); }
            education = CvScoringHandler.Arr(p, "education").Select(e => string.Join(", ", new[] { $"{CvScoringHandler.Str(e, "studyType")} {CvScoringHandler.Str(e, "area")}".Trim(), CvScoringHandler.Str(e, "institution") }.Where(x => x.Length > 0)))
                .Where(x => x.Length > 0).Take(3).ToList();
            skills = CvScoringHandler.Arr(p, "skills").SelectMany(s => new[] { CvScoringHandler.Str(s, "name") }.Concat(CvScoringHandler.Arr(s, "keywords").Select(k => k.GetString() ?? "")))
                .Where(x => x.Length > 0).Distinct().Take(12).ToList();
            languages = CvScoringHandler.Arr(p, "languages").Select(l => $"{CvScoringHandler.Str(l, "language")} {(CvScoringHandler.Str(l, "fluency") is { Length: > 0 } f ? $"({f})" : "")}".Trim())
                .Where(x => x.Length > 0).Take(5).ToList();
        }
        return new
        {
            status = cv.Status == "Pending" ? "pending" : cv.Status == "Unreadable" ? "unreadable" : "ready",
            phone = cv.Phone, years = cv.YearsOfExperience, latest_role = latestRole, employer, education, skills, languages,
        };
    }

    /// <summary>The account's CV to apply with: a copy of its file for the application (applications own their file).</summary>
    internal async Task<(string FileName, byte[] Content)?> ForApplyingAsync(int accountId, int cvId, CancellationToken ct)
    {
        var cv = await db.CandidateCvs.AsNoTracking().FirstOrDefaultAsync(c => c.ApplicantAccountId == accountId && c.Id == cvId, ct);
        if (cv == null) return null;
        var f = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cv.StoredFileId, ct);
        return f == null ? null : (cv.FileName, f.Content);
    }

    [GeneratedRegex(@"(?:\+|00)?(?:971|966|965|974|973|968|\d{1,3})?[\s\-()]*\d[\d\s\-()]{7,14}\d")] internal static partial Regex PhoneRe();

    /// <summary>The first phone-like number in a CV (9 to 15 digits), tidied; "" when there is none.</summary>
    public static string FindPhone(string text)
    {
        foreach (Match m in PhoneRe().Matches(text ?? ""))
        {
            var digits = m.Value.Count(char.IsDigit);
            if (digits is >= 9 and <= 15 && !Regex.IsMatch(m.Value, @"^\s*(19|20)\d\d\s*[-–]")) return Regex.Replace(m.Value.Trim(), @"\s{2,}", " ").Replace("00971", "+971");
        }
        return "";
    }
}

/// <summary>Reads a library CV: its text, a phone number, and a structured profile when a language model is set up.</summary>
public sealed class CandidateCvReadHandler(IAppDbContext db, ILanguageModel model, ICvReader reader, IClock clock) : IJobHandler
{
    public string JobType => JobTypes.ReadCandidateCv;

    public async Task RunAsync(int cvId, CancellationToken ct)
    {
        var cv = await db.CandidateCvs.FirstOrDefaultAsync(c => c.Id == cvId, ct);
        if (cv == null) return;
        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == cv.StoredFileId, ct);
        var text = file == null ? null : await reader.ReadAsync(file.Content, Path.GetExtension(cv.FileName), ct);
        cv.ReadAt = clock.UtcNow;
        if (text == null || text.Text.Trim().Length < 50)
        {
            cv.Status = "Unreadable";
            await db.SaveChangesAsync(ct);
            return;
        }
        cv.Text = TextUtil.Clip(text.Text, 60000);
        cv.Phone = TextUtil.Clip(CandidateCvService.FindPhone(text.Text), 40);
        if (!model.IsConfigured)
        {
            cv.Status = "TextOnly";
            await db.SaveChangesAsync(ct);
            return;
        }
        var name = await db.ApplicantAccounts.Where(a => a.Id == cv.ApplicantAccountId).Select(a => a.FullName).FirstOrDefaultAsync(ct) ?? "";
        var profile = await model.CompleteJsonAsync(Prompts.Profile, $"<cv>\n{TextUtil.Clip(CvScoring.Redact(text.Text, name), 40000)}\n</cv>", Prompts.ProfileSchema, "cv_profile", ct);
        var work = CvScoringHandler.Arr(profile, "work").Select(w => (CvScoringHandler.Str(w, "startDate"), CvScoringHandler.Str(w, "endDate"))).ToList();
        cv.YearsOfExperience = CvScoring.ResolveExperience(CvScoring.YearsOfExperience(work, clock.Today()), CvScoringHandler.Num(profile, "stated_years_of_experience"));
        var basics = profile.TryGetProperty("basics", out var b) ? b : default;
        var label = basics.ValueKind == JsonValueKind.Object ? CvScoringHandler.Str(basics, "label") : "";
        var summary = basics.ValueKind == JsonValueKind.Object ? CvScoringHandler.Str(basics, "summary") : "";
        cv.Summary = TextUtil.Clip(TextUtil.NoEmDash(string.Join(". ", new[] { label, summary }.Where(x => x.Length > 0))), 2000);
        cv.ProfileJson = profile.GetRawText();
        cv.Status = "Ready";
        await db.SaveChangesAsync(ct);
    }
}
