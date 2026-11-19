using Microsoft.EntityFrameworkCore;
using Moet.Hrms.Application.Common;
using Moet.Hrms.Domain;
using Moet.Hrms.Domain.Common;
using Moet.Hrms.Domain.Hiring;

namespace Moet.Hrms.Application.Backoffice;

/// <summary>
/// Candidates who agreed to be considered for other jobs, with their latest application and CV summary, and inviting them to
/// apply for a job (an email with the job link).
/// </summary>
public sealed class TalentPoolService(IAppDbContext db, IRequestLanguage language, PortalMailer mailer, ContentService content)
{
    public async Task<object> ListAsync(ListQuery lq, string? cvText, int? notAppliedTo, CancellationToken ct = default)
    {
        var q = db.ApplicantAccounts.AsNoTracking().Where(u => u.InTalentPool && u.AnonymisedAt == null && u.IsEnabled);
        if (lq.Term.Length > 0) q = q.Where(u => u.FullName.Contains(lq.Term) || u.Email.Contains(lq.Term));
        if (!string.IsNullOrWhiteSpace(cvText))
        {
            var t = cvText.Trim();
            q = q.Where(u => db.JobApplications.Any(a => a.ApplicantAccountId == u.Id && (a.CvAnalysis!.Summary.Contains(t) || a.CvAnalysis.ProfileJson!.Contains(t)))
                || db.CandidateCvs.Any(c => c.ApplicantAccountId == u.Id && (c.Summary.Contains(t) || c.Text.Contains(t) || c.ProfileJson!.Contains(t))));
        }
        if (notAppliedTo != null) q = q.Where(u => !db.JobApplications.Any(a => a.ApplicantAccountId == u.Id && a.JobOpeningId == notAppliedTo));
        var en = language.English;
        var sorts = new Sorts<Domain.Identity.ApplicantAccount>("joined").Add("joined", u => u.TalentPoolAt).Add("name", u => u.FullName);
        return await q.PageAsync(lq, sorts, u => new
        {
            id = u.Id, full_name = u.FullName, email = u.Email, joined_at = u.TalentPoolAt, language = u.PreferredLanguageCode,
            applications = db.JobApplications.Count(a => a.ApplicantAccountId == u.Id),
            // Their own CV (from their profile), so people who never applied can be found and judged too.
            cv = db.CandidateCvs.Where(c => c.ApplicantAccountId == u.Id).OrderByDescending(c => c.IsDefault).ThenByDescending(c => c.UploadedAt)
                .Select(c => new { id = c.Id, file_name = c.FileName, years = c.YearsOfExperience, summary = c.Summary, uploaded_at = c.UploadedAt, status = c.Status }).FirstOrDefault(),
            cvs = db.CandidateCvs.Count(c => c.ApplicantAccountId == u.Id),
            latest = db.JobApplications.Where(a => a.ApplicantAccountId == u.Id).OrderByDescending(a => a.CreatedAt).Select(a => new
            {
                id = a.Id, created_at = a.CreatedAt, status = a.Status,
                job_title = en ? a.JobOpening!.Translations.Where(t => t.LanguageCode == "en" && t.Title != "").Select(t => t.Title).FirstOrDefault() ?? a.JobOpening.Title : a.JobOpening!.Title,
                score = a.CvAnalysis == null ? null : a.CvAnalysis.Score, years = a.CvAnalysis == null ? null : a.CvAnalysis.YearsOfExperience,
                summary = a.CvAnalysis == null ? "" : en || a.CvAnalysis.SummaryArabic == "" ? a.CvAnalysis.Summary : a.CvAnalysis.SummaryArabic,
            }).FirstOrDefault(),
        }, ct);
    }

    /// <summary>Emails the candidate an invitation to apply for an open, published job. False when email is off or not set up.</summary>
    public async Task<object> InviteAsync(int accountId, int jobId, CancellationToken ct = default)
    {
        var u = await db.ApplicantAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId && x.InTalentPool && x.AnonymisedAt == null, ct)
            ?? throw DomainException.NotFound();
        var job = await db.JobOpenings.AsNoTracking().Include(j => j.Translations).FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw DomainException.NotFound();
        if (!job.IsListed(DateOnly.FromDateTime(DateTime.UtcNow))) throw DomainException.Conflict("job_not_open");
        if (await db.JobApplications.AnyAsync(a => a.ApplicantAccountId == u.Id && a.JobOpeningId == jobId, ct)) throw DomainException.Conflict("already_applied");
        var lang = await content.PortalLanguageAsync(u.PreferredLanguageCode ?? "ar", ct);
        var link = await mailer.LinkAsync(lang, $"/jobs/{job.Code}", ct);
        var emailed = await mailer.QueueAsync(u.Email, u.FullName, lang, "talent_invite", link, new Dictionary<string, string> { ["job_title"] = job.In(lang).Title }, ct);
        if (emailed) await db.SaveChangesAsync(ct);
        return new { emailed };
    }
}
