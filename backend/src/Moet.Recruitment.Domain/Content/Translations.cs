using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Content;

/// <summary>A reviewed translation of content written in another language.</summary>
public interface ITranslation
{
    string LanguageCode { get; set; }
    ReviewStatus ReviewStatus { get; set; }
    string? SuggestedHash { get; set; }
    int? ApprovedByStaffId { get; set; }
    DateTime? ApprovedAt { get; set; }
}

/// <summary>
/// The review rule for translations: a fresh language model draft waits for review. HR approves by ticking
/// Approved, or by writing or editing the text themselves. Unticking puts the current text back into review.
/// </summary>
public static class TranslationReview
{
    public static (ReviewStatus Status, string? KeepHash) Decide(bool approved, bool wasApproved, bool suggested,
        string? storedHash, string currentHash)
    {
        if (suggested) return (ReviewStatus.Suggested, currentHash);
        if (wasApproved && !approved) return (ReviewStatus.Suggested, currentHash);
        if (approved || string.IsNullOrEmpty(storedHash) || storedHash != currentHash) return (ReviewStatus.Approved, storedHash);
        return (ReviewStatus.Suggested, storedHash);
    }

    /// <summary>Applies an edit to a translation and records who approved it.</summary>
    public static void Apply(ITranslation t, bool approvedTicked, bool suggestedByModel, string currentHash, int? staffId, DateTime now)
    {
        var was = t.ReviewStatus == ReviewStatus.Approved;
        var (status, keep) = Decide(approvedTicked, was, suggestedByModel, t.SuggestedHash, currentHash);
        t.ReviewStatus = status;
        t.SuggestedHash = keep;
        if (status == ReviewStatus.Approved)
        {
            if (!was || t.ApprovedByStaffId == null)
            {
                t.ApprovedByStaffId = staffId;
                t.ApprovedAt = now;
            }
        }
        else
        {
            t.ApprovedByStaffId = null;
            t.ApprovedAt = null;
        }
    }

    public static bool IsFilled(params string?[] parts) => parts.Any(p => TextUtil.StripHtml(p).Length > 0);
}
