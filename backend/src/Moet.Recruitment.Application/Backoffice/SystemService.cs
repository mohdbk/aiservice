using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Backoffice;

/// <summary>System administration pages: the audit log, assistant usage with per-person limits, and the health of the service.</summary>
public sealed class SystemService(IAppDbContext db, IClock clock, MailConfig mail, ICurrentActor actor, IVirusScanner scanner)
{
    static readonly Sorts<AuditEntry> AuditSorts = new Sorts<AuditEntry>("when").Add("when", a => a.OccurredAt).Add("record", a => a.EntityName)
        .Add("action", a => a.Action).Add("by", a => a.ActorName);

    public async Task<object> AuditAsync(ListQuery lq, string? record, string? action, string? key, DateOnly? from, DateOnly? to, CancellationToken ct = default,
        string? via = null, string? kind = null, int? staff = null, string? request = null)
    {
        var q = db.AuditEntries.AsNoTracking().AsQueryable();
        if (via == "Assistant") q = q.Where(a => a.Via == "Assistant");
        if (kind == "changes") q = q.Where(a => a.Action != AuditAction.Action);
        if (kind == "actions") q = q.Where(a => a.Action == AuditAction.Action);
        // By default the log leaves out routine lines: an action line whose record changes are listed anyway (same request),
        // and the assistant's reads. "all" shows them too, as does looking at one request.
        if (kind is null or "" && string.IsNullOrEmpty(request))
            q = q.Where(a => !(a.Action == AuditAction.Action
                && ((a.RequestId != "" && db.AuditEntries.Any(b => b.RequestId == a.RequestId && b.Action != AuditAction.Action))
                    || (a.Via == "Assistant" && a.Description.StartsWith("GET ")))));
        if (staff != null) q = q.Where(a => a.ActorType == ActorType.Staff && a.ActorId == staff);
        if (!string.IsNullOrEmpty(request)) q = q.Where(a => a.RequestId == request);
        if (!string.IsNullOrEmpty(record)) q = q.Where(a => a.EntityName == record);
        if (Enum.TryParse<AuditAction>(action, out var ac)) q = q.Where(a => a.Action == ac);
        if (!string.IsNullOrEmpty(key)) q = q.Where(a => a.EntityKey == key);
        if (from != null) q = q.Where(a => a.OccurredAt >= from.Value.ToDateTime(TimeOnly.MinValue));
        if (to != null) q = q.Where(a => a.OccurredAt < to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (lq.Term.Length > 0) q = q.Where(a => a.ActorName.Contains(lq.Term) || a.EntityKey == lq.Term || a.Changes.Contains(lq.Term)
            || a.Description.Contains(lq.Term) || a.Comment.Contains(lq.Term));
        var page = await q.PageAsync(lq, AuditSorts, a => new
        {
            id = a.Id, record = a.EntityName, key = a.EntityKey, action = a.Action.ToString(), by = a.ActorName, actor_type = a.ActorType.ToString(),
            when = a.OccurredAt, changes = a.Changes, description = a.Description, via = a.Via, comment = a.Comment, proposal_id = a.ProposalId,
            request_id = a.RequestId, ip = a.IpAddress,
            comments = a.Comments.OrderBy(c => c.CreatedAt).Select(c => new { id = c.Id, by = c.StaffName, text = c.Text, at = c.CreatedAt }).ToList(),
        }, ct);
        var records = await db.AuditEntries.AsNoTracking().Select(a => a.EntityName).Distinct().OrderBy(x => x).ToListAsync(ct);
        return new { page.Items, page.Total, page.Page, page.Size, page.Pages, page.Sort, page.Dir, records };
    }

    /// <summary>A reviewer's comment on an audit entry; entries themselves never change.</summary>
    public async Task<object> CommentAsync(long id, string? text, CancellationToken ct = default)
    {
        text = (text ?? "").Trim();
        if (text.Length is < 1 or > 2000) throw DomainException.Invalid("note_length");
        if (!await db.AuditEntries.AnyAsync(a => a.Id == id, ct)) throw DomainException.NotFound();
        db.AuditComments.Add(new AuditComment
        {
            AuditEntryId = id, StaffUserId = actor.Id ?? throw DomainException.Forbidden(), StaffName = actor.Name, Text = TextUtil.NoEmDash(text), CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    /// <summary>Assistant messages per person (last day, week and month) with their daily limit, and the daily total for 30 days.</summary>
    public async Task<object> AssistantUsageAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var month = now.AddDays(-30);
        var setting = await db.AgentSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct) ?? new AgentSetting();
        var runs = await db.AgentRuns.AsNoTracking().Where(r => r.StartedAt >= month).Select(r => new { r.StaffUserId, r.StartedAt }).ToListAsync(ct);
        var staff = await db.StaffUsers.AsNoTracking().OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName, u.Email, u.Role, u.IsEnabled, u.AgentDailyLimit }).ToListAsync(ct);
        var proposals = await db.AgentProposals.AsNoTracking().Where(p => p.CreatedAt >= month)
            .GroupBy(p => new { p.StaffUserId, p.Status }).Select(g => new { g.Key.StaffUserId, g.Key.Status, n = g.Count() }).ToListAsync(ct);
        var people = staff.Select(u => new
        {
            id = u.Id, name = u.FullName, email = u.Email, role = u.Role.ToString(), enabled = u.IsEnabled,
            day = runs.Count(r => r.StaffUserId == u.Id && r.StartedAt >= now.AddDays(-1)),
            week = runs.Count(r => r.StaffUserId == u.Id && r.StartedAt >= now.AddDays(-7)),
            month = runs.Count(r => r.StaffUserId == u.Id),
            approved = proposals.Where(p => p.StaffUserId == u.Id && p.Status == ProposalStatus.Approved).Sum(p => p.n),
            limit = u.AgentDailyLimit is { } l ? (int)l : (int?)null,
        }).OrderByDescending(p => p.month).ThenBy(p => p.name).ToList();
        var today = clock.Today();
        var days = Enumerable.Range(0, 30).Select(i => today.AddDays(-29 + i)).Select(d => new
        {
            day = d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            messages = runs.Count(r => DateOnly.FromDateTime(clock.Local(r.StartedAt)) == d),
        }).ToList();
        // Model use over the same 30 days: tokens per day and per feature, with an estimated cost when prices are set
        // (LLM_PRICE_INPUT and LLM_PRICE_OUTPUT: the price per million tokens, in LLM_PRICE_CURRENCY, default USD).
        var use = await db.ModelUsages.AsNoTracking().Where(u => u.At >= month)
            .Select(u => new { u.At, u.Source, u.Feature, u.InputTokens, u.OutputTokens }).ToListAsync(ct);
        static decimal? Price(string name) => decimal.TryParse(Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var p) && p >= 0 ? p : null;
        var pin = Price("LLM_PRICE_INPUT");
        var pout = Price("LLM_PRICE_OUTPUT");
        decimal? Cost(long i, long o) => pin == null && pout == null ? null : Math.Round((pin ?? 0) * i / 1_000_000m + (pout ?? 0) * o / 1_000_000m, 4);
        var model = new
        {
            currency = Environment.GetEnvironmentVariable("LLM_PRICE_CURRENCY") is { Length: > 0 } c ? c : "USD",
            priced = pin != null || pout != null,
            input = use.Sum(u => (long)u.InputTokens), output = use.Sum(u => (long)u.OutputTokens), calls = use.Count,
            cost = Cost(use.Sum(u => (long)u.InputTokens), use.Sum(u => (long)u.OutputTokens)),
            days = days.Select(d => d.day).Select(d => new
            {
                day = d,
                assistant = use.Where(u => u.Source == "assistant" && clock.Local(u.At).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) == d).Sum(u => (long)u.InputTokens + u.OutputTokens),
                features = use.Where(u => u.Source != "assistant" && clock.Local(u.At).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) == d).Sum(u => (long)u.InputTokens + u.OutputTokens),
            }).ToList(),
            by_feature = use.GroupBy(u => u.Feature).Select(g => new
            {
                feature = g.Key, calls = g.Count(), input = g.Sum(u => (long)u.InputTokens), output = g.Sum(u => (long)u.OutputTokens),
                cost = Cost(g.Sum(u => (long)u.InputTokens), g.Sum(u => (long)u.OutputTokens)),
            }).OrderByDescending(x => x.input + x.output).ToList(),
        };
        return new { default_limit = (int)setting.DailyRunLimit, people, days, total_month = runs.Count, model };
    }

    /// <summary>A person's own daily limit; null uses the assistant's default, 0 turns the assistant off for them.</summary>
    public async Task<object> SetAssistantLimitAsync(int staffId, int? limit, CancellationToken ct = default)
    {
        var u = await db.StaffUsers.FirstOrDefaultAsync(x => x.Id == staffId, ct) ?? throw DomainException.NotFound();
        if (limit is < 0 or > 5000) throw DomainException.Invalid("agent_daily_limit");
        u.AgentDailyLimit = limit is { } l ? (short)l : null;
        u.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    /// <summary>The state of the service: database, background work, the email outbox, CV scoring and (given by the caller) the assistant.</summary>
    public async Task<object> HealthAsync(object assistant, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        string dbState;
        try
        {
            await db.StaffUsers.AsNoTracking().CountAsync(ct);
            dbState = "ok";
        }
        catch (Exception) { dbState = "down"; }
        var dbMs = sw.ElapsedMilliseconds;
        var now = clock.UtcNow;
        var dayAgo = now.AddDays(-1);
        var jobs = await db.BackgroundJobs.AsNoTracking().GroupBy(j => j.Status).Select(g => new { g.Key, n = g.Count() }).ToListAsync(ct);
        var oldestQueued = await db.BackgroundJobs.AsNoTracking().Where(j => j.Status == BackgroundJobStatus.Queued && j.RunAfter <= now)
            .OrderBy(j => j.RunAfter).Select(j => (DateTime?)j.RunAfter).FirstOrDefaultAsync(ct);
        var failedJobs = await db.BackgroundJobs.AsNoTracking().Where(j => j.Status == BackgroundJobStatus.Failed).OrderByDescending(j => j.CreatedAt).Take(5)
            .Select(j => new { id = j.Id, type = j.JobType, target = j.TargetId, error = j.LastError, at = j.CreatedAt }).ToListAsync(ct);
        var emails = await db.EmailMessages.AsNoTracking().Where(e => e.CreatedAt >= dayAgo).GroupBy(e => e.Status).Select(g => new { g.Key, n = g.Count() }).ToListAsync(ct);
        var queuedEmails = await db.EmailMessages.AsNoTracking().CountAsync(e => e.Status == EmailStatus.Queued, ct);
        var cvFailed = await db.CvAnalyses.AsNoTracking().CountAsync(c => c.Status == CvAnalysisStatus.Failed, ct);
        var cvWaiting = await db.CvAnalyses.AsNoTracking().CountAsync(c => c.Status == CvAnalysisStatus.Pending || c.Status == CvAnalysisStatus.Processing, ct);
        var waitMinutes = oldestQueued == null ? 0 : Math.Round((now - oldestQueued.Value).TotalMinutes, 1);
        var mailOn = await mail.IsConfiguredAsync(ct);
        return new
        {
            checked_at = now,
            database = new { state = dbState, ms = dbMs },
            background = new
            {
                state = waitMinutes > 15 ? "slow" : "ok", wait_minutes = waitMinutes,
                queued = jobs.Where(j => j.Key == BackgroundJobStatus.Queued).Sum(j => j.n), running = jobs.Where(j => j.Key == BackgroundJobStatus.Running).Sum(j => j.n),
                failed = jobs.Where(j => j.Key == BackgroundJobStatus.Failed).Sum(j => j.n), recent_failures = failedJobs,
            },
            email = new
            {
                state = !mailOn ? "off" : emails.Where(e => e.Key == EmailStatus.Failed).Sum(e => e.n) > 0 ? "errors" : "ok",
                configured = mailOn, queued = queuedEmails,
                sent_day = emails.Where(e => e.Key == EmailStatus.Sent).Sum(e => e.n), failed_day = emails.Where(e => e.Key == EmailStatus.Failed).Sum(e => e.n),
            },
            cv_scoring = new { state = cvFailed > 0 ? "errors" : "ok", waiting = cvWaiting, failed = cvFailed },
            virus_scan = new { state = await scanner.StateAsync(ct), required = scanner.Required },
            assistant,
        };
    }
}
