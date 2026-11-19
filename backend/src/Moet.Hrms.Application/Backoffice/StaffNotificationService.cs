using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Moet.Hrms.Application.Common;
using Moet.Hrms.Domain;
using Moet.Hrms.Domain.Common;
using Moet.Hrms.Domain.Hiring;
using Moet.Hrms.Domain.Identity;
using Moet.Hrms.Domain.Operations;

namespace Moet.Hrms.Application.Backoffice;

public sealed record NotificationPrefs(bool DailyDigest, bool WeeklyReport);

/// <summary>
/// What needs a staff member's attention (the notifications bell): their interviews in the next day, feedback they
/// owe, approvals waiting (for those who decide them), answers to mark, new applications and replies to offers.
/// The same list goes out as a morning digest email, and a weekly hiring summary on Mondays, for those who ask.
/// </summary>
public sealed class StaffNotificationService(IAppDbContext db, IClock clock, ICurrentActor actor, MailConfig mail)
{
    public sealed record Item(string Kind, string Name, DateTime At, string Link, int Count = 1);

    public async Task<object> ListAsync(CancellationToken ct = default)
    {
        if (actor.Id is not { } me) throw DomainException.Forbidden("staff_only");
        var u = await db.StaffUsers.AsNoTracking().FirstAsync(x => x.Id == me, ct);
        var items = await ItemsAsync(u, ct);
        return new
        {
            items = items.Select(i => new { kind = i.Kind, name = i.Name, at = i.At, link = i.Link, count = i.Count }),
            daily_digest = u.DailyDigest, weekly_report = u.WeeklyReport, email = await mail.IsConfiguredAsync(ct),
        };
    }

    public async Task<object> SavePrefsAsync(NotificationPrefs input, CancellationToken ct = default)
    {
        if (actor.Id is not { } me) throw DomainException.Forbidden("staff_only");
        var u = await db.StaffUsers.FirstAsync(x => x.Id == me, ct);
        u.DailyDigest = input.DailyDigest;
        u.WeeklyReport = input.WeeklyReport;
        u.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { daily_digest = u.DailyDigest, weekly_report = u.WeeklyReport };
    }

