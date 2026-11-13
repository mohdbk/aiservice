namespace Moet.Recruitment.Domain.Operations;

/// <summary>
/// Emails to system administrators when a health check fails (background jobs stuck or failing, emails failing, CV scoring
/// failing, backups failing). One email when problems appear or change, a reminder each day while they last, and one when
/// everything is back to normal.
/// </summary>
public class HealthAlertSetting
{
    public byte Id { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    /// <summary>More addresses to tell, besides the enabled administrators (comma or line separated).</summary>
    public string ExtraRecipients { get; set; } = "";
    /// <summary>The problems in the last alert (keys, sorted), empty when all was fine.</summary>
    public string LastProblems { get; set; } = "";
    public DateTime? LastAlertAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public IReadOnlyList<string> Extra() =>
        ExtraRecipients.Split([',', ';', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => e.Contains('@') && e.Length <= 254).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();

    /// <summary>Whether to email now: the problems changed, or they are still there a day after the last email.</summary>
    public bool ShouldAlert(string problems, DateTime now) =>
        problems != LastProblems ? problems.Length > 0 || LastProblems.Length > 0 : problems.Length > 0 && (LastAlertAt == null || now - LastAlertAt >= TimeSpan.FromDays(1));
}
