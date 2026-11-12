using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Hiring;

public enum ApprovalKind { JobPublish, OfferSend, Requisition }

public enum ApprovalStatus { Pending, Approved, Rejected, Cancelled }

/// <summary>
/// A step that needs a recruitment admin's sign-off: publishing a job, sending an offer, or opening a requisition. The
/// request records who asked and who decided, with their comment; approving carries the step out.
/// </summary>
public class ApprovalRequest
{
    public int Id { get; set; }
    public ApprovalKind Kind { get; set; }
    public int RecordId { get; set; }
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public int RequestedByStaffId { get; set; }
    public string RequestedByName { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    public int? DecidedByStaffId { get; set; }
    public string DecidedByName { get; set; } = "";
    public DateTime? DecidedAt { get; set; }
    public string Comment { get; set; } = "";

    public void Decide(bool approve, int staffId, string name, string? comment, DateTime now)
    {
        if (Status != ApprovalStatus.Pending) throw DomainException.Conflict("approval_closed");
        if (!approve && string.IsNullOrWhiteSpace(comment)) throw DomainException.Invalid("reason_required");
        Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        DecidedByStaffId = staffId;
        DecidedByName = name;
        DecidedAt = now;
        Comment = TextUtil.Clip(comment?.Trim(), 2000);
    }
}

public enum RequisitionStatus { Pending, Approved, Rejected, Filled, Cancelled }

/// <summary>A request for a new post (department, grade, number of people, budget and reason). Once approved, it becomes a draft job.</summary>
public class Requisition
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string TitleEn { get; set; } = "";
    public int? DepartmentId { get; set; }
    public int? LocationId { get; set; }
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;
    public string Grade { get; set; } = "";
    public int Headcount { get; set; } = 1;
    public decimal? BudgetMonthly { get; set; }
    public string Justification { get; set; } = "";
    public DateOnly? NeededBy { get; set; }
    public RequisitionStatus Status { get; set; } = RequisitionStatus.Pending;
    public int? JobOpeningId { get; set; }
    public int RequestedByStaffId { get; set; }
    public string RequestedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public void Validate()
    {
        if (Title.Trim().Length == 0) throw DomainException.Invalid("title_required");
        if (Justification.Trim().Length == 0) throw DomainException.Invalid("justification_required");
        if (Headcount is < 1 or > 500) throw DomainException.Invalid("headcount_invalid");
    }
}
