using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Content;

namespace Moet.Recruitment.Domain.Hiring;

public class JobOpening
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string SourceLanguageCode { get; set; } = "ar";
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    /// <summary>Free-text location, kept for jobs from before locations were a list (and as typed by the assistant).</summary>
    public string Location { get; set; } = "";
    public int? LocationId { get; set; }
    public JobLocation? JobLocation { get; set; }
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;
    public JobStatus Status { get; set; } = JobStatus.Draft;
    public bool IsPublished { get; set; }
    public DateOnly? PostedOn { get; set; }
    public DateOnly? ClosesOn { get; set; }
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public string SalaryCurrency { get; set; } = "AED";
    public SalaryPeriod SalaryPeriod { get; set; } = SalaryPeriod.Month;
    public bool ShowSalary { get; set; }
    public string? CriteriaVersion { get; set; }
    /// <summary>The role profile (job family) personality results are matched against; its sibling of the other questionnaire is used too.</summary>
    public int? RoleProfileId { get; set; }
    /// <summary>Weights of the combined selection score (percent, adding up to 100).</summary>
    public byte WeightCv { get; set; }
    public byte WeightTests { get; set; } = 40;
    public byte WeightInterview { get; set; } = 40;
    public byte WeightPersonality { get; set; } = 20;

    public SelectionWeights Weights => new(WeightCv, WeightTests, WeightInterview, WeightPersonality);
    public DateTime CreatedAt { get; set; }
    public int? CreatedByStaffId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByStaffId { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<JobOpeningTranslation> Translations { get; set; } = [];
    public List<JobCriterion> Criteria { get; set; } = [];
    public List<JobScreeningQuestion> ScreeningQuestions { get; set; } = [];
    public List<JobAssessmentStage> AssessmentStages { get; set; } = [];

    /// <summary>Accepting applications: open and not past its closing date.</summary>
    public bool IsAccepting(DateOnly today) => Status == JobStatus.Open && (ClosesOn == null || ClosesOn >= today);

    public bool IsListed(DateOnly today) => IsPublished && IsAccepting(today);

    /// <summary>Approved translation for a language, or null (the portal then shows the original).</summary>
    public JobOpeningTranslation? ApprovedTranslation(string lang) =>
        lang == SourceLanguageCode ? null : Translations.FirstOrDefault(t => t.LanguageCode == lang && t.ReviewStatus == ReviewStatus.Approved);

    public (string Title, string Description, string ContentLang) In(string lang)
    {
        var tr = ApprovedTranslation(lang);
        if (tr == null) return (Title, Description, SourceLanguageCode);
        return (tr.Title.Trim().Length > 0 ? tr.Title : Title, TextUtil.StripHtml(tr.Description).Length > 0 ? tr.Description : Description, lang);
    }

    /// <summary>
    /// The location in a language: the approved translation's own text when it has one, otherwise the emirate's name in that
    /// language when the location is an emirate, otherwise the original.
    /// </summary>
    public string LocationIn(string lang) =>
        JobLocation != null ? JobLocation.NameIn(lang)
        : lang == SourceLanguageCode ? Location
        : ApprovedTranslation(lang) is { Location.Length: > 0 } tr ? tr.Location
        : Locations.In(Location, lang);

    /// <summary>Version of the description that the screening criteria were derived from.</summary>
    public string DescriptionVersion() => TextUtil.Sha256Hex(Title + "\u001f" + TextUtil.StripHtml(Description))[..16];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title)) throw DomainException.Invalid("title_required");
        if (SalaryMin != null && SalaryMax != null && SalaryMin > SalaryMax) throw DomainException.Invalid("salary_range");
        if (PostedOn != null && ClosesOn != null && ClosesOn < PostedOn) throw DomainException.Invalid("closing_date");
        if (ScreeningQuestions.Any(q => q.RequirementRule != null && string.IsNullOrWhiteSpace(q.ExpectedValue)))
            throw DomainException.Invalid("requirement_value");
        Weights.Validate();
    }
}

public class JobOpeningTranslation : ITranslation
{
    public int JobOpeningId { get; set; }
    public string LanguageCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>The location in this language (any place, written as the reader should see it).</summary>
    public string Location { get; set; } = "";
    public ReviewStatus ReviewStatus { get; set; }
    public string? SuggestedHash { get; set; }
    public int? ApprovedByStaffId { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

/// <summary>A screening criterion derived from the job description, used to score every CV for the job.</summary>
public class JobCriterion
{
    public int Id { get; set; }
    public int JobOpeningId { get; set; }
    public int SortOrder { get; set; }
    public string Criterion { get; set; } = "";
    public decimal Weight { get; set; }
    public bool IsMustHave { get; set; }
}

public class JobScreeningQuestion
{
    public int JobOpeningId { get; set; }
    public int ScreeningQuestionId { get; set; }
    public ScreeningQuestion? ScreeningQuestion { get; set; }
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public RequirementRule? RequirementRule { get; set; }
    public string? ExpectedValue { get; set; }
}

public class JobAssessmentStage
{
    public int JobOpeningId { get; set; }
    public int AssessmentId { get; set; }
    public int SortOrder { get; set; }
    public SendWhen SendWhen { get; set; } = SendWhen.Manual;
    public byte DeadlineDays { get; set; } = 7;
}
