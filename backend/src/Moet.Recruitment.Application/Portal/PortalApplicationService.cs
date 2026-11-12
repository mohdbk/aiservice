using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Assessments;
using Moet.Recruitment.Application.Backoffice;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Identity;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Portal;

public sealed record ApplyInput(
    string? Email, string? Epoch, string? Job, string? Phone, string? Country, string? CoverNote, string? Declaration,
    string? Lang, string? Screening, string? FileName, byte[]? Cv, string? TalentPool = null);

public sealed record StatusView(string Code, string Tone);

public sealed record ApplicationSummaryView(string Id, string Job, string Title, string Submitted, string Reference, StatusView Status);

/// <summary>Applying, the applicant's applications, and corrections to their CV details.</summary>
public sealed class PortalApplicationService(
    IAppDbContext db, ContentService content, PortalAccountService accounts, PortalSiteService site, AssessmentWorkflow assessments,
    JobQueue jobs, ICodeGenerator codes, IClock clock, PortalMailer mailer, BookingService bookings)
{
    public async Task<object> ApplyAsync(ApplyInput input, CancellationToken ct = default)
    {
        var user = await accounts.RequireAsync(input.Email, input.Epoch, ct);
        var s = await content.SettingsAsync(ct);
        var lang = await content.PortalLanguageAsync(input.Lang, ct);
        var job = await site.LoadPublishedAsync(input.Job, ct);
        if (!job.IsAccepting(clock.Today())) throw DomainException.Conflict("job_closed");
        if (input.Declaration is not ("1" or "true" or "on")) throw DomainException.Invalid("declaration_required");
        if (!TextUtil.ValidPhone(input.Phone)) throw DomainException.Invalid("phone_invalid");
        if (await db.JobApplications.AnyAsync(a => a.JobOpeningId == job.Id && a.ApplicantAccountId == user.Id, ct))
            throw DomainException.Conflict("already_applied");

        var asked = PortalSiteService.Asked(job);
        var evaluation = ScreeningRules.Evaluate(asked, ParseScreening(input.Screening));
        if (evaluation.Invalid.Count > 0)
            throw DomainException.Invalid("screening_invalid", new Dictionary<string, object?> { ["questions"] = evaluation.Invalid });
        if (evaluation.Missing.Count > 0)
            throw DomainException.Invalid("screening_required", new Dictionary<string, object?> { ["questions"] = evaluation.Missing });

        if (input.Cv == null || input.Cv.Length == 0) throw DomainException.Invalid("cv_required");
        var ext = CvFile.Validate(input.FileName ?? "", input.Cv, s.MaxCvMb);
        var now = clock.UtcNow;
        var file = new StoredFile
        {
            Id = Guid.NewGuid(), FileName = $"cv-{Slug(user.FullName)}.{ext}", ContentType = CvFile.ContentTypes[ext],
            SizeBytes = input.Cv.Length, Sha256 = Convert.ToHexString(SHA256.HashData(input.Cv)).ToLowerInvariant(),
            Content = input.Cv, CreatedAt = now,
        };
        db.StoredFiles.Add(file);
        var app = new JobApplication
        {
            Code = await codes.NextAsync(CodeKind.JobApplication, ct), JobOpeningId = job.Id, ApplicantAccountId = user.Id,
            FullName = user.FullName, Email = user.Email, Phone = TextUtil.Clip(input.Phone!.Trim(), 30),
            Country = Countries.IsKnown(input.Country) ? input.Country! : "", CoverNote = TextUtil.Clip(input.CoverNote?.Trim(), 4000),
            Status = ApplicationStatus.Open, LanguageCode = lang, DeclarationAcceptedAt = now, CvFileId = file.Id,
            ScreeningOutcome = evaluation.Outcome(asked), CreatedAt = now, UpdatedAt = now,
            CvAnalysis = new CvAnalysis { Status = s.AutoScoreApplications ? CvAnalysisStatus.Pending : CvAnalysisStatus.Skipped, UpdatedAt = now },
        };
        var order = 0;
        foreach (var q in asked)
        {
            var ans = evaluation.Answers.FirstOrDefault(a => a.Key == q.Key);
            app.ScreeningAnswers.Add(new ScreeningAnswer
            {
                ScreeningQuestionId = q.Question.Id, SortOrder = order++, QuestionText = q.Question.Text,
                AnswerText = ans?.Text ?? "", MeetsRequirement = ans?.Meets ?? (evaluation.NotMet.Contains(q.Key) ? false : null),
            });
        }
        db.ConsentRecords.Add(new ConsentRecord { ApplicantAccountId = user.Id, Kind = ConsentRecord.Declaration, Granted = true, Detail = app.Code, At = now });
        app.Notes.Add(new ApplicationNote
        {
            Kind = NoteKind.System, Text = $"Applied via the career portal ({lang}). Applicant declaration recorded.",
            AuthorApplicantId = user.Id, CreatedAt = now,
        });
        db.JobApplications.Add(app);
        // "Consider me for other suitable jobs" on the form joins the talent pool (a consent record is kept).
        if (input.TalentPool == "1")
        {
            var acc = await db.ApplicantAccounts.FirstAsync(x => x.Id == user.Id, ct);
            if (!acc.InTalentPool)
            {
                acc.InTalentPool = true;
                acc.TalentPoolAt = now;
                db.ConsentRecords.Add(new ConsentRecord { ApplicantAccountId = user.Id, Kind = ConsentRecord.TalentPool, Granted = true, Detail = app.Code, At = now });
            }
        }
        await db.SaveChangesAsync(ct);

        if (s.AutoScoreApplications) await jobs.EnqueueAsync(JobTypes.ScoreApplication, app.Id, ct);
        await assessments.InviteOnApplyAsync(app, ct);
        // "Application Received" email, when HR has switched it on.
        await mailer.QueueAsync(app.Email, app.FullName, lang, "application_received", await mailer.LinkAsync(lang, "/applications", ct),
            new Dictionary<string, string> { ["job_title"] = job.In(lang).Title, ["reference"] = app.Reference }, ct);
        await db.SaveChangesAsync(ct);
        return new { application = app.Code, reference = app.Reference };
    }

    static string Slug(string name)
    {
        var s = new string((name ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return s.Length == 0 ? "cv" : TextUtil.Clip(s, 40);
    }

    /// <summary>Screening answers come as JSON: {"sq1": "Yes", "sq2": ["12", "14"]}.</summary>
    static Dictionary<string, IReadOnlyList<string>> ParseScreening(string? raw)
    {
        var o = new Dictionary<string, IReadOnlyList<string>>();
        if (string.IsNullOrWhiteSpace(raw)) return o;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return o;
            foreach (var p in doc.RootElement.EnumerateObject())
                o[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.Array => p.Value.EnumerateArray().Select(Str).ToList(),
                    JsonValueKind.Null => [],
                    _ => [Str(p.Value)],
                };
        }
        catch (JsonException) { }
        return o;
    }

    static string Str(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => "",
    };

    async Task<Dictionary<int, bool>> ActiveInterviewsAsync(IEnumerable<int> appIds, CancellationToken ct)
    {
        var ids = appIds.ToList();
        var hits = await db.Interviews.AsNoTracking()
            .Where(i => ids.Contains(i.JobApplicationId) && (i.Status == InterviewStatus.Scheduled || i.Status == InterviewStatus.UnderReview))
            .Select(i => i.JobApplicationId).Distinct().ToListAsync(ct);
        return ids.ToDictionary(i => i, hits.Contains);
    }

    public async Task<object> ListAsync(string? email, string? epoch, string? lang, CancellationToken ct = default)
    {
        lang = await content.RequireLanguageAsync(lang, ct);
        var user = await accounts.RequireAsync(email, epoch, ct);
        var apps = await db.JobApplications.AsNoTracking().Include(a => a.JobOpening!).ThenInclude(j => j.Translations)
            .Where(a => a.ApplicantAccountId == user.Id).OrderByDescending(a => a.CreatedAt).Take(100).ToListAsync(ct);
        var interviews = await ActiveInterviewsAsync(apps.Select(a => a.Id), ct);
        return new { applications = apps.Select(a => Summary(a, lang, interviews[a.Id])).ToList() };
    }

    ApplicationSummaryView Summary(JobApplication a, string lang, bool interview)
    {
        var (code, tone) = CandidateStatus.For(a.Status, interview);
        return new ApplicationSummaryView(a.Code, a.JobOpening!.Code, a.JobOpening.In(lang).Title, clock.LocalDate(a.CreatedAt), a.Reference,
            new StatusView(code, tone));
    }

    async Task<JobApplication> OwnAsync(ApplicantAccount user, string? code, CancellationToken ct) =>
        string.IsNullOrEmpty(code) ? throw DomainException.NotFound("application_not_found")
        : await db.JobApplications.Include(a => a.JobOpening!).ThenInclude(j => j.Translations).Include(a => a.CvAnalysis)
              .FirstOrDefaultAsync(a => a.Code == code && a.ApplicantAccountId == user.Id, ct)
          ?? throw DomainException.NotFound("application_not_found");

    public async Task<object> DetailAsync(string? email, string? epoch, string? code, string? lang, CancellationToken ct = default)
    {
        lang = await content.RequireLanguageAsync(lang, ct);
        var user = await accounts.RequireAsync(email, epoch, ct);
        var a = await OwnAsync(user, code, ct);
        var interview = (await ActiveInterviewsAsync([a.Id], ct))[a.Id];
        var sum = Summary(a, lang, interview);
        var cv = a.CvAnalysis;
        var state = cv?.ProfileJson != null ? "ready" : cv?.Status is CvAnalysisStatus.Failed or CvAnalysisStatus.Skipped ? "unavailable" : "preparing";
        return new
        {
            id = sum.Id, job = sum.Job, title = sum.Title, submitted = sum.Submitted, reference = sum.Reference, status = sum.Status,
            details_state = state, details = Details(cv?.ProfileJson, cv?.YearsOfExperience),
            booking = await bookings.OpenForCandidateAsync(a.Id, lang, ct),
        };
    }

    /// <summary>The parsed CV details the applicant may check (never scores or reasoning).</summary>
    static object? Details(string? json, decimal? years)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var p = doc.RootElement;
            static string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            static IEnumerable<JsonElement> A(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];
            return new
            {
                years = years ?? (p.TryGetProperty("years_of_experience", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetDecimal() : (decimal?)null),
                work = A(p, "work").Take(10).Select(w => new
                {
                    position = S(w, "position"), employer = S(w, "name") is { Length: > 0 } n ? n : S(w, "description"),
                    start = S(w, "startDate"), end = S(w, "endDate"),
                }).ToList(),
                education = A(p, "education").Take(6).Select(e => new
                {
                    degree = string.Join(" ", new[] { S(e, "studyType"), S(e, "area") }.Where(x => x.Length > 0)),
                    institution = S(e, "institution"), end = S(e, "endDate"),
                }).ToList(),
                skills = A(p, "skills").SelectMany(sk => A(sk, "keywords").Select(k => k.GetString() ?? "").DefaultIfEmpty(S(sk, "name")))
                    .Where(x => x.Length > 0).Distinct().Take(30).ToList(),
                languages = A(p, "languages").Where(l => S(l, "language").Length > 0)
                    .Select(l => S(l, "language") + (S(l, "fluency") is { Length: > 0 } f ? $" ({f})" : "")).Take(8).ToList(),
                certificates = A(p, "certificates").Select(c => S(c, "name")).Where(x => x.Length > 0).Take(10).ToList(),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<object> AddCorrectionAsync(string? email, string? epoch, string? code, string? note, CancellationToken ct = default)
    {
        var user = await accounts.RequireAsync(email, epoch, ct);
        var a = await OwnAsync(user, code, ct);
        note = (note ?? "").Trim();
        if (note.Length is < 3 or > 2000) throw DomainException.Invalid("note_length");
        db.ApplicationNotes.Add(new ApplicationNote
        {
            JobApplicationId = a.Id, Kind = NoteKind.Correction, Text = note, AuthorApplicantId = user.Id, CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    internal static string Invariant(decimal d) => d.ToString(CultureInfo.InvariantCulture);
}
