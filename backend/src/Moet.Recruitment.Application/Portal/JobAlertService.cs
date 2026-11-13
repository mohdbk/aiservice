using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Identity;

namespace Moet.Recruitment.Application.Portal;

/// <summary>
/// Candidates' job alerts (saved searches, up to five each) and the hourly run that emails each alert the newly published
/// jobs that match it, once per job.
/// </summary>
public sealed class JobAlertService(IAppDbContext db, IClock clock, PortalMailer mailer, ContentService content)
{
    public async Task<object> ListAsync(ApplicantAccount user, string? lang, CancellationToken ct = default)
    {
        var l = lang == "en" ? "en" : "ar";
        var alerts = await db.JobAlerts.AsNoTracking().Where(a => a.ApplicantAccountId == user.Id).OrderBy(a => a.Id).ToListAsync(ct);
        var depIds = alerts.Where(a => a.DepartmentId != null).Select(a => a.DepartmentId!.Value).ToList();
        var locIds = alerts.Where(a => a.LocationId != null).Select(a => a.LocationId!.Value).ToList();
        var deps = await db.Departments.AsNoTracking().Include(d => d.Translations).Where(d => depIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
        var locs = await db.JobLocations.AsNoTracking().Include(x => x.Translations).Where(x => locIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return new
        {
            alerts = alerts.Select(a => new
            {
                id = a.Id, keywords = a.Keywords, department_id = a.DepartmentId, location_id = a.LocationId,
                department = a.DepartmentId is { } d && deps.TryGetValue(d, out var dep) ? dep.NameIn(l) : "",
                location = a.LocationId is { } x && locs.TryGetValue(x, out var loc) ? loc.NameIn(l) : "",
                created = a.CreatedAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            }),
            max = JobAlert.MaxPerAccount,
            departments = (await db.Departments.AsNoTracking().Include(d => d.Translations).Where(d => d.IsActive).ToListAsync(ct))
                .Select(d => new { id = d.Id, name = d.NameIn(l) }).OrderBy(d => d.name),
            locations = (await db.JobLocations.AsNoTracking().Include(x => x.Translations).ToListAsync(ct))
                .Select(x => new { id = x.Id, name = x.NameIn(l) }).OrderBy(x => x.name),
        };
    }

    public async Task<object> SaveAsync(ApplicantAccount user, string? keywords, int? departmentId, int? locationId, string? lang, CancellationToken ct = default)
    {
        var words = TextUtil.NoEmDash(TextUtil.Clip(keywords?.Trim(), 300));
        if (words.Length == 0 && departmentId == null && locationId == null) throw DomainException.Invalid("alert_empty");
        if (await db.JobAlerts.CountAsync(a => a.ApplicantAccountId == user.Id, ct) >= JobAlert.MaxPerAccount)
            throw DomainException.Conflict("too_many_alerts");
        if (departmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d, ct)) departmentId = null;
        if (locationId is { } l && !await db.JobLocations.AnyAsync(x => x.Id == l, ct)) locationId = null;
        var alert = new JobAlert
        {
            ApplicantAccountId = user.Id, Keywords = words, DepartmentId = departmentId, LocationId = locationId,
            LanguageCode = lang == "en" ? "en" : "ar", CreatedAt = clock.UtcNow,
        };
        db.JobAlerts.Add(alert);
        await db.SaveChangesAsync(ct);
        // Jobs already open when the alert is made are not sent: the candidate has just seen them.
        foreach (var job in await MatchingOpenJobsAsync(alert, ct))
            db.JobAlertSents.Add(new JobAlertSent { JobAlertId = alert.Id, JobOpeningId = job.Id, SentAt = clock.UtcNow });
        await db.SaveChangesAsync(ct);
        return new { id = alert.Id };
    }

    public async Task<object> DeleteAsync(ApplicantAccount user, int id, CancellationToken ct = default)
    {
        var a = await db.JobAlerts.FirstOrDefaultAsync(x => x.Id == id && x.ApplicantAccountId == user.Id, ct) ?? throw DomainException.NotFound();
        db.JobAlerts.Remove(a);
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    async Task<List<JobOpening>> OpenJobsAsync(CancellationToken ct)
    {
        var today = clock.Today();
        return await db.JobOpenings.AsNoTracking().Include(j => j.Translations)
            .Where(j => j.IsPublished && j.Status == JobStatus.Open && (j.ClosesOn == null || j.ClosesOn >= today))
            .OrderByDescending(j => j.CreatedAt).Take(500).ToListAsync(ct);
    }

    static IEnumerable<string> Texts(JobOpening j) =>
        new[] { j.Title, TextUtil.StripHtml(j.Description) }
            .Concat(j.Translations.Where(t => t.ReviewStatus == ReviewStatus.Approved).SelectMany(t => new[] { t.Title, TextUtil.StripHtml(t.Description) }));

    async Task<List<JobOpening>> MatchingOpenJobsAsync(JobAlert alert, CancellationToken ct) =>
        (await OpenJobsAsync(ct)).Where(j => alert.Matches(j.DepartmentId, j.LocationId, Texts(j))).ToList();

    /// <summary>Emails every alert the open jobs that match it and were not sent before. Returns the number of emails queued.</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var jobs = await OpenJobsAsync(ct);
        if (jobs.Count == 0) return 0;
        var alerts = await db.JobAlerts.Join(db.ApplicantAccounts, a => a.ApplicantAccountId, u => u.Id, (a, u) => new { Alert = a, User = u })
            .Where(x => x.User.IsEnabled && x.User.AnonymisedAt == null).ToListAsync(ct);
        if (alerts.Count == 0) return 0;
        var alertIds = alerts.Select(x => x.Alert.Id).ToList();
        var sent = (await db.JobAlertSents.AsNoTracking().Where(s => alertIds.Contains(s.JobAlertId)).Select(s => new { s.JobAlertId, s.JobOpeningId }).ToListAsync(ct))
            .Select(s => (s.JobAlertId, s.JobOpeningId)).ToHashSet();
        var now = clock.UtcNow;
        var emails = 0;
        foreach (var x in alerts)
        {
            var fresh = jobs.Where(j => !sent.Contains((x.Alert.Id, j.Id)) && x.Alert.Matches(j.DepartmentId, j.LocationId, Texts(j))).Take(10).ToList();
            if (fresh.Count == 0) continue;
            var lang = await content.PortalLanguageAsync(x.User.PreferredLanguageCode ?? x.Alert.LanguageCode, ct);
            var l = lang == "ar" ? "ar" : "en";
            var lines = new List<string>();
            foreach (var j in fresh) lines.Add($"{j.In(l).Title}: {await mailer.LinkAsync(lang, $"/jobs/{j.Code}", ct)}");
            var link = await mailer.LinkAsync(lang, "/jobs", ct);
            if (await mailer.QueueAsync(x.User.Email, x.User.FullName, lang, "job_alert", link, new Dictionary<string, string> { ["jobs"] = string.Join("\n", lines) }, ct)) emails++;
            // Marked as sent even when email is off, so turning email on later does not send a backlog.
            foreach (var j in fresh) db.JobAlertSents.Add(new JobAlertSent { JobAlertId = x.Alert.Id, JobOpeningId = j.Id, SentAt = now });
            x.Alert.LastSentAt = now;
        }
        await db.SaveChangesAsync(ct);
        return emails;
    }
}
