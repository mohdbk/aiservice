using System.Net.Mail;
using System.Text.RegularExpressions;
using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Operations;

/// <summary>
/// The outgoing email server, set by administrators in the backoffice. When Host is empty the SMTP_* environment
/// settings are used instead (on-prem installs can keep them in the environment). The password is stored encrypted.
/// </summary>
public class EmailSetting
{
    public int Id { get; set; } = 1;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseTls { get; set; } = true;
    public string UserName { get; set; } = "";
    public string PasswordProtected { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "";
    public string SignatureAr { get; set; } = "";
    public string SignatureEn { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByStaffId { get; set; }

    public void Validate()
    {
        Host = Host.Trim();
        if (Host.Length == 0) return;
        if (Port is < 1 or > 65535) throw DomainException.Invalid("smtp_port_invalid");
        if (!EmailTemplates.IsEmail(FromAddress)) throw DomainException.Invalid("email_invalid", new Dictionary<string, object?> { ["field"] = "from_address" });
    }
}

/// <summary>
/// An email to candidates in Arabic and English. Event templates are sent by the system when something happens (see
/// <see cref="EmailTemplates.Events"/>); templates without an event are ready-made wording for HR and the assistant.
/// Bodies are plain text: a blank line starts a new paragraph and {placeholders} are filled when sending.
/// </summary>
public class EmailTemplate
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string? Event { get; set; }
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string SubjectAr { get; set; } = "";
    public string BodyAr { get; set; } = "";
    public string SubjectEn { get; set; } = "";
    public string BodyEn { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByStaffId { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public (string Subject, string Body) In(string lang) => lang == "ar" ? (SubjectAr, BodyAr) : (SubjectEn, BodyEn);

    public void Validate()
    {
        if (!EmailTemplates.KeyPattern().IsMatch(Key)) throw DomainException.Invalid("template_key_invalid");
        if (NameAr.Trim().Length == 0 || NameEn.Trim().Length == 0) throw DomainException.Invalid("name_required");
        if (SubjectAr.Trim().Length == 0 || SubjectEn.Trim().Length == 0 || BodyAr.Trim().Length == 0 || BodyEn.Trim().Length == 0)
            throw DomainException.Invalid("template_text_required");
        if (Event != null)
        {
            if (!EmailTemplates.Events.TryGetValue(Event, out var info)) throw DomainException.Invalid("invalid_value");
            if (info.Required && !IsEnabled) throw DomainException.Invalid("template_required_on");
            // The sign-in links only work if the email carries them.
            if (info.Placeholders.Contains("link") && info.Required && (!BodyAr.Contains("{link}") || !BodyEn.Contains("{link}")))
                throw DomainException.Invalid("template_link_required");
        }
    }
}

public sealed record EmailEvent(string NameAr, string NameEn, bool Required, string[] Placeholders);

public static partial class EmailTemplates
{
    /// <summary>
    /// The events that send an email to the candidate, with the placeholders each one fills. Required events (sign-in links)
    /// cannot be switched off.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, EmailEvent> Events = new Dictionary<string, EmailEvent>
    {
        ["verify"] = new("تفعيل الحساب", "Account Activation", true, ["name", "link"]),
        ["reset"] = new("إعادة تعيين كلمة المرور", "Password Reset", true, ["name", "link"]),
        ["application_received"] = new("استلام الطلب", "Application Received", false, ["name", "job_title", "reference", "link"]),
        ["assessment"] = new("دعوة إلى اختبار", "Assessment Invitation", false, ["name", "test", "deadline", "link"]),
        ["reexam"] = new("محاولة جديدة في اختبار", "New Assessment Attempt", false, ["name", "test", "deadline", "link"]),
        ["interview_scheduled"] = new("موعد المقابلة", "Interview Scheduled", false, ["name", "job_title", "round", "date", "time", "location"]),
        ["offer_sent"] = new("عرض العمل", "Job Offer", false, ["name", "job_title", "position_title", "start_date", "offer_letter", "link"]),
        ["not_selected"] = new("عدم الاختيار", "Not Selected", false, ["name", "job_title", "reference"]),
    };

    /// <summary>Placeholders every email can use.</summary>
    public static readonly string[] Common = ["name"];

    [GeneratedRegex("^[a-z][a-z0-9_]{1,59}$")]
    public static partial Regex KeyPattern();

    public static bool IsEmail(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length > 254) return false;
        try { return new MailAddress(s.Trim()).Address == s.Trim(); }
        catch (FormatException) { return false; }
    }

    /// <summary>Fills {placeholders}; unknown ones are left as written so HR can see them in a preview.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values)
    {
        foreach (var (k, v) in values) text = text.Replace("{" + k + "}", v);
        return text;
    }
}

/// <summary>
/// An offer letter in Arabic and English. Plain text: a blank line starts a paragraph and a line starting with "# " is a
/// heading. Placeholders are filled from the offer when the letter is shown, printed or emailed.
/// </summary>
public class OfferTemplate
{
    public int Id { get; set; }
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string BodyAr { get; set; } = "";
    public string BodyEn { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByStaffId { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public static readonly string[] Placeholders =
        ["name", "reference", "job_title", "department", "position_title", "offer_date", "start_date", "monthly_salary", "currency", "terms", "offer_code"];

    public string In(string lang) => lang == "ar" ? BodyAr : BodyEn;

    public void Validate()
    {
        if (NameAr.Trim().Length == 0 || NameEn.Trim().Length == 0) throw DomainException.Invalid("name_required");
        if (BodyAr.Trim().Length == 0 || BodyEn.Trim().Length == 0) throw DomainException.Invalid("template_text_required");
        if (IsDefault && !IsActive) throw DomainException.Invalid("template_default_inactive");
    }
}
