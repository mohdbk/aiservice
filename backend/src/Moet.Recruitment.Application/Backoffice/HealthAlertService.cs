using System.Net;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record HealthAlertInput(bool IsEnabled, string? ExtraRecipients);

/// <summary>
/// Checks the system's health every few minutes and emails the system administrators (and any extra addresses) when
/// something fails, again each day while it lasts, and once when all is back to normal.
/// </summary>
public sealed class HealthAlertService(IAppDbContext db, IClock clock, MailConfig mail)
{
    /// <summary>A problem: a key (stable, for comparing runs) and the text in each language.</summary>
    public sealed record Problem(string Key, string En, string Ar);

    public async Task<object> GetAsync(CancellationToken ct = default)
    {
        var s = await db.HealthAlertSettings.AsNoTracking().FirstAsync(ct);
        var admins = await db.StaffUsers.AsNoTracking().Where(u => u.IsEnabled && u.Role == StaffRole.Administrator).Select(u => u.Email).ToListAsync(ct);
        return new
        {
            is_enabled = s.IsEnabled, extra_recipients = s.ExtraRecipients, administrators = admins, email_configured = await mail.IsConfiguredAsync(ct),
            last_alert_at = s.LastAlertAt, open_problems = s.LastProblems.Split(',', StringSplitOptions.RemoveEmptyEntries),
        };
    }

    public async Task<object> SaveAsync(HealthAlertInput input, CancellationToken ct = default)
    {
        var s = await db.HealthAlertSettings.FirstAsync(ct);
        s.IsEnabled = input.IsEnabled;
        s.ExtraRecipients = TextUtil.Clip(input.ExtraRecipients?.Trim(), 2000);
        if (s.ExtraRecipients.Split([',', ';', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries).Any(e => !e.Contains('@')))
            throw DomainException.Invalid("invalid_value", new Dictionary<string, object?> { ["field"] = "extra_recipients" });
        s.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    /// <summary>What is wrong right now (empty when all is well). Thresholds match the health page.</summary>
    public async Task<List<Problem>> ProblemsAsync(IEnumerable<Problem>? extra, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var list = new List<Problem>();
        var oldestQueued = await db.BackgroundJobs.AsNoTracking().Where(j => j.Status == BackgroundJobStatus.Queued && j.RunAfter <= now)
            .OrderBy(j => j.RunAfter).Select(j => (DateTime?)j.RunAfter).FirstOrDefaultAsync(ct);
        if (oldestQueued is { } q && now - q > TimeSpan.FromMinutes(15))
            list.Add(new("jobs_stuck", $"Background jobs are waiting ({(int)(now - q).TotalMinutes} minutes for the oldest).",
                $"المهام الخلفية متأخرة (أقدمها ينتظر منذ {(int)(now - q).TotalMinutes} دقيقة)."));
        var failedJobs = await db.BackgroundJobs.AsNoTracking().CountAsync(j => j.Status == BackgroundJobStatus.Failed && j.CreatedAt >= now.AddDays(-1), ct);
        if (failedJobs > 0) list.Add(new("jobs_failed", $"{failedJobs} background jobs failed in the last day.", $"فشلت {failedJobs} مهام خلفية خلال آخر يوم."));
        var failedEmails = await db.EmailMessages.AsNoTracking().CountAsync(e => e.Status == EmailStatus.Failed && e.CreatedAt >= now.AddDays(-1), ct);
        if (failedEmails > 0) list.Add(new("email_failed", $"{failedEmails} emails could not be sent in the last day.", $"تعذر إرسال {failedEmails} رسائل بريد خلال آخر يوم."));
        var cvFailed = await db.CvAnalyses.AsNoTracking().CountAsync(c => c.Status == CvAnalysisStatus.Failed && c.UpdatedAt >= now.AddDays(-1), ct);
        if (cvFailed > 0) list.Add(new("cv_failed", $"{cvFailed} CVs could not be scored in the last day.", $"تعذر تقييم {cvFailed} سير ذاتية خلال آخر يوم."));
        if (extra != null) list.AddRange(extra);
        return list.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>Runs one check and queues the alert or the all-clear when due. Returns true when an email was queued.</summary>
    public async Task<bool> CheckAsync(IEnumerable<Problem>? extra, CancellationToken ct = default)
    {
        var s = await db.HealthAlertSettings.FirstAsync(ct);
        if (!s.IsEnabled || !await mail.IsConfiguredAsync(ct)) return false;
        var now = clock.UtcNow;
        var problems = await ProblemsAsync(extra, ct);
        var keys = string.Join(",", problems.Select(p => p.Key));
        if (!s.ShouldAlert(keys, now)) return false;
        var to = (await db.StaffUsers.AsNoTracking().Where(u => u.IsEnabled && u.Role == StaffRole.Administrator).Select(u => u.Email).ToListAsync(ct))
            .Concat(s.Extra()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (to.Count > 0)
        {
            var ok = problems.Count == 0;
            var subject = ok ? "نظام التوظيف: عادت الأمور إلى طبيعتها | Recruitment System: All Clear" : "نظام التوظيف: تنبيه | Recruitment System: Alert";
            string Items(Func<Problem, string> f) => string.Join("", problems.Select(p => $"<li>{WebUtility.HtmlEncode(f(p))}</li>"));
            var html =
                $"<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\"><p>{(ok ? "تم حل جميع المشكلات السابقة." : "رصد فحص الحالة ما يلي:")}</p>{(ok ? "" : $"<ul>{Items(p => p.Ar)}</ul>")}<p>راجع صفحة حالة النظام في نظام التوظيف.</p></div>" +
                $"<hr><div dir=\"ltr\" style=\"font-family:Arial,sans-serif\"><p>{(ok ? "All earlier problems are resolved." : "The health check found:")}</p>{(ok ? "" : $"<ul>{Items(p => p.En)}</ul>")}<p>See System Health in the recruitment backoffice.</p></div>";
            var text = (ok ? "All earlier problems are resolved." : "The health check found:\n" + string.Join("\n", problems.Select(p => "- " + p.En))) + "\n\n" +
                (ok ? "تم حل جميع المشكلات السابقة." : "رصد فحص الحالة ما يلي:\n" + string.Join("\n", problems.Select(p => "- " + p.Ar)));
            foreach (var address in to)
                db.EmailMessages.Add(new EmailMessage
                {
                    ToAddress = address, Subject = subject, HtmlBody = html, TextBody = text, Status = EmailStatus.Queued, NextAttemptAt = now, CreatedAt = now,
                });
        }
        s.LastProblems = keys;
        s.LastAlertAt = now;
        await db.SaveChangesAsync(ct);
        return to.Count > 0;
    }
}
