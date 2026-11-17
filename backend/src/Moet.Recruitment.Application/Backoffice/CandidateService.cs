using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Application.Portal;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Identity;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Backoffice;

/// <summary>
/// Candidate accounts (people signed up on the careers portal), for recruitment admins: find an account, see its
/// applications, help with access (verification email, password reset link, verifying the email by hand), disable or
/// enable it, and start a deletion request (handled on the Data Protection page).
/// </summary>
public sealed class CandidateService(IAppDbContext db, PortalAccountService accounts, PortalMailer mailer, IClock clock, IRequestLanguage language)
{
    public async Task<object> ListAsync(ListQuery lq, bool? verified, bool? enabled, bool? talentPool, CancellationToken ct = default)
    {
        var q = db.ApplicantAccounts.AsNoTracking().Where(u => u.AnonymisedAt == null);
        if (verified != null) q = q.Where(u => u.IsEmailVerified == verified);
        if (enabled != null) q = q.Where(u => u.IsEnabled == enabled);
        if (talentPool != null) q = q.Where(u => u.InTalentPool == talentPool);
        if (lq.Term.Length > 0) q = q.Where(u => u.FullName.Contains(lq.Term) || u.Email.Contains(lq.Term));
        var sorts = new Sorts<ApplicantAccount>("joined").Add("joined", u => u.CreatedAt).Add("name", u => u.FullName).Add("email", u => u.Email)
            .Add("last_sign_in", u => u.LastSignInAt);
        return await q.PageAsync(lq, sorts, u => new
        {
            id = u.Id, full_name = u.FullName, email = u.Email, verified = u.IsEmailVerified, enabled = u.IsEnabled, talent_pool = u.InTalentPool,
            joined_at = u.CreatedAt, last_sign_in_at = u.LastSignInAt, language = u.PreferredLanguageCode,
            locked = u.LockedUntil != null && u.LockedUntil > DateTime.UtcNow,
            applications = db.JobApplications.Count(a => a.ApplicantAccountId == u.Id),
        }, ct);
    }

    public async Task<object> GetAsync(int id, CancellationToken ct = default)
    {
        var u = await db.ApplicantAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        var en = language.English;
        var apps = await db.JobApplications.AsNoTracking().Where(a => a.ApplicantAccountId == id).OrderByDescending(a => a.CreatedAt).Select(a => new
        {
            id = a.Id, code = a.Code, status = a.Status.ToString(), created_at = a.CreatedAt,
            job_title = en ? a.JobOpening!.Translations.Where(t => t.LanguageCode == "en" && t.Title != "").Select(t => t.Title).FirstOrDefault() ?? a.JobOpening.Title : a.JobOpening!.Title,
        }).ToListAsync(ct);
        var requests = await db.DataRequests.AsNoTracking().Where(r => r.ApplicantAccountId == id).OrderByDescending(r => r.CreatedAt)
            .Select(r => new { id = r.Id, kind = r.Kind.ToString(), status = r.Status.ToString(), created_at = r.CreatedAt }).ToListAsync(ct);
        var emails = await db.EmailMessages.AsNoTracking().Where(m => m.ToAddress == u.Email).OrderByDescending(m => m.CreatedAt).Take(10)
            .Select(m => new { subject = m.Subject, status = m.Status.ToString(), created_at = m.CreatedAt, sent_at = m.SentAt, error = m.LastError }).ToListAsync(ct);
        return new
        {
            id = u.Id, full_name = u.FullName, email = u.Email, verified = u.IsEmailVerified, enabled = u.IsEnabled, anonymised = u.AnonymisedAt != null,
            talent_pool = u.InTalentPool, joined_at = u.CreatedAt, last_sign_in_at = u.LastSignInAt, language = u.PreferredLanguageCode,
            locked_until = u.LockedUntil > clock.UtcNow ? u.LockedUntil : null, email_configured = await mailer.CanSendAsync(ct),
            applications = apps, data_requests = requests, emails,
        };
    }

