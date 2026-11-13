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
    string? Lang, string? Screening, string? FileName, byte[]? Cv, string? TalentPool = null, string? Nationality = null);

public sealed record StatusView(string Code, string Tone);

public sealed record ApplicationSummaryView(string Id, string Job, string Title, string Submitted, string Reference, StatusView Status);

/// <summary>Applying, the applicant's applications, and corrections to their CV details.</summary>
public sealed class PortalApplicationService(
    IAppDbContext db, ContentService content, PortalAccountService accounts, PortalSiteService site, AssessmentWorkflow assessments,
    JobQueue jobs, ICodeGenerator codes, IClock clock, PortalMailer mailer, BookingService bookings, DocumentService documents,
    BackofficeInterviewService offers, IVirusScanner scanner)
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
        await scanner.CheckAsync(input.Cv, ct);
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
            Country = await KnownCountryAsync(input.Country, ct), Nationality = await KnownCountryAsync(input.Nationality, ct), CoverNote = TextUtil.Clip(input.CoverNote?.Trim(), 4000),
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

    /// <summary>A country from HR's list (by its English name, or its Arabic name or code from an older client), stored in English; "" otherwise.</summary>
    async Task<string> KnownCountryAsync(string? value, CancellationToken ct)
    {
        var v = value?.Trim() ?? "";
        if (v.Length == 0) return "";
        return await db.Countries.AsNoTracking().Where(c => c.NameEn == v || c.NameAr == v || c.Code == v).Select(c => c.NameEn).FirstOrDefaultAsync(ct) ?? "";
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
            offer = await OfferAsync(a.Id, lang, ct),
            timeline = await TimelineAsync(a, ct),
        };
    }

    /// <summary>
    /// What has happened with the application so far, oldest first: submitted, assessments, interviews, the offer and the
    /// outcome. Dates only, in neutral words (never who reviewed it or how).
    /// </summary>
    async Task<List<object>> TimelineAsync(JobApplication a, CancellationToken ct)
    {
        static string D(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var now = clock.UtcNow;
        var steps = new List<(DateTime At, string Key, string State)> { (a.CreatedAt, "submitted", "done") };
        var invitations = await db.Invitations.AsNoTracking().Where(i => i.JobApplicationId == a.Id)
            .Select(i => new { i.CreatedAt, i.Status, SubmittedAt = i.Attempts.Max(t => t.SubmittedAt) }).ToListAsync(ct);
        foreach (var i in invitations)
        {
            if (i.Status is InvitationStatus.Submitted or InvitationStatus.Marked) steps.Add((i.SubmittedAt ?? i.CreatedAt, "assessment_done", "done"));
            else if (i.Status is InvitationStatus.Invited or InvitationStatus.InProgress) steps.Add((i.CreatedAt, "assessment_invited", "current"));
        }
        var interviews = await db.Interviews.AsNoTracking().Where(i => i.JobApplicationId == a.Id && i.Status != InterviewStatus.Cancelled)
            .Select(i => new { i.StartsAt }).ToListAsync(ct);
        foreach (var i in interviews) steps.Add((i.StartsAt, i.StartsAt <= now ? "interview_done" : "interview_planned", i.StartsAt <= now ? "done" : "current"));
        var offer = await db.JobOffers.AsNoTracking().Where(o => o.JobApplicationId == a.Id && o.Status != OfferStatus.Draft && o.Status != OfferStatus.Withdrawn)
            .OrderByDescending(o => o.CreatedAt).Select(o => new { o.Status, o.CreatedAt, o.UpdatedAt }).FirstOrDefaultAsync(ct);
        if (offer != null)
        {
            steps.Add((offer.CreatedAt, "offer_sent", offer.Status == OfferStatus.Sent ? "current" : "done"));
            if (offer.Status == OfferStatus.Accepted) steps.Add((offer.UpdatedAt, "offer_accepted", "done"));
            if (offer.Status == OfferStatus.Declined) steps.Add((offer.UpdatedAt, "offer_declined", "done"));
        }
        if (a.Status == ApplicationStatus.Rejected) steps.Add((a.UpdatedAt, "closed", "done"));
        return steps.OrderBy(x => x.At).Select(x => (object)new { key = x.Key, date = D(x.At), state = x.State }).ToList();
    }

    /// <summary>A sent offer waiting for the candidate's answer, with its letter; or the answer they gave.</summary>
    async Task<object?> OfferAsync(int appId, string lang, CancellationToken ct)
    {
        var o = await db.JobOffers.AsNoTracking().Where(x => x.JobApplicationId == appId && (x.Status == OfferStatus.Sent || x.Status == OfferStatus.Accepted || x.Status == OfferStatus.Declined))
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (o == null) return null;
        var letter = o.Status == OfferStatus.Sent ? await documents.RenderAsync(DocumentKinds.OfferLetter, o.Id, null, lang == "ar" ? "ar" : "en", ct) : null;
        return new
        {
            code = o.Code, status = o.Status.ToString().ToLowerInvariant(), position_title = o.PositionTitle,
            start_date = o.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), letter_html = letter?.Html ?? "",
        };
    }

    /// <summary>The candidate accepts or declines the offer sent to them (as HR would record it, with a note on the application).</summary>
    public async Task<object> RespondOfferAsync(string? email, string? epoch, string? code, bool accept, string? reason, CancellationToken ct = default)
    {
        var user = await accounts.RequireAsync(email, epoch, ct);
        var a = await OwnAsync(user, code, ct);
        var o = await db.JobOffers.AsNoTracking().Where(x => x.JobApplicationId == a.Id && x.Status == OfferStatus.Sent).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct)
            ?? throw DomainException.Conflict("offer_not_open");
        await offers.SetOfferStatusAsync(o.Id, accept ? nameof(OfferStatus.Accepted) : nameof(OfferStatus.Declined), ct);
        db.ApplicationNotes.Add(new ApplicationNote
        {
            JobApplicationId = a.Id, Kind = NoteKind.System, AuthorApplicantId = user.Id, CreatedAt = clock.UtcNow,
            Text = TextUtil.Clip($"Offer {o.Code} {(accept ? "accepted" : "declined")} by the candidate in the career portal." + (string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {reason.Trim()}"), 4000),
        });
        await db.SaveChangesAsync(ct);
        return new { status = accept ? "accepted" : "declined" };
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
