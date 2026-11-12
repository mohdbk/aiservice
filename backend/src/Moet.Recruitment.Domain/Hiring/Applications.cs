using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Identity;

namespace Moet.Recruitment.Domain.Hiring;

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
    public string CoverNote { get; set; } = "";
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Open;
    public string Source { get; set; } = "CareersPortal";
    public string LanguageCode { get; set; } = "ar";
    public DateTime DeclarationAcceptedAt { get; set; }
    public Guid CvFileId { get; set; }
    public ScreeningOutcome ScreeningOutcome { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
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
