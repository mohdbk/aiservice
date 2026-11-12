namespace Moet.Recruitment.Domain.Content;

public class Language
{
    public string Code { get; set; } = "";
    public string NativeName { get; set; } = "";
    public string EnglishName { get; set; } = "";
    public bool IsRtl { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>Career portal and assistant settings (a single row).</summary>
public class PortalSetting
{
    public byte Id { get; set; } = 1;
    public string MainSiteUrl { get; set; } = "https://www.moet.gov.ae";
    public string PortalUrl { get; set; } = "";
    public string DefaultLanguageCode { get; set; } = "ar";
    public bool AllowSignup { get; set; } = true;
    public bool RequireEmailVerification { get; set; } = true;
    public byte MaxCvMb { get; set; } = 5;
    public Guid? LogoFileId { get; set; }
    public Guid? LogoDarkFileId { get; set; }
    public bool AutoScoreApplications { get; set; } = true;
    public bool AssistantEnabled { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByStaffId { get; set; }
}

/// <summary>An HR override of one portal text in one language. Only approved rows reach the portal.</summary>
public class PortalText : ITranslation
{
    public int Id { get; set; }
    public string LanguageCode { get; set; } = "";
    public string TextKey { get; set; } = "";
    public string Text { get; set; } = "";
    public ReviewStatus ReviewStatus { get; set; }
    public string? SuggestedHash { get; set; }
    public int? ApprovedByStaffId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SourceLanguageCode { get; set; } = "ar";
    public bool IsActive { get; set; } = true;
    public List<DepartmentTranslation> Translations { get; set; } = [];

    public string NameIn(string lang) =>
        lang == SourceLanguageCode ? Name : Translations.FirstOrDefault(t => t.LanguageCode == lang)?.Name is { Length: > 0 } n ? n : Name;
}

public class DepartmentTranslation
{
    public int DepartmentId { get; set; }
    public string LanguageCode { get; set; } = "";
    public string Name { get; set; } = "";
}
