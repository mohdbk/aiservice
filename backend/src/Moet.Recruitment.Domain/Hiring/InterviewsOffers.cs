using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Identity;

namespace Moet.Recruitment.Domain.Hiring;

public class InterviewRound
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>English name, shown when the backoffice is in English (the Arabic name otherwise).</summary>
    public string NameEn { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public List<InterviewRoundSkill> Skills { get; set; } = [];
}

public class InterviewRoundSkill
{
    public int Id { get; set; }
    public int InterviewRoundId { get; set; }
    public int SortOrder { get; set; }
    public string Skill { get; set; } = "";
}

public class Interview
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int JobApplicationId { get; set; }
    public JobApplication? JobApplication { get; set; }
    public int InterviewRoundId { get; set; }
    public InterviewRound? InterviewRound { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string Location { get; set; } = "";
    public InterviewStatus Status { get; set; } = InterviewStatus.Scheduled;
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int? CreatedByStaffId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<InterviewPanelist> Panelists { get; set; } = [];
    public List<InterviewFeedback> Feedback { get; set; } = [];

    public bool IsActive => Status is InterviewStatus.Scheduled or InterviewStatus.UnderReview;

    public void Validate()
    {
        if (EndsAt <= StartsAt) throw DomainException.Invalid("interview_times");
    }

    /// <summary>Records one panelist's feedback; the first feedback moves the interview to Under Review.</summary>
    public InterviewFeedback GiveFeedback(int staffId, Recommendation rec, string comments, IEnumerable<(string Skill, byte Rating)> ratings, DateTime now)
    {
        if (Status == InterviewStatus.Cancelled) throw DomainException.Conflict("interview_cancelled");
        if (string.IsNullOrWhiteSpace(comments)) throw DomainException.Invalid("feedback_required");
        var fb = Feedback.FirstOrDefault(f => f.StaffUserId == staffId);
        if (fb == null)
        {
            fb = new InterviewFeedback { StaffUserId = staffId };
            Feedback.Add(fb);
        }
        fb.Recommendation = rec;
        fb.Comments = TextUtil.Clip(comments.Trim(), 4000);
        fb.SubmittedAt = now;
        fb.Ratings.Clear();
        foreach (var (skill, rating) in ratings.Where(r => r.Skill.Trim().Length > 0).DistinctBy(r => r.Skill.Trim()))
        {
            if (rating is < 1 or > 5) throw DomainException.Invalid("rating_range");
            fb.Ratings.Add(new InterviewFeedbackRating { Skill = TextUtil.Clip(skill.Trim(), 140), Rating = rating });
        }
        if (Status == InterviewStatus.Scheduled) Status = InterviewStatus.UnderReview;
        UpdatedAt = now;
        return fb;
    }
}

public class InterviewPanelist
{
    public int InterviewId { get; set; }
    public int StaffUserId { get; set; }
    public StaffUser? StaffUser { get; set; }
}

public class InterviewFeedback
{
    public int Id { get; set; }
    public int InterviewId { get; set; }
    public int StaffUserId { get; set; }
    public StaffUser? StaffUser { get; set; }
    public Recommendation Recommendation { get; set; }
    public string Comments { get; set; } = "";
    public DateTime SubmittedAt { get; set; }
    public List<InterviewFeedbackRating> Ratings { get; set; } = [];
}

public class InterviewFeedbackRating
{
    public int InterviewFeedbackId { get; set; }
    public string Skill { get; set; } = "";
    public byte Rating { get; set; }
}

public class JobOffer
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int JobApplicationId { get; set; }
    public JobApplication? JobApplication { get; set; }
    public string PositionTitle { get; set; } = "";
    public DateOnly OfferDate { get; set; }
    public DateOnly? StartDate { get; set; }
    public decimal? MonthlySalary { get; set; }
    public string Currency { get; set; } = "AED";
    public string Terms { get; set; } = "";
    /// <summary>The letter template; the default template when none was picked.</summary>
    public int? OfferTemplateId { get; set; }
    public OfferStatus Status { get; set; } = OfferStatus.Draft;
    public DateTime CreatedAt { get; set; }
    public int? CreatedByStaffId { get; set; }
    public DateTime UpdatedAt { get; set; }

    static readonly Dictionary<OfferStatus, OfferStatus[]> Allowed = new()
    {
        [OfferStatus.Draft] = [OfferStatus.Sent, OfferStatus.Withdrawn],
        [OfferStatus.Sent] = [OfferStatus.Accepted, OfferStatus.Declined, OfferStatus.Withdrawn],
        [OfferStatus.Accepted] = [OfferStatus.Withdrawn],
        [OfferStatus.Declined] = [],
        [OfferStatus.Withdrawn] = [],
    };

    public void MoveTo(OfferStatus next, DateTime now)
    {
        if (next == Status) return;
        if (!Allowed[Status].Contains(next)) throw DomainException.Conflict("offer_status");
        Status = next;
        UpdatedAt = now;
    }
}