    /// <summary>
    /// Accounts that look like the same person: the same name (ignoring case and spaces) or the same phone number on
    /// their applications. People decide whether to merge.
    /// </summary>
    public async Task<object> DuplicatesAsync(CancellationToken ct = default)
    {
        var accounts = await db.ApplicantAccounts.AsNoTracking().Where(u => u.AnonymisedAt == null)
            .Select(u => new { u.Id, u.FullName, u.Email, u.CreatedAt, u.IsEnabled }).ToListAsync(ct);
        var phones = await db.JobApplications.AsNoTracking().Where(a => a.Phone != "")
            .Select(a => new { a.ApplicantAccountId, a.Phone }).Distinct().ToListAsync(ct);
        var apps = await db.JobApplications.AsNoTracking().GroupBy(a => a.ApplicantAccountId).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(ct);
        static string Name(string s) => string.Join(" ", s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        static string Digits(string s) { var d = new string(s.Where(char.IsDigit).ToArray()); return d.Length > 9 ? d[^9..] : d; }
        var byName = accounts.Where(u => u.IsEnabled && Name(u.FullName).Length > 3).GroupBy(u => Name(u.FullName)).Where(g => g.Count() > 1)
            .Select(g => (Reason: "name", Ids: g.Select(u => u.Id).ToList()));
        var byPhone = phones.Where(p => Digits(p.Phone).Length >= 7).GroupBy(p => Digits(p.Phone))
            .Select(g => g.Select(p => p.ApplicantAccountId).Distinct().ToList()).Where(ids => ids.Count > 1)
            .Select(ids => (Reason: "phone", Ids: ids));
        var seen = new HashSet<string>();
        var groups = new List<object>();
        foreach (var (reason, ids) in byName.Concat(byPhone))
        {
            var members = accounts.Where(u => ids.Contains(u.Id) && u.IsEnabled).OrderBy(u => u.CreatedAt).ToList();
            if (members.Count < 2 || !seen.Add(string.Join(",", members.Select(m => m.Id)))) continue;
            groups.Add(new
            {
                reason,
                accounts = members.Select(m => new { id = m.Id, full_name = m.FullName, email = m.Email, joined_at = m.CreatedAt, applications = apps.FirstOrDefault(a => a.Id == m.Id)?.N ?? 0 }),
            });
            if (groups.Count >= 100) break;
        }
        return new { groups };
    }

    /// <summary>
    /// Merges a duplicate account into the one kept: its applications, job alerts, consents and data requests move over,
    /// a note goes on each moved application, and the duplicate is switched off (signed out, no further sign-in).
    /// Refused when both applied to the same job, since an application belongs to one account.
    /// </summary>
    public async Task<object> MergeAsync(int keepId, int fromId, CancellationToken ct = default)
    {
        if (keepId == fromId) throw DomainException.Invalid("merge_same_account");
        var keep = await AccountAsync(keepId, ct);
        var from = await AccountAsync(fromId, ct);
        if (keep.AnonymisedAt != null || from.AnonymisedAt != null) throw DomainException.Conflict("merge_anonymised");
        var keepJobs = await db.JobApplications.Where(a => a.ApplicantAccountId == keepId).Select(a => a.JobOpeningId).ToListAsync(ct);
        var moving = await db.JobApplications.Where(a => a.ApplicantAccountId == fromId).ToListAsync(ct);
        if (moving.Any(a => keepJobs.Contains(a.JobOpeningId))) throw DomainException.Conflict("merge_same_job");
        var now = clock.UtcNow;
        foreach (var a in moving)
        {
            a.ApplicantAccountId = keepId;
            a.UpdatedAt = now;
            db.ApplicationNotes.Add(new ApplicationNote
            {
                JobApplicationId = a.Id, Kind = NoteKind.System, CreatedAt = now,
                Text = TextUtil.Clip($"Moved from the duplicate account {from.Email} when it was merged into {keep.Email}.", 4000),
            });
        }
        foreach (var x in await db.JobAlerts.Where(x => x.ApplicantAccountId == fromId).ToListAsync(ct)) x.ApplicantAccountId = keepId;
        foreach (var x in await db.ConsentRecords.Where(x => x.ApplicantAccountId == fromId).ToListAsync(ct)) x.ApplicantAccountId = keepId;
        foreach (var x in await db.DataRequests.Where(x => x.ApplicantAccountId == fromId).ToListAsync(ct)) x.ApplicantAccountId = keepId;
        foreach (var x in await db.ApplicationNotes.Where(n => n.AuthorApplicantId == fromId).ToListAsync(ct)) x.AuthorApplicantId = keepId;
        db.AccountTokens.RemoveRange(await db.AccountTokens.Where(t => t.ApplicantAccountId == fromId).ToListAsync(ct));
        if (from.InTalentPool && !keep.InTalentPool) { keep.InTalentPool = true; keep.TalentPoolAt = from.TalentPoolAt ?? now; }
        from.IsEnabled = false;
        from.SessionVersion++;
        await db.SaveChangesAsync(ct);
        return new { moved = moving.Count };
    }

    async Task<ApplicantAccount> AccountAsync(int id, CancellationToken ct)
    {
        var u = await db.ApplicantAccounts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        if (u.AnonymisedAt != null) throw DomainException.Conflict("anonymised");
        return u;
    }

    public async Task<object> ResendVerificationAsync(int id, CancellationToken ct = default)
    {
        var u = await AccountAsync(id, ct);
        if (u.IsEmailVerified) throw DomainException.Conflict("already_verified");
        if (!await mailer.CanSendAsync(ct)) throw DomainException.Conflict("email_not_configured");
        await accounts.ResendVerificationAsync(u.Email, u.PreferredLanguageCode, "", ct);
        return new { sent = true };
    }

    public async Task<object> SendPasswordResetAsync(int id, CancellationToken ct = default)
    {
        var u = await AccountAsync(id, ct);
        if (!u.IsEnabled) throw DomainException.Conflict("account_disabled");
        if (!await mailer.CanSendAsync(ct)) throw DomainException.Conflict("email_not_configured");
        await accounts.RequestPasswordResetAsync(u.Email, u.PreferredLanguageCode, ct);
        return new { sent = true };
    }

    /// <summary>When the verification email cannot reach the candidate and HR has confirmed the address another way.</summary>
    public async Task<object> MarkVerifiedAsync(int id, CancellationToken ct = default)
    {
        var u = await AccountAsync(id, ct);
        u.IsEmailVerified = true;
        await db.SaveChangesAsync(ct);
        return new { verified = true };
    }

    /// <summary>Disabling signs the candidate out everywhere and stops sign-in; their applications stay. Enabling also clears a lockout.</summary>
    public async Task<object> SetEnabledAsync(int id, bool enabled, CancellationToken ct = default)
    {
        var u = await AccountAsync(id, ct);
        u.IsEnabled = enabled;
        if (!enabled) u.SessionVersion++;
        else { u.FailedSignIns = 0; u.LockedUntil = null; }
        await db.SaveChangesAsync(ct);
        return new { enabled = u.IsEnabled };
    }

    /// <summary>Opens a deletion request on the candidate's behalf (for example after a phone call); it is decided on the Data Protection page.</summary>
    public async Task<object> RequestDeletionAsync(int id, string? reason, CancellationToken ct = default)
    {
        var u = await AccountAsync(id, ct);
        if (await db.DataRequests.AnyAsync(r => r.ApplicantAccountId == u.Id && r.Kind == DataRequestKind.Deletion && r.Status == DataRequestStatus.Open, ct))
            return new { requested = true, already = true };
        db.DataRequests.Add(new DataRequest
        {
            ApplicantAccountId = u.Id, Kind = DataRequestKind.Deletion, Requester = $"{u.FullName} <{u.Email}>",
            Reason = TextUtil.NoEmDash(TextUtil.Clip(string.IsNullOrWhiteSpace(reason) ? "Recorded by HR" : reason.Trim(), 2000)), CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return new { requested = true, already = false };
    }
}

public sealed record EnabledBody(bool Enabled);
public sealed record MergeBody(int KeepId);
public sealed record ReasonBody(string? Reason);
