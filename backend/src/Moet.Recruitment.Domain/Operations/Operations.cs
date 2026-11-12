using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Operations;

public class StoredFile
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}

public class EmailMessage
{
    public long Id { get; set; }
    public string ToAddress { get; set; } = "";
    public string Subject { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public EmailStatus Status { get; set; } = EmailStatus.Queued;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string LastError { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }

    public const int MaxAttempts = 6;

    /// <summary>Retries with growing gaps: 1, 4, 9, 16, 25 minutes.</summary>
    public void Failed(string error, DateTime now)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        if (Attempts >= MaxAttempts) Status = EmailStatus.Failed;
        else NextAttemptAt = now.AddMinutes(Attempts * Attempts);
    }
}

public class BackgroundJob
{
    public long Id { get; set; }
    public string JobType { get; set; } = "";
    public int TargetId { get; set; }
    public BackgroundJobStatus Status { get; set; } = BackgroundJobStatus.Queued;
    public int Attempts { get; set; }
    public DateTime RunAfter { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string LastError { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public const int MaxAttempts = 3;
}

public class AuditEntry
{
    public long Id { get; set; }
    public string EntityName { get; set; } = "";
    public string EntityKey { get; set; } = "";
    public AuditAction Action { get; set; }
    public string Changes { get; set; } = "{}";
    public ActorType ActorType { get; set; }
    public int? ActorId { get; set; }
    public string ActorName { get; set; } = "";
    public DateTime OccurredAt { get; set; }
}

/// <summary>
/// A change the recruitment agent proposed. Nothing happens until the same staff member approves it, once, before it expires;
/// the backend then carries it out with that person's permissions.
/// </summary>
public class AgentProposal
{
    public Guid Id { get; set; }
    public int StaffUserId { get; set; }
    public string ThreadId { get; set; } = "";
    public string ActionName { get; set; } = "";
    public string Arguments { get; set; } = "{}";
    public string Label { get; set; } = "";
    public ProposalStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string Result { get; set; } = "";

    /// <summary>Claims the proposal for a decision by <paramref name="staffId"/>; throws when it is not theirs or no longer open.</summary>
    public void Claim(int staffId, DateTime now, bool approve)
    {
        if (StaffUserId != staffId) throw DomainException.Forbidden();
        if (Status != ProposalStatus.Pending || ExpiresAt < now) throw DomainException.Conflict("proposal_expired");
        Status = approve ? ProposalStatus.Approved : ProposalStatus.Cancelled;
        DecidedAt = now;
    }
}
