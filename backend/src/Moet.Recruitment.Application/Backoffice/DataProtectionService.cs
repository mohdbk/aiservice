using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Identity;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record RetentionInput(bool IsEnabled, int UnsuccessfulMonths, int InactiveAccountMonths);

/// <summary>
/// The UAE Personal Data Protection Law in practice: a candidate's copy of their data, account deletion requests reviewed by
/// system administrators, consent records, and retention (old unsuccessful applications and inactive accounts are anonymised).
/// Anonymising keeps the records for reporting (dates, statuses, scores) and removes what identifies the person.
/// </summary>
public sealed class DataProtectionService(IAppDbContext db, ICurrentActor actor, IClock clock)
{
    public const string Removed = "Anonymised";

    // ---------------- candidate side ----------------

    /// <summary>Everything the service holds about the candidate, as a document they can keep. Recorded as a completed request.</summary>
    public async Task<object> ExportAsync(ApplicantAccount user, CancellationToken ct = default)
    {
        var apps = await db.JobApplications.AsNoTracking().Where(a => a.ApplicantAccountId == user.Id)
            .Include(a => a.JobOpening!).Include(a => a.ScreeningAnswers).Include(a => a.Notes)
            .AsSplitQuery().OrderBy(a => a.CreatedAt).ToListAsync(ct);
        var ids = apps.Select(a => a.Id).ToList();
        var invitations = await db.Invitations.AsNoTracking().Where(i => ids.Contains(i.JobApplicationId)).Include(i => i.Assessment)
            .Include(i => i.Attempts).ThenInclude(t => t.Sections).ThenInclude(s => s.Questions).AsSplitQuery().ToListAsync(ct);
        var interviews = await db.Interviews.AsNoTracking().Where(i => ids.Contains(i.JobApplicationId)).Include(i => i.InterviewRound).ToListAsync(ct);
        var offers = await db.JobOffers.AsNoTracking().Where(o => ids.Contains(o.JobApplicationId)).ToListAsync(ct);
        var consents = await db.ConsentRecords.AsNoTracking().Where(c => c.ApplicantAccountId == user.Id).OrderBy(c => c.At).ToListAsync(ct);
        db.DataRequests.Add(new DataRequest
        {
            ApplicantAccountId = user.Id, Kind = DataRequestKind.Export, Status = DataRequestStatus.Completed, Requester = Who(user),
            CreatedAt = clock.UtcNow, ClosedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        static string D(DateTime? d) => d?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) ?? "";
        return new
        {
            produced_at = D(clock.UtcNow),
            account = new { email = user.Email, full_name = user.FullName, language = user.PreferredLanguageCode, created_at = D(user.CreatedAt), last_sign_in_at = D(user.LastSignInAt) },
            applications = apps.Select(a => new
            {
                reference = a.Reference, job = a.JobOpening!.Title, applied_at = D(a.CreatedAt), status = a.Status.ToString(), full_name = a.FullName,
                email = a.Email, phone = a.Phone, country = a.Country, cover_note = a.CoverNote, declaration_at = D(a.DeclarationAcceptedAt),
                screening = a.ScreeningAnswers.OrderBy(s => s.SortOrder).Select(s => new { question = s.QuestionText, answer = s.AnswerText }),
                corrections = a.Notes.Where(n => n.AuthorApplicantId == user.Id && n.Kind != NoteKind.System).Select(n => new { at = D(n.CreatedAt), text = n.Text }),
                assessments = invitations.Where(i => i.JobApplicationId == a.Id).Select(i => new
                {
                    test = i.Assessment!.Title, status = i.Status.ToString(), language = i.LanguageCode,
                    attempts = i.Attempts.OrderBy(t => t.AttemptNo).Select(t => new
                    {
                        started_at = D(t.StartedAt), submitted_at = D(t.SubmittedAt),
                        answers = t.Sections.OrderBy(s => s.SectionNo).SelectMany(s => s.Questions.OrderBy(q => q.SortOrder))
                            .Where(q => q.AnswerText != null || q.AnswerFileName != null).Select(q => new { question = q.Text, answer = q.AnswerText ?? "", file = q.AnswerFileName ?? "" }),
                    }),
                }),
                interviews = interviews.Where(i => i.JobApplicationId == a.Id).Select(i => new { round = i.InterviewRound!.NameEn.Length > 0 ? i.InterviewRound.NameEn : i.InterviewRound.Name, starts_at = D(i.StartsAt), location = i.Location, status = i.Status.ToString() }),
                offers = offers.Where(o => o.JobApplicationId == a.Id && o.Status != OfferStatus.Draft).Select(o => new { position = o.PositionTitle, offer_date = o.OfferDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), status = o.Status.ToString() }),
            }),
            consents = consents.Select(c => new { kind = c.Kind, granted = c.Granted, detail = c.Detail, at = D(c.At) }),
        };
    }

