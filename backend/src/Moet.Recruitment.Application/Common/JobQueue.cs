using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Common;

/// <summary>Background work kinds. The worker in Infrastructure runs them through <see cref="IJobHandler"/>s.</summary>
public static class JobTypes
{
    public const string ScoreApplication = "ScoreApplication";
    public const string SuggestJobTranslation = "SuggestJobTranslation";
    public const string SuggestScreeningTranslation = "SuggestScreeningTranslation";
    public const string SuggestQuestionTranslation = "SuggestQuestionTranslation";
    public const string SuggestAssessmentTranslation = "SuggestAssessmentTranslation";
    public const string SuggestMarks = "SuggestMarks";
    public const string HandOff = "HandOff";
    public const string SuggestPortalText = "SuggestPortalText";
}

public interface IJobHandler
{
    string JobType { get; }
    Task RunAsync(int targetId, CancellationToken ct);
}

/// <summary>A job handler that records a final failure (for example, on the application).</summary>
public interface IJobFailureHandler
{
    Task FailedAsync(int targetId, string error, CancellationToken ct);
}

public sealed class JobQueue(IAppDbContext db, IClock clock)
{
    /// <summary>Queues work for a record; a job already waiting for the same record is reused.</summary>
    public async Task EnqueueAsync(string jobType, int targetId, CancellationToken ct = default, TimeSpan? delay = null)
    {
        var pending = await db.BackgroundJobs.AnyAsync(j => j.JobType == jobType && j.TargetId == targetId
            && (j.Status == BackgroundJobStatus.Queued || j.Status == BackgroundJobStatus.Running), ct);
        if (pending) return;
        if (db.BackgroundJobs.Local.Any(j => j.JobType == jobType && j.TargetId == targetId && j.Status == BackgroundJobStatus.Queued)) return;
        db.BackgroundJobs.Add(new BackgroundJob
        {
            JobType = jobType, TargetId = targetId, Status = BackgroundJobStatus.Queued,
            RunAfter = clock.UtcNow + (delay ?? TimeSpan.Zero), CreatedAt = clock.UtcNow,
        });
    }

    /// <summary>Queues the same work for many records in one go (records already waiting are skipped).</summary>
    public async Task<int> EnqueueManyAsync(string jobType, IReadOnlyCollection<int> targetIds, CancellationToken ct = default)
    {
        if (targetIds.Count == 0) return 0;
        var waiting = (await db.BackgroundJobs.Where(j => j.JobType == jobType && (j.Status == BackgroundJobStatus.Queued || j.Status == BackgroundJobStatus.Running))
            .Select(j => j.TargetId).ToListAsync(ct)).ToHashSet();
        var n = 0;
        foreach (var id in targetIds.Distinct().Where(id => !waiting.Contains(id)))
        {
            db.BackgroundJobs.Add(new BackgroundJob { JobType = jobType, TargetId = id, Status = BackgroundJobStatus.Queued, RunAfter = clock.UtcNow, CreatedAt = clock.UtcNow });
            n++;
        }
        return n;
    }
}
