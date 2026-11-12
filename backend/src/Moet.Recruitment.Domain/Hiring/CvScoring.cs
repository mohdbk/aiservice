using System.Globalization;
using System.Text.RegularExpressions;

namespace Moet.Recruitment.Domain.Hiring;

/// <summary>Pure rules for CV screening: redaction before the model sees a CV, experience, and the weighted score.</summary>
public static partial class CvScoring
{
    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+")] private static partial Regex EmailRe();
    [GeneratedRegex(@"\+?\d[\d\s\-()]{7,}\d")] private static partial Regex PhoneRe();
    [GeneratedRegex(@"[\w؀-ۿ]{3,}")] private static partial Regex NameTokenRe();

    const string SensitiveLabels =
        @"nationality|citizenship|date of birth|d\.o\.b|dob|age|gender|sex|marital status|religion|passport(?: no\.?| number)?|emirates id|" +
        "الجنسية|تاريخ الميلاد|العمر|الجنس|الحالة الاجتماعية|الديانة|رقم الجواز|رقم الهوية";

    static readonly Regex LabelRe = new($@"(?im)^(\s*(?:{SensitiveLabels})\s*[:：\-]).*$", RegexOptions.Compiled);

    /// <summary>Removes the applicant's name, contacts and labelled personal attributes (nationality, age and so on).</summary>
    public static string Redact(string? text, string? applicantName)
    {
        var t = EmailRe().Replace(text ?? "", "[EMAIL]");
        t = PhoneRe().Replace(t, m => m.Value.Count(char.IsDigit) >= 9 ? "[PHONE]" : m.Value);
        foreach (var token in NameTokenRe().Matches(applicantName ?? "").Select(m => m.Value).Distinct().OrderByDescending(x => x.Length))
            t = Regex.Replace(t, Regex.Escape(token), "[CANDIDATE]", RegexOptions.IgnoreCase);
        return LabelRe.Replace(t, "$1 [REDACTED]");
    }

    public sealed record Criterion(string Text, decimal Weight, bool MustHave);

    public sealed record CriterionResult(string Text, decimal Weight, bool MustHave, int Score, string Evidence);

    /// <summary>Weights come from the job's criteria (in code), never from the model. Score is 0 to 100.</summary>
    public static (IReadOnlyList<CriterionResult> Rows, decimal Total, IReadOnlyList<string> Gaps) Combine(
        IReadOnlyList<Criterion> criteria, IReadOnlyList<(int Score, string Evidence)> items)
    {
        var rows = criteria.Select((c, i) =>
        {
            var (score, evidence) = i < items.Count ? items[i] : (0, "Not returned by the model");
            return new CriterionResult(c.Text, c.Weight, c.MustHave, Math.Clamp(score, 0, 10), evidence);
        }).ToList();
        var totalWeight = rows.Sum(r => r.Weight);
        if (totalWeight <= 0) totalWeight = 1;
        var total = Math.Round(rows.Sum(r => r.Weight * r.Score) / (10 * totalWeight) * 100, 1);
        var gaps = rows.Where(r => r.MustHave && r.Score < 5).Select(r => r.Text).ToList();
        return (rows, total, gaps);
    }

    static readonly string[] PresentWords = ["present", "current", "now", "today", "حتى الآن", "الآن"];

    static (int Year, int Month)? YearMonth(string? s)
    {
        s = (s ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0) return null;
        var m = Regex.Match(s, @"^(\d{4})(?:[-/.](\d{1,2}))?");
        if (m.Success) return (int.Parse(m.Groups[1].Value), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1);
        m = Regex.Match(s, @"^(\d{1,2})[-/.](\d{4})$");
        if (m.Success) return (int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value));
        m = Regex.Match(s, @"^([a-z]{3})[a-z]*\.?\s+(\d{4})$");
        if (m.Success)
        {
            var months = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames.Take(12).Select(x => x.ToLowerInvariant()).ToList();
            var i = months.IndexOf(m.Groups[1].Value);
            if (i >= 0) return (int.Parse(m.Groups[2].Value), i + 1);
        }
        return null;
    }

    /// <summary>Total years across roles, merging overlaps. A missing or "present" end date means today.</summary>
    public static decimal YearsOfExperience(IEnumerable<(string Start, string End)> work, DateOnly today)
    {
        var spans = new List<(int A, int B)>();
        foreach (var (s, e) in work)
        {
            var start = YearMonth(s);
            if (start == null) continue;
            var endRaw = (e ?? "").Trim().ToLowerInvariant();
            var end = endRaw.Length == 0 || PresentWords.Contains(endRaw) ? (today.Year, today.Month) : YearMonth(endRaw);
            if (end == null) continue;
            int a = start.Value.Year * 12 + start.Value.Month - 1, b = end.Value.Item1 * 12 + end.Value.Item2;
            if (b > a) spans.Add((a, b));
        }
        spans.Sort();
        var total = 0;
        (int A, int B)? cur = null;
        foreach (var (a, b) in spans)
        {
            if (cur != null && a <= cur.Value.B) cur = (cur.Value.A, Math.Max(cur.Value.B, b));
            else
            {
                if (cur != null) total += cur.Value.B - cur.Value.A;
                cur = (a, b);
            }
        }
        if (cur != null) total += cur.Value.B - cur.Value.A;
        return Math.Round(total / 12m, 1);
    }

    /// <summary>Dated roles are preferred; a figure stated in the CV is used when dates are missing or clearly incomplete.</summary>
    public static decimal ResolveExperience(decimal fromDates, decimal stated)
    {
        fromDates = Math.Max(0, fromDates);
        stated = Math.Max(0, stated);
        if (fromDates > 0 && (stated == 0 || fromDates >= 0.8m * stated)) return Math.Round(fromDates, 1);
        return stated > 0 ? Math.Round(Math.Max(stated, fromDates), 1) : 0;
    }
}
