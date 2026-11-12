using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Operations;

public enum DataRequestKind { Export, Deletion }

public enum DataRequestStatus { Open, Completed, Declined }

/// <summary>
/// A candidate's request about their personal data (PDPL): a copy of it (given at once) or deleting their account (reviewed
/// by a system administrator, who approves it, which removes the personal data, or declines it with a reason).
/// </summary>
public class DataRequest
{
    public int Id { get; set; }
    public int ApplicantAccountId { get; set; }
    public DataRequestKind Kind { get; set; }
    public DataRequestStatus Status { get; set; } = DataRequestStatus.Open;
    /// <summary>The candidate's name and email when the request was made (kept after the account is anonymised).</summary>
    public string Requester { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Response { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int? ClosedByStaffId { get; set; }
}

/// <summary>A consent a candidate gave or withdrew (the application declaration, joining the talent pool), as a permanent record.</summary>
public class ConsentRecord
{
    public long Id { get; set; }
    public int ApplicantAccountId { get; set; }
    public string Kind { get; set; } = "";
    public bool Granted { get; set; }
    /// <summary>What it was given for, for example the application code.</summary>
    public string Detail { get; set; } = "";
    public DateTime At { get; set; }

    public const string Declaration = "declaration", TalentPool = "talent_pool";
}

/// <summary>
/// How long personal data is kept (one row): unsuccessful applications are anonymised after a number of months, and accounts with
/// no applications and no sign-in for a number of months are anonymised. A daily job applies it while it is on.
/// </summary>
public class RetentionSetting
{
    public byte Id { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public short UnsuccessfulMonths { get; set; } = 24;
    public short InactiveAccountMonths { get; set; } = 36;
    public DateTime? LastRunAt { get; set; }
    public int LastRunCount { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByStaffId { get; set; }

    public void Validate()
    {
        if (UnsuccessfulMonths is < 1 or > 240 || InactiveAccountMonths is < 1 or > 240) throw DomainException.Invalid("retention_months");
    }
}