    public async Task<object> RequestDeletionAsync(ApplicantAccount user, string? reason, CancellationToken ct = default)
    {
        if (await db.DataRequests.AnyAsync(r => r.ApplicantAccountId == user.Id && r.Kind == DataRequestKind.Deletion && r.Status == DataRequestStatus.Open, ct))
            return new { requested = true, already = true };
        db.DataRequests.Add(new DataRequest
        {
            ApplicantAccountId = user.Id, Kind = DataRequestKind.Deletion, Requester = Who(user), Reason = TextUtil.NoEmDash(TextUtil.Clip(reason?.Trim(), 2000)),
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return new { requested = true, already = false };
    }

    public async Task<object> MyRequestsAsync(ApplicantAccount user, CancellationToken ct = default) =>
        new
        {
            deletion_pending = await db.DataRequests.AnyAsync(r => r.ApplicantAccountId == user.Id && r.Kind == DataRequestKind.Deletion && r.Status == DataRequestStatus.Open, ct),
            in_talent_pool = user.InTalentPool,
        };

    /// <summary>Joins or leaves the talent pool (other suitable jobs may be suggested to the candidate); kept as a consent record.</summary>
    public async Task<object> SetTalentPoolAsync(ApplicantAccount user, bool join, string detail, CancellationToken ct = default)
    {
        var u = await db.ApplicantAccounts.FirstAsync(x => x.Id == user.Id, ct);
        if (u.InTalentPool != join)
        {
            u.InTalentPool = join;
            u.TalentPoolAt = join ? clock.UtcNow : null;
            db.ConsentRecords.Add(new ConsentRecord { ApplicantAccountId = u.Id, Kind = ConsentRecord.TalentPool, Granted = join, Detail = TextUtil.Clip(detail, 200), At = clock.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        return new { in_talent_pool = u.InTalentPool };
    }

    static string Who(ApplicantAccount u) => TextUtil.Clip($"{u.FullName} <{u.Email}>", 400);

    // ---------------- backoffice ----------------

    static readonly Sorts<DataRequest> RequestSorts = new Sorts<DataRequest>("created").Add("created", r => r.CreatedAt).Add("status", r => r.Status).Add("kind", r => r.Kind);

    public Task<PageResult<object>> RequestsAsync(ListQuery lq, string? status, string? kind, CancellationToken ct = default)
    {
        var q = db.DataRequests.AsNoTracking().AsQueryable();
        if (Enum.TryParse<DataRequestStatus>(status, out var s)) q = q.Where(r => r.Status == s);
        if (Enum.TryParse<DataRequestKind>(kind, out var k)) q = q.Where(r => r.Kind == k);
        if (lq.Term.Length > 0) q = q.Where(r => r.Requester.Contains(lq.Term));
        return q.PageAsync(lq, RequestSorts, r => (object)new
        {
            id = r.Id, kind = r.Kind.ToString(), status = r.Status.ToString(), requester = r.Requester, reason = r.Reason, response = r.Response,
            created_at = r.CreatedAt, closed_at = r.ClosedAt,
            applications = db.JobApplications.Count(a => a.ApplicantAccountId == r.ApplicantAccountId),
        }, ct);
    }

    /// <summary>Approves a deletion request: the account and its applications are anonymised (hired applicants' records are kept).</summary>
    public async Task<object> ApproveAsync(int id, string? response, CancellationToken ct = default)
    {
        var r = await OpenRequestAsync(id, ct);
        var kept = await AnonymiseAccountAsync(r.ApplicantAccountId, ct);
        r.Status = DataRequestStatus.Completed;
        r.Response = TextUtil.NoEmDash(TextUtil.Clip(response?.Trim(), 2000));
        r.ClosedAt = clock.UtcNow;
        r.ClosedByStaffId = actor.Id;
        await db.SaveChangesAsync(ct);
        return new { ok = true, kept_hired = kept };
    }

    public async Task<object> DeclineAsync(int id, string? response, CancellationToken ct = default)
    {
        var r = await OpenRequestAsync(id, ct);
        if (string.IsNullOrWhiteSpace(response)) throw DomainException.Invalid("reason_required");
        r.Status = DataRequestStatus.Declined;
        r.Response = TextUtil.NoEmDash(TextUtil.Clip(response.Trim(), 2000));
        r.ClosedAt = clock.UtcNow;
        r.ClosedByStaffId = actor.Id;
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    async Task<DataRequest> OpenRequestAsync(int id, CancellationToken ct)
    {
        var r = await db.DataRequests.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        if (r.Kind != DataRequestKind.Deletion || r.Status != DataRequestStatus.Open) throw DomainException.Conflict("request_closed");
        return r;
    }

    static readonly Sorts<ConsentRecord> ConsentSorts = new Sorts<ConsentRecord>("at").Add("at", c => c.At).Add("kind", c => c.Kind);

    public Task<PageResult<object>> ConsentsAsync(ListQuery lq, string? kind, CancellationToken ct = default)
    {
        var q = db.ConsentRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(kind)) q = q.Where(c => c.Kind == kind);
        if (lq.Term.Length > 0) q = q.Where(c => c.Detail.Contains(lq.Term) || db.ApplicantAccounts.Any(a => a.Id == c.ApplicantAccountId && a.Email.Contains(lq.Term)));
        return q.PageAsync(lq, ConsentSorts, c => (object)new
        {
            id = c.Id, kind = c.Kind, granted = c.Granted, detail = c.Detail, at = c.At,
            email = db.ApplicantAccounts.Where(a => a.Id == c.ApplicantAccountId).Select(a => a.Email).FirstOrDefault() ?? "",
        }, ct);
    }

    // ---------------- retention ----------------

    public async Task<RetentionSetting> RetentionAsync(CancellationToken ct = default) =>
        await db.RetentionSettings.FirstOrDefaultAsync(ct) ?? new RetentionSetting();

    public async Task<object> RetentionViewAsync(CancellationToken ct = default)
    {
        var s = await RetentionAsync(ct);
        var (apps, accounts) = await DueAsync(s, ct);
        return new
        {
            is_enabled = s.IsEnabled, unsuccessful_months = (int)s.UnsuccessfulMonths, inactive_account_months = (int)s.InactiveAccountMonths,
            last_run_at = s.LastRunAt, last_run_count = s.LastRunCount, due_applications = apps.Count, due_accounts = accounts.Count,
            open_requests = await db.DataRequests.CountAsync(r => r.Status == DataRequestStatus.Open, ct),
        };
    }

    public async Task<object> SaveRetentionAsync(RetentionInput input, CancellationToken ct = default)
    {
        var s = await db.RetentionSettings.FirstOrDefaultAsync(ct);
        if (s == null) db.RetentionSettings.Add(s = new RetentionSetting());
        s.IsEnabled = input.IsEnabled;
        s.UnsuccessfulMonths = (short)Math.Clamp(input.UnsuccessfulMonths, -1, 1000);
        s.InactiveAccountMonths = (short)Math.Clamp(input.InactiveAccountMonths, -1, 1000);
        s.Validate();
        s.UpdatedAt = clock.UtcNow;
        s.UpdatedByStaffId = actor.Id;
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    /// <summary>
    /// What retention would anonymise now: unsuccessful applications (not selected, or the job closed without an offer accepted)
    /// untouched for the set months, and accounts with no applications and no sign-in for the set months.
    /// </summary>
    async Task<(List<int> Apps, List<int> Accounts)> DueAsync(RetentionSetting s, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var appCutoff = now.AddMonths(-s.UnsuccessfulMonths);
        var accCutoff = now.AddMonths(-s.InactiveAccountMonths);
        var apps = await db.JobApplications.Where(a => a.AnonymisedAt == null && a.UpdatedAt < appCutoff
                && (a.Status == ApplicationStatus.Rejected || a.JobOpening!.Status == JobStatus.Closed)
                && !db.JobOffers.Any(o => o.JobApplicationId == a.Id && o.Status == OfferStatus.Accepted))
            .Select(a => a.Id).Take(500).ToListAsync(ct);
        var accounts = await db.ApplicantAccounts.Where(u => u.AnonymisedAt == null && (u.LastSignInAt ?? u.CreatedAt) < accCutoff
                && !db.JobApplications.Any(a => a.ApplicantAccountId == u.Id))
            .Select(u => u.Id).Take(500).ToListAsync(ct);
        return (apps, accounts);
    }

    /// <summary>Applies retention (daily, and on demand). Returns how many records were anonymised.</summary>
    public async Task<int> RunRetentionAsync(bool force, CancellationToken ct = default)
    {
        var s = await db.RetentionSettings.FirstOrDefaultAsync(ct);
        if (s == null || (!s.IsEnabled && !force)) return 0;
        var (apps, accounts) = await DueAsync(s, ct);
        foreach (var id in apps) await AnonymiseApplicationAsync(id, ct);
        foreach (var id in accounts) await AnonymiseAccountOnlyAsync(id, ct);
        s.LastRunAt = clock.UtcNow;
        s.LastRunCount = apps.Count + accounts.Count;
        await db.SaveChangesAsync(ct);
        return s.LastRunCount;
    }

    // ---------------- anonymising ----------------

    /// <summary>Anonymises the account and every application except those that ended in a hire (kept as employment records).</summary>
    public async Task<int> AnonymiseAccountAsync(int accountId, CancellationToken ct)
    {
        var apps = await db.JobApplications.Where(a => a.ApplicantAccountId == accountId && a.AnonymisedAt == null)
            .Select(a => new { a.Id, Hired = db.JobOffers.Any(o => o.JobApplicationId == a.Id && o.Status == OfferStatus.Accepted) }).ToListAsync(ct);
        foreach (var a in apps.Where(a => !a.Hired)) await AnonymiseApplicationAsync(a.Id, ct);
        await AnonymiseAccountOnlyAsync(accountId, ct);
        return apps.Count(a => a.Hired);
    }

    async Task AnonymiseAccountOnlyAsync(int accountId, CancellationToken ct)
    {
        var u = await db.ApplicantAccounts.FirstOrDefaultAsync(x => x.Id == accountId, ct);
        if (u == null || u.AnonymisedAt != null) return;
        await db.EmailMessages.Where(m => m.ToAddress == u.Email).ExecuteDeleteAsync(ct);
        await db.AccountTokens.Where(t => t.ApplicantAccountId == u.Id).ExecuteDeleteAsync(ct);
        await db.JobAlerts.Where(a => a.ApplicantAccountId == u.Id).ExecuteDeleteAsync(ct);
        u.Email = $"anonymised-{u.Id}@invalid.local";
        u.FullName = Removed;
        u.PasswordHash = "";
        u.IsEnabled = false;
        u.IsEmailVerified = false;
        u.SessionVersion++;
        u.InTalentPool = false;
        u.AnonymisedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task AnonymiseApplicationAsync(int appId, CancellationToken ct)
    {
        var a = await db.JobApplications.Include(x => x.ScreeningAnswers).Include(x => x.Notes).Include(x => x.CvAnalysis!).ThenInclude(c => c.CriterionScores)
            .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == appId, ct);
        if (a == null || a.AnonymisedAt != null) return;
        var email = a.Email;
        a.FullName = Removed;
        a.Email = $"anonymised-{a.Id}@invalid.local";
        a.Phone = "";
        a.Country = "";
        a.CoverNote = "";
        a.AnonymisedAt = clock.UtcNow;
        foreach (var s in a.ScreeningAnswers) s.AnswerText = "";
        db.ApplicationNotes.RemoveRange(a.Notes.Where(n => n.Kind != NoteKind.System));
        if (a.CvAnalysis is { } c)
        {
            c.Summary = "";
            c.SummaryArabic = "";
            c.MustHaveGaps = "";
            c.ProfileJson = null;
            foreach (var cs in c.CriterionScores) cs.Evidence = "";
        }
        await db.StoredFiles.Where(f => f.Id == a.CvFileId).ExecuteUpdateAsync(u => u.SetProperty(f => f.Content, Array.Empty<byte>())
            .SetProperty(f => f.SizeBytes, 0L).SetProperty(f => f.FileName, "removed").SetProperty(f => f.Sha256, ""), ct);
        // Test answers: the text and uploaded files go, the marks stay.
        var questions = db.AttemptQuestions.Where(q => db.Attempts.Where(t => t.Invitation!.JobApplicationId == appId)
            .SelectMany(t => t.Sections).Select(s => s.Id).Contains(q.AttemptSectionId));
        var files = await questions.Where(q => q.AnswerFileId != null).Select(q => q.AnswerFileId!.Value).ToListAsync(ct);
        await questions.ExecuteUpdateAsync(u => u.SetProperty(q => q.AnswerText, q => q.AnswerText == null ? null : "")
            .SetProperty(q => q.AnswerFileName, q => q.AnswerFileName == null ? null : "removed").SetProperty(q => q.MarkerComment, "")
            .SetProperty(q => q.SuggestionReason, ""), ct);
        if (files.Count > 0)
            await db.StoredFiles.Where(f => files.Contains(f.Id)).ExecuteUpdateAsync(u => u.SetProperty(f => f.Content, Array.Empty<byte>())
                .SetProperty(f => f.SizeBytes, 0L).SetProperty(f => f.FileName, "removed").SetProperty(f => f.Sha256, ""), ct);
        await db.Interviews.Where(i => i.JobApplicationId == appId).ExecuteUpdateAsync(u => u.SetProperty(i => i.Notes, ""), ct);
        await db.InterviewFeedback.Where(f => db.Interviews.Any(i => i.Id == f.InterviewId && i.JobApplicationId == appId))
            .ExecuteUpdateAsync(u => u.SetProperty(f => f.Comments, Removed), ct);
        await db.JobOffers.Where(o => o.JobApplicationId == appId).ExecuteUpdateAsync(u => u.SetProperty(o => o.Terms, ""), ct);
        await db.EmailMessages.Where(m => m.ToAddress == email).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        // The audit entries of this application (including the one just written) held the personal data as it changed.
        var key = appId.ToString(CultureInfo.InvariantCulture);
        await db.AuditEntries.Where(e => e.EntityName == nameof(JobApplication) && e.EntityKey == key)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.Changes, "{\"personal_data\":{\"to\":\"removed\"}}"), ct);
    }
}
