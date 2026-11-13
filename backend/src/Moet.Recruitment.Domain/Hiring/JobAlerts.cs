namespace Moet.Recruitment.Domain.Hiring;

/// <summary>
/// A candidate's saved search: words to look for (any of them, in the job's title or description, in any language), and
/// optionally a department and a location. New matching jobs are emailed once each.
/// </summary>
public class JobAlert
{
    public int Id { get; set; }
    public int ApplicantAccountId { get; set; }
    public string Keywords { get; set; } = "";
    public int? DepartmentId { get; set; }
    public int? LocationId { get; set; }
    public string LanguageCode { get; set; } = "ar";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSentAt { get; set; }

    public const int MaxPerAccount = 5;

    public IReadOnlyList<string> Terms() =>
        Keywords.Split([',', ';', '،', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(t => t.Length >= 2).Distinct().Take(10).ToList();

    /// <summary>Whether a job matches: department and location when set, and any word when there are words.</summary>
    public bool Matches(int? departmentId, int? locationId, IEnumerable<string> texts)
    {
        if (DepartmentId != null && DepartmentId != departmentId) return false;
        if (LocationId != null && LocationId != locationId) return false;
        var terms = Terms();
        if (terms.Count == 0) return true;
        var all = string.Join("\n", texts);
        return terms.Any(t => all.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>A job already emailed for an alert (each job goes once per alert).</summary>
public class JobAlertSent
{
    public int JobAlertId { get; set; }
    public int JobOpeningId { get; set; }
    public DateTime SentAt { get; set; }
}
