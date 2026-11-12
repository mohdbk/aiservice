using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moet.Recruitment.Application.Assessments;
using Moet.Recruitment.Application.Backoffice;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Application.Portal;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Infrastructure.Persistence;

namespace Moet.Recruitment.Infrastructure.Background;

/// <summary>Sends queued emails over SMTP, with retries.</summary>
public sealed class EmailOutboxWorker(IServiceScopeFactory scopes, ILogger<EmailOutboxWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await SendDueAsync(stop);
            }
            catch (Exception e) when (!stop.IsCancellationRequested)
            {
                log.LogError(e, "Email outbox failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(10), stop).ContinueWith(_ => { });
        }
    }

    async Task SendDueAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        // The server set in the backoffice, otherwise the SMTP_* environment settings; nothing is sent without either.
        var config = await scope.ServiceProvider.GetRequiredService<MailConfig>().CurrentAsync(ct);
        if (config == null) return;
        var sender = scope.ServiceProvider.GetRequiredService<IMailSender>();
        var now = clock.UtcNow;
        var due = await db.EmailMessages.Where(m => m.Status == EmailStatus.Queued && m.NextAttemptAt <= now)
            .OrderBy(m => m.Id).Take(20).ToListAsync(ct);
        if (due.Count == 0) return;
        foreach (var m in due)
        {
            try
            {
                await sender.SendAsync(config, m.ToAddress, m.Subject, m.HtmlBody, m.TextBody, ct);
                m.Status = EmailStatus.Sent;
                m.SentAt = clock.UtcNow;
                m.Attempts++;
            }
            catch (Exception e) when (e is SmtpException or InvalidOperationException or FormatException or System.Net.Sockets.SocketException)
            {
                m.Failed(e.Message, clock.UtcNow);
                log.LogWarning("Email {Id} to {To} failed: {Error}", m.Id, m.ToAddress, e.Message);
            }
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Runs queued background jobs (CV scoring, translation drafts, mark suggestions) and the schedules.</summary>
public sealed class JobWorker(IServiceScopeFactory scopes, ILogger<JobWorker> log) : BackgroundService
{
    static readonly string Owner = Environment.MachineName + ":" + Environment.ProcessId;
    DateTime _nextHourly = DateTime.MinValue;
    DateTime _nextDaily = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                worked = await RunOneAsync(stop);
                await SchedulesAsync(stop);
            }
            catch (Exception e) when (!stop.IsCancellationRequested)
            {
                log.LogError(e, "Background job loop failed");
            }
            if (!worked) await Task.Delay(TimeSpan.FromSeconds(3), stop).ContinueWith(_ => { });
        }
    }

    async Task<bool> RunOneAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        // Claim one due job atomically (several API instances can run this loop safely).
        var ids = await db.Database.SqlQueryRaw<long>(
            """
            UPDATE TOP (1) j SET Status = 'Running', LockedUntil = DATEADD(minute, 30, SYSUTCDATETIME()), Attempts = Attempts + 1
            OUTPUT inserted.Id AS [Value]
            FROM ops.BackgroundJob j WITH (READPAST, UPDLOCK, ROWLOCK)
            WHERE (j.Status = 'Queued' AND j.RunAfter <= SYSUTCDATETIME())
               OR (j.Status = 'Running' AND j.LockedUntil < SYSUTCDATETIME())
            """).ToListAsync(ct);
        if (ids.Count == 0) return false;
        var job = await db.BackgroundJobs.FirstAsync(j => j.Id == ids[0], ct);
        var handler = scope.ServiceProvider.GetServices<IJobHandler>().FirstOrDefault(h => h.JobType == job.JobType);
        try
        {
            if (handler == null) throw new InvalidOperationException($"No handler for {job.JobType}");
            await handler.RunAsync(job.TargetId, ct);
            db.ChangeTracker.Clear();
            await db.BackgroundJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(u => u
                .SetProperty(j => j.Status, BackgroundJobStatus.Done).SetProperty(j => j.CompletedAt, DateTime.UtcNow)
                .SetProperty(j => j.LockedUntil, (DateTime?)null), ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(e, "Job {Type} {Target} failed (attempt {Attempt})", job.JobType, job.TargetId, job.Attempts);
            db.ChangeTracker.Clear();
            var failed = job.Attempts >= Domain.Operations.BackgroundJob.MaxAttempts;
            var error = e.Message.Length > 1000 ? e.Message[..1000] : e.Message;
            await db.BackgroundJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(u => u
                .SetProperty(j => j.Status, failed ? BackgroundJobStatus.Failed : BackgroundJobStatus.Queued)
                .SetProperty(j => j.RunAfter, now.AddMinutes(2 * job.Attempts))
                .SetProperty(j => j.LockedUntil, (DateTime?)null)
                .SetProperty(j => j.LastError, error), ct);
            if (failed && handler is IJobFailureHandler f) await f.FailedAsync(job.TargetId, error, ct);
        }
        return true;
    }

    async Task SchedulesAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (now < _nextHourly && now < _nextDaily) return;
        using var scope = scopes.CreateScope();
        if (now >= _nextHourly)
        {
            _nextHourly = now.AddHours(1);
            var n = await scope.ServiceProvider.GetRequiredService<AssessmentWorkflow>().ExpireInvitationsAsync(ct);
            if (n > 0) log.LogInformation("Expired {Count} assessment invitations", n);
        }
        if (now >= _nextDaily)
        {
            _nextDaily = now.AddDays(1);
            await scope.ServiceProvider.GetRequiredService<PortalAccountService>().PurgeOldTokensAsync(ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var old = now.AddDays(-30);
            await db.BackgroundJobs.Where(j => j.Status == BackgroundJobStatus.Done && j.CompletedAt < old).ExecuteDeleteAsync(ct);
            await db.EmailMessages.Where(m => m.Status == EmailStatus.Sent && m.SentAt < old).ExecuteDeleteAsync(ct);
            // Retention (PDPL): old unsuccessful applications and inactive accounts are anonymised.
            var n = await scope.ServiceProvider.GetRequiredService<DataProtectionService>().RunRetentionAsync(false, ct);
            if (n > 0) log.LogInformation("Retention anonymised {Count} records", n);
        }
    }
}
