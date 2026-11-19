using Moet.Hrms.Domain.Common;
using Moet.Hrms.Domain.Identity;

namespace Moet.Hrms.Domain.Hiring;

public class JobApplication
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int JobOpeningId { get; set; }
    public JobOpening? JobOpening { get; set; }
    public int ApplicantAccountId { get; set; }
    public ApplicantAccount? ApplicantAccount { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Country { get; set; } = "";
    /// <summary>The candidate's nationality (optional; for Emiratisation reporting, never for screening or scoring).</summary>
    public string Nationality { get; set; } = "";
    public string CoverNote { get; set; } = "";
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Open;
    public string Source { get; set; } = "Portal";
    public string LanguageCode { get; set; } = "ar";
    public DateTime DeclarationAcceptedAt { get; set; }
    public Guid CvFileId { get; set; }
    /// <summary>The candidate's saved CV the application was sent with (its file is copied to CvFileId); null for older ones.</summary>
    public int? CandidateCvId { get; set; }
    public ScreeningOutcome ScreeningOutcome { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Set when the personal data was removed (retention or a deletion request); the record stays for reporting.</summary>
    public DateTime? AnonymisedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<ScreeningAnswer> ScreeningAnswers { get; set; } = [];
    public CvAnalysis? CvAnalysis { get; set; }
    public List<ApplicationNote> Notes { get; set; } = [];

    public string Reference => TextUtil.Reference(Code, CreatedAt);
}

public class ScreeningAnswer
{
    public int JobApplicationId { get; set; }
    public int ScreeningQuestionId { get; set; }
    public int SortOrder { get; set; }
    public string QuestionText { get; set; } = "";
    public string AnswerText { get; set; } = "";
    public bool? MeetsRequirement { get; set; }
}

/// <summary>The CV reading and scoring result for one application. Recommendations only; people decide.</summary>
public class CvAnalysis
{
    public int JobApplicationId { get; set; }
    public CvAnalysisStatus Status { get; set; } = CvAnalysisStatus.Pending;
    public decimal? Score { get; set; }
    public string Summary { get; set; } = "";
    public string SummaryArabic { get; set; } = "";
    public string MustHaveGaps { get; set; } = "";
    public decimal? YearsOfExperience { get; set; }
    public string CvLanguage { get; set; } = "";
    public string ReadMethod { get; set; } = "";
    public string Model { get; set; } = "";
    public string? CriteriaVersion { get; set; }
    public string? ProfileJson { get; set; }
    public string Error { get; set; } = "";
    public DateTime? ScoredAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool NeedsReview { get; set; }
    public string ReviewReasons { get; set; } = "";
    /// <summary>"good" or "poor" (a short or garbled read, usually a scan).</summary>
    public string ReadQuality { get; set; } = "";
    public List<CvCriterionScore> CriterionScores { get; set; } = [];
}

public class CvCriterionScore
{
    public int JobApplicationId { get; set; }
    public int SortOrder { get; set; }
    public string Criterion { get; set; } = "";
    public decimal Weight { get; set; }
    public bool IsMustHave { get; set; }
    public byte Score { get; set; }
    public string Evidence { get; set; } = "";
    public string Category { get; set; } = "Other";
    /// <summary>Essential requirements: Met, NotMet or Unclear; empty for desirable ones.</summary>
    public string Status { get; set; } = "";
    /// <summary>The exact words from the CV the score rests on, and whether they were found in the CV text.</summary>
    public string Quote { get; set; } = "";
    public bool? QuoteFound { get; set; }
    public decimal? RelevantYears { get; set; }
    public string WhyNotHigher { get; set; } = "";
    /// <summary>A recruiter's score in place of the AI's, with the reason (kept when the CV is scored again).</summary>
    public byte? OverrideScore { get; set; }
    public string OverrideReason { get; set; } = "";
    public int? OverrideByStaffId { get; set; }
    public DateTime? OverrideAt { get; set; }

    public byte Effective => OverrideScore ?? Score;
}

public class ApplicationNote
{
    public int Id { get; set; }
    public int JobApplicationId { get; set; }
    public NoteKind Kind { get; set; }
    public string Text { get; set; } = "";
    public int? AuthorStaffId { get; set; }
    public StaffUser? AuthorStaff { get; set; }
    public int? AuthorApplicantId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>What an applicant sees of their application's status (never scores or notes).</summary>
public static class CandidateStatus
{
    public static (string Code, string Tone) For(ApplicationStatus status, bool hasInterview) =>
        hasInterview && status is ApplicationStatus.Open or ApplicationStatus.Hold or ApplicationStatus.Replied
            ? ("interview", "success")
            : status switch
            {
                ApplicationStatus.Replied => ("contacted", "warning"),
                ApplicationStatus.Accepted => ("successful", "success"),
                ApplicationStatus.Rejected => ("not_selected", "error"),
                ApplicationStatus.Withdrawn => ("withdrawn", "neutral"),
                _ => ("under_review", "info"),
            };
}

/// <summary>Accepted CV files: extension and leading bytes must agree.</summary>
public static class CvFile
{
    static readonly Dictionary<string, byte[][]> Signatures = new()
    {
        ["pdf"] = [[0x25, 0x50, 0x44, 0x46]],
        ["docx"] = [[0x50, 0x4B, 0x03, 0x04]],
        ["png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        ["jpg"] = [[0xFF, 0xD8, 0xFF]],
        ["jpeg"] = [[0xFF, 0xD8, 0xFF]],
    };

    public static readonly Dictionary<string, string> ContentTypes = new()
    {
        ["pdf"] = "application/pdf",
        ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
    };

    /// <summary>Returns the normalised extension or throws cv_type, cv_empty or cv_too_large.</summary>
    public static string Validate(string fileName, ReadOnlySpan<byte> content, int maxMb)
    {
        var ext = Path.GetExtension(fileName ?? "").TrimStart('.').ToLowerInvariant();
        var p = new Dictionary<string, object?> { ["max_mb"] = maxMb };
        if (!Signatures.TryGetValue(ext, out var sigs)) throw DomainException.Invalid("cv_type", p);
        if (content.Length == 0) throw DomainException.Invalid("cv_empty", p);
        if (content.Length > maxMb * 1024 * 1024) throw DomainException.Invalid("cv_too_large", p);
        foreach (var sig in sigs)
            if (content.Length >= sig.Length && content[..sig.Length].SequenceEqual(sig)) return ext;
        throw DomainException.Invalid("cv_type", p);
    }
}

/// <summary>
/// One of a candidate's own CVs, kept with their account: uploaded by them in the portal (or by HR for them), read once
/// for the text, a phone number and a structured profile, used to fill in applications and to find them in the talent pool.
/// </summary>
public class CandidateCv
{
    public const int MaxPerAccount = 5;
    public int Id { get; set; }
    public int ApplicantAccountId { get; set; }
    public Guid StoredFileId { get; set; }
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public bool IsDefault { get; set; }
    /// <summary>Pending (not read yet), Ready (profile read), TextOnly (text read, no language model) or Unreadable.</summary>
    public string Status { get; set; } = "Pending";
    public string Phone { get; set; } = "";
    public decimal? YearsOfExperience { get; set; }
    public string Summary { get; set; } = "";
    public string? ProfileJson { get; set; }
    public string Text { get; set; } = "";
    /// <summary>How the text was read: pdf-text, word, ocr or model ("" when not read).</summary>
    public string ReadMethod { get; set; } = "";
    /// <summary>Suggested screening answers already worked out from this CV, by job code: {"JOB-0001": {"answers": {...}}}.</summary>
    public string? ScreeningSuggestionsJson { get; set; }
    public int? UploadedByStaffId { get; set; }
    public DateTime UploadedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

/// <summary>
/// A CV uploaded on the sign-up page before the account exists: read at once so the form can be filled in (name, email,
/// phone), then moved into the new account's CV library. Deleted with its file a day after upload if nobody signs up.
/// </summary>
public class CvDraft
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);
    public int Id { get; set; }
    public Guid Token { get; set; }
    public Guid StoredFileId { get; set; }
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public decimal? YearsOfExperience { get; set; }
    public string Summary { get; set; } = "";
    public string? ProfileJson { get; set; }
    public string Text { get; set; } = "";
    public string ReadMethod { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