    public async Task<List<Item>> ItemsAsync(StaffUser u, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var list = new List<Item>();
        var soon = await db.Interviews.AsNoTracking()
            .Where(i => i.Status == InterviewStatus.Scheduled && i.StartsAt >= now && i.StartsAt <= now.AddDays(1) && i.Panelists.Any(p => p.StaffUserId == u.Id))
            .OrderBy(i => i.StartsAt).Take(10)
            .Select(i => new { i.Id, i.StartsAt, i.CreatedAt, Name = i.JobApplication!.FullName }).ToListAsync(ct);
        list.AddRange(soon.Select(i => new Item("interview_soon", $"{i.Name}|{clock.Local(i.StartsAt):HH:mm}", i.CreatedAt > now.AddDays(-1) ? i.CreatedAt : i.StartsAt.AddDays(-1), $"/interviews/{i.Id}")));
        var owed = await db.Interviews.AsNoTracking()
            .Where(i => i.EndsAt < now && i.Status != InterviewStatus.Cancelled && i.Panelists.Any(p => p.StaffUserId == u.Id) && !i.Feedback.Any(f => f.StaffUserId == u.Id))
            .OrderByDescending(i => i.EndsAt).Take(10)
            .Select(i => new { i.Id, i.EndsAt, Name = i.JobApplication!.FullName }).ToListAsync(ct);
        list.AddRange(owed.Select(i => new Item("feedback_due", i.Name, i.EndsAt, $"/interviews/{i.Id}")));
        if (u.CanAdminister)
        {
            var approvals = await db.ApprovalRequests.AsNoTracking().Where(r => r.Status == ApprovalStatus.Pending)
                .OrderByDescending(r => r.RequestedAt).Take(10).Select(r => new { r.Title, r.RequestedAt }).ToListAsync(ct);
            list.AddRange(approvals.Select(r => new Item("approval", r.Title, r.RequestedAt, "/approvals")));
        }
        var marking = await db.Attempts.AsNoTracking().Where(a => a.Status == AttemptStatus.Submitted)
            .OrderBy(a => a.SubmittedAt).Select(a => new { a.Id, a.SubmittedAt }).Take(50).ToListAsync(ct);
        if (marking.Count > 0) list.Add(new Item("marking", "", marking.Max(a => a.SubmittedAt) ?? now, $"/marking/{marking[0].Id}", marking.Count));
        var since = now.AddDays(-1);
        var fresh = await db.JobApplications.AsNoTracking().Where(a => a.CreatedAt >= since).GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Last = g.Max(a => a.CreatedAt) }).FirstOrDefaultAsync(ct);
        if (fresh is { Count: > 0 }) list.Add(new Item("new_applications", "", fresh.Last, "/applicants?sort=created&dir=desc", fresh.Count));
        var replies = await db.JobOffers.AsNoTracking()
            .Where(o => (o.Status == OfferStatus.Accepted || o.Status == OfferStatus.Declined) && o.UpdatedAt >= now.AddDays(-7))
            .OrderByDescending(o => o.UpdatedAt).Take(10)
            .Select(o => new { o.Status, o.UpdatedAt, Name = o.JobApplication!.FullName }).ToListAsync(ct);
        list.AddRange(replies.Select(o => new Item(o.Status == OfferStatus.Accepted ? "offer_accepted" : "offer_declined", o.Name, o.UpdatedAt, "/offers")));
        return list.OrderByDescending(i => i.At).ToList();
    }

    /// <summary>
    /// Queues the morning digests (from 7:00 in the ministry's time zone) and, on Mondays, the weekly summaries for
    /// those who switched them on. Runs every hour; each person gets each email once per day or week.
    /// </summary>
    public async Task<int> SendDueAsync(string backofficeUrl, CancellationToken ct = default)
    {
        if (!await mail.IsConfiguredAsync(ct)) return 0;
        var now = clock.UtcNow;
        var local = clock.Local(now);
        if (local.Hour < 7) return 0;
        var today = DateOnly.FromDateTime(local);
        var monday = local.DayOfWeek == DayOfWeek.Monday;
        var people = await db.StaffUsers.Where(u => u.IsEnabled && ((u.DailyDigest && (u.DigestSentOn == null || u.DigestSentOn < today))
            || (monday && u.WeeklyReport && (u.WeeklySentOn == null || u.WeeklySentOn < today)))).ToListAsync(ct);
        var sent = 0;
        object? week = null;
        foreach (var u in people)
        {
            if (u.DailyDigest && (u.DigestSentOn == null || u.DigestSentOn < today))
            {
                u.DigestSentOn = today;
                var items = await ItemsAsync(u, ct);
                if (items.Count > 0) { Queue(u.Email, "نظام إدارة الموارد البشرية: ملخص اليوم | HRMS: Today's Summary", DigestHtml(u.FullName, items, backofficeUrl), DigestText(items, backofficeUrl), now); sent++; }
            }
            if (monday && u.WeeklyReport && (u.WeeklySentOn == null || u.WeeklySentOn < today))
            {
                u.WeeklySentOn = today;
                week ??= await WeekAsync(now, ct);
                var w = (Week)week;
                Queue(u.Email, "نظام إدارة الموارد البشرية: ملخص الأسبوع | HRMS: Weekly Summary", WeekHtml(u.FullName, w, backofficeUrl), WeekText(w, backofficeUrl), now);
                sent++;
            }
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    public sealed record Week(int Applications, int Interviews, int OffersSent, int OffersAccepted, int JobsOpen);

    async Task<Week> WeekAsync(DateTime now, CancellationToken ct)
    {
        var from = now.AddDays(-7);
        return new Week(
            await db.JobApplications.CountAsync(a => a.CreatedAt >= from, ct),
            await db.Interviews.CountAsync(i => i.StartsAt >= from && i.StartsAt < now && i.Status != InterviewStatus.Cancelled, ct),
            await db.JobOffers.CountAsync(o => o.Status != OfferStatus.Draft && o.CreatedAt >= from, ct),
            await db.JobOffers.CountAsync(o => o.Status == OfferStatus.Accepted && o.UpdatedAt >= from, ct),
            await db.JobOpenings.CountAsync(j => j.Status == JobStatus.Open, ct));
    }

    void Queue(string to, string subject, string html, string text, DateTime now) =>
        db.EmailMessages.Add(new EmailMessage { ToAddress = to, Subject = subject, HtmlBody = html, TextBody = text, Status = EmailStatus.Queued, NextAttemptAt = now, CreatedAt = now });

    static readonly Dictionary<string, (string Ar, string En)> Labels = new()
    {
        ["interview_soon"] = ("مقابلة قريبة مع {0}", "Upcoming interview with {0}"),
        ["feedback_due"] = ("تقييم مطلوب لمقابلة {0}", "Feedback due for {0}'s interview"),
        ["approval"] = ("بانتظار الموافقة: {0}", "Waiting for approval: {0}"),
        ["marking"] = ("{1} إجابات بانتظار التصحيح", "{1} answers waiting to be marked"),
        ["new_applications"] = ("{1} طلبات جديدة خلال آخر 24 ساعة", "{1} new applications in the last 24 hours"),
        ["offer_accepted"] = ("قبل {0} العرض الوظيفي", "{0} accepted the job offer"),
        ["offer_declined"] = ("رفض {0} العرض الوظيفي", "{0} declined the job offer"),
    };

    static string Line(Item i, bool ar)
    {
        var (a, e) = Labels[i.Kind];
        var parts = i.Name.Split('|');
        var name = parts[0] + (parts.Length > 1 ? (ar ? $" الساعة {parts[1]}" : $" at {parts[1]}") : "");
        return string.Format(CultureInfo.InvariantCulture, ar ? a : e, name, i.Count);
    }

    static string Enc(string s) => WebUtility.HtmlEncode(s);
    static string Url(string b, string path, string lang) => b.Length == 0 ? "" : $"{b.TrimEnd('/')}/{lang}{path}";

    static string DigestHtml(string name, List<Item> items, string b)
    {
        string List(bool ar) => string.Join("", items.Select(i =>
        {
            var href = Url(b, i.Link, ar ? "ar" : "en");
            return href.Length > 0 ? $"<li><a href=\"{Enc(href)}\">{Enc(Line(i, ar))}</a></li>" : $"<li>{Enc(Line(i, ar))}</li>";
        }));
        return $"<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\"><p>{Enc(name)}، صباح الخير. هذا ما يحتاج إلى متابعتك اليوم:</p><ul>{List(true)}</ul></div>" +
               $"<hr><div dir=\"ltr\" style=\"font-family:Arial,sans-serif\"><p>Good morning {Enc(name)}. Here is what needs you today:</p><ul>{List(false)}</ul>" +
               "<p style=\"color:#666;font-size:12px\">You can switch this email off from the notifications bell in the backoffice.</p></div>";
    }

    static string DigestText(List<Item> items, string b) =>
        "Here is what needs you today:\n" + string.Join("\n", items.Select(i => "- " + Line(i, false))) + (b.Length > 0 ? $"\n\n{Url(b, "/", "en")}" : "") +
        "\n\nهذا ما يحتاج إلى متابعتك اليوم:\n" + string.Join("\n", items.Select(i => "- " + Line(i, true)));

    static string WeekHtml(string name, Week w, string b)
    {
        string Rows(bool ar) => string.Join("", new (string, string, int)[]
        {
            ("طلبات جديدة", "New applications", w.Applications), ("مقابلات أجريت", "Interviews held", w.Interviews),
            ("عروض أرسلت", "Offers sent", w.OffersSent), ("عروض قبلت", "Offers accepted", w.OffersAccepted), ("وظائف منشورة", "Jobs open", w.JobsOpen),
        }.Select(r => $"<tr><td style=\"padding:4px 12px 4px 0\">{(ar ? r.Item1 : r.Item2)}</td><td style=\"padding:4px 0;font-weight:bold\">{r.Item3}</td></tr>"));
        var link = Url(b, "/reports", "en");
        return $"<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\"><p>{Enc(name)}، هذا ملخص التوظيف لآخر 7 أيام:</p><table>{Rows(true)}</table></div>" +
               $"<hr><div dir=\"ltr\" style=\"font-family:Arial,sans-serif\"><p>{Enc(name)}, here is hiring over the last 7 days:</p><table>{Rows(false)}</table>" +
               (link.Length > 0 ? $"<p><a href=\"{Enc(link)}\">Open the Hiring Report</a></p>" : "") + "</div>";
    }

    static string WeekText(Week w, string b) =>
        $"Hiring over the last 7 days:\n- New applications: {w.Applications}\n- Interviews held: {w.Interviews}\n- Offers sent: {w.OffersSent}\n- Offers accepted: {w.OffersAccepted}\n- Jobs open: {w.JobsOpen}" +
        (b.Length > 0 ? $"\n\n{Url(b, "/reports", "en")}" : "");
}
