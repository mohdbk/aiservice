using System.Globalization;
using System.Text.RegularExpressions;

namespace Moet.Hrms.Domain.Hiring;

/// <summary>Pure rules for CV screening: redaction before the model sees a CV, experience, and the weighted score.</summary>
public static partial class CvScoring
{
    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+")] private static partial Regex EmailRe();
    [GeneratedRegex(@"\+?\d[\d\s\-()]{7,}\d")] private static partial Regex PhoneRe();
    [GeneratedRegex(@"[\w؀-ۿ]{3,}")] private static partial Regex NameTokenRe();

    const string SensitiveLabels =
        @"nationality|citizenship|date of birth|d\.o\.b|dob|birth ?date|place of birth|birth ?place|age|gender|sex|marital status|family status|" +
        @"religion|sect|passport(?: no\.?| number)?|emirates id|visa(?: status)?|residency(?: status)?|dependents|number of children|children|spouse|" +
        "الجنسية|تاريخ الميلاد|مكان الميلاد|العمر|الجنس|النوع|الحالة الاجتماعية|الحالة العائلية|الديانة|الدين|المذهب|رقم الجواز|رقم الهوية|التأشيرة|الإقامة|عدد الأطفال|الزوج|الزوجة";

    static readonly Regex LabelRe = new($@"(?im)^(\s*(?:{SensitiveLabels})\s*[:：\-]).*$", RegexOptions.Compiled);
    // The same labels in the middle of a line ("Nationality: X | Gender: Y"): the value up to the next separator.
    static readonly Regex InlineLabelRe = new($@"(?i)(?<![\p{{L}}])((?:{SensitiveLabels})\s*[:：])\s*[^\n|;,•]{{1,40}}", RegexOptions.Compiled);
    // Short "personal details" lines that are only a gender, marital status or age.
    static readonly Regex PersonalWordRe = new(@"(?im)^\s*(male|female|married|single|divorced|widowed|\d{2}\s*years?\s*old|ذكر|أنثى|متزوج|متزوجة|أعزب|عزباء|مطلق|مطلقة|أرمل|أرملة)\s*$", RegexOptions.Compiled);
    static readonly Regex AgeRe = new(@"(?i)\b\d{2}\s*(?:years?|yrs?)\s*old\b|\bborn\s+(?:on|in)\s+[^\n,;|]{1,30}", RegexOptions.Compiled);
    static readonly Regex TitleRe = new(@"(?i)\b(?:mr|mrs|ms|miss)\.?\s+|(?:السيد|السيدة|الآنسة)\s+", RegexOptions.Compiled);

    /// <summary>Removes the applicant's name, contacts and labelled personal attributes (nationality, age and so on).</summary>
    public static string Redact(string? text, string? applicantName)
    {
        var t = EmailRe().Replace(text ?? "", "[EMAIL]");
        t = PhoneRe().Replace(t, m => m.Value.Count(char.IsDigit) >= 9 ? "[PHONE]" : m.Value);
        foreach (var token in NameTokenRe().Matches(applicantName ?? "").Select(m => m.Value).Distinct().OrderByDescending(x => x.Length))
            t = Regex.Replace(t, $@"(?<![\p{{L}}]){Regex.Escape(token)}(?![\p{{L}}])", "[CANDIDATE]", RegexOptions.IgnoreCase);
        t = LabelRe.Replace(t, "$1 [REDACTED]");
        t = InlineLabelRe.Replace(t, "$1 [REDACTED]");
        t = PersonalWordRe.Replace(t, "[REDACTED]");
        t = AgeRe.Replace(t, "[REDACTED]");
        return TitleRe.Replace(t, "");
    }

    public static readonly string[] Categories = ["Qualification", "Experience", "Skill", "Certification", "Language", "Other"];

    /// <summary>Weights come from Essential or Desirable and Standard or High importance, never typed by hand.</summary>
    public static decimal WeightFor(bool essential, bool high) => essential ? (high ? 4 : 3) : (high ? 2 : 1);

    public static string CategoryOf(string? c) => Categories.FirstOrDefault(x => string.Equals(x, c?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Other";

    public sealed record Criterion(string Text, decimal Weight, bool MustHave, string Category = "Other", int? MinYears = null, string Level = "", string Guide = "")
    {
        /// <summary>The requirement as one line for the model and the record, for example "Experience: Data analysis (at least 3 years)".</summary>
        public string Describe() =>
            (Category == "Other" ? "" : $"{Category}: ") + Text
            + (Category == "Experience" && MinYears is > 0 ? $" (at least {MinYears} years)" : "")
            + (Category == "Language" && Level.Length > 0 ? $" ({Level})" : "");
    }

    /// <summary>What the model returned for one requirement.</summary>
    public sealed record ModelItem(int Score, string Evidence, string Quote = "", string Status = "", IReadOnlyList<int>? Roles = null, string WhyNotHigher = "");

    public sealed record CriterionResult(string Text, decimal Weight, bool MustHave, int Score, string Evidence, string Category = "Other",
        string Status = "", string Quote = "", bool? QuoteFound = null, decimal? RelevantYears = null, string WhyNotHigher = "");

    public sealed record Outcome(IReadOnlyList<CriterionResult> Rows, decimal Total, IReadOnlyList<string> Gaps, IReadOnlyList<string> ReviewReasons);

    /// <summary>Weights come from the job's criteria (in code), never from the model. Score is 0 to 100.</summary>
    public static (IReadOnlyList<CriterionResult> Rows, decimal Total, IReadOnlyList<string> Gaps) Combine(
        IReadOnlyList<Criterion> criteria, IReadOnlyList<(int Score, string Evidence)> items)
    {
        var o = Evaluate(criteria, items.Select(i => new ModelItem(i.Score, i.Evidence)).ToList(), [], DateOnly.MinValue, null);
        return (o.Rows, o.Total, o.Gaps);
    }

    /// <summary>
    /// Checks the model's answer against the CV and the rules, requirement by requirement:
    /// a quote that is not in the CV caps the score and asks for a person to look; no quote caps it lower;
    /// experience with a minimum is worked out from the dates of the roles the model marked relevant;
    /// essential requirements end as Met, NotMet or Unclear (Unclear asks for a person, NotMet is a gap).
    /// cvText null skips the quote check (older callers).
    /// </summary>
    public static Outcome Evaluate(IReadOnlyList<Criterion> criteria, IReadOnlyList<ModelItem> items,
        IReadOnlyList<(string Start, string End)> roles, DateOnly today, string? cvText)
    {
        var review = new List<string>();
        var rows = criteria.Select((c, i) =>
        {
            var m = i < items.Count ? items[i] : new ModelItem(0, "Not returned by the model");
            var score = Math.Clamp(m.Score, 0, 10);
            var quote = (m.Quote ?? "").Trim();
            bool? found = null;
            if (cvText != null)
            {
                found = quote.Length > 0 && QuoteInText(quote, cvText);
                if (quote.Length > 0 && found == false) { score = Math.Min(score, 3); review.Add($"Evidence not found in the CV: {c.Text}"); }
                else if (quote.Length == 0) score = Math.Min(score, 2);
            }
            decimal? years = null;
            var status = "";
            if (c.Category == "Experience" && c.MinYears is > 0 && m.Roles is { Count: > 0 } picked)
            {
                var chosen = picked.Where(n => n >= 1 && n <= roles.Count).Distinct().Select(n => roles[n - 1]).ToList();
                if (chosen.Count > 0)
                {
                    years = YearsOfExperience(chosen, today);
                    if (years < c.MinYears) score = Math.Min(score, (int)Math.Round(6m * years.Value / c.MinYears.Value));
                }
            }
            if (c.MustHave && (cvText != null || !string.IsNullOrEmpty(m.Status)))
            {
                var said = (m.Status ?? "").Trim().ToLowerInvariant().Replace(" ", "_");
                status = said switch { "met" => "Met", "not_met" or "notmet" => "NotMet", _ => "Unclear" };
                // Code has the last word on minimum years, and a quote that is not in the CV cannot make a requirement met.
                if (c.Category == "Experience" && c.MinYears is > 0 && years != null) status = years >= c.MinYears ? "Met" : "NotMet";
                else if (c.Category == "Experience" && c.MinYears is > 0 && status == "Met") status = "Unclear";
                if (status == "Met" && found == false) status = "Unclear";
                if (status == "Unclear") review.Add($"Essential requirement unclear: {c.Text}");
            }
            return new CriterionResult(c.Text, c.Weight, c.MustHave, score, m.Evidence, c.Category, status, quote, found, years,
                score >= 10 ? "" : (m.WhyNotHigher ?? "").Trim());
        }).ToList();
        var gaps = rows.Where(r => r.MustHave && (r.Status == "NotMet" || (r.Status == "" && r.Score < 5))).Select(r => r.Text).ToList();
        return new Outcome(rows, Total(rows.Select(r => (r.Weight, r.Score))), gaps, review.Distinct().ToList());
    }

    /// <summary>The overall 0 to 100 from weights and 0 to 10 scores (recruiters' overrides included by the caller).</summary>
    public static decimal Total(IEnumerable<(decimal Weight, int Score)> rows)
    {
        var list = rows.ToList();
        var totalWeight = list.Sum(r => r.Weight);
        if (totalWeight <= 0) totalWeight = 1;
        return Math.Round(list.Sum(r => r.Weight * r.Score) / (10 * totalWeight) * 100, 1);
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")] private static partial Regex NonWordRe();

    static string Words(string s) => NonWordRe().Replace(s.ToLowerInvariant(), " ").Trim();

    /// <summary>
    /// Whether a quote really appears in the CV, ignoring case, spacing and punctuation. A long quote also counts when
    /// a run of eight of its words appears in order (models often trim or join lines).
    /// </summary>
    public static bool QuoteInText(string quote, string text)
    {
        var q = Words(quote);
        if (q.Length == 0) return false;
        var t = " " + Words(text) + " ";
        if (t.Contains(" " + q + " ", StringComparison.Ordinal) || t.Contains(q, StringComparison.Ordinal) && q.Length >= 12) return true;
        var w = q.Split(' ');
        if (w.Length < 8) return false;
        for (var i = 0; i + 8 <= w.Length; i++)
            if (t.Contains(" " + string.Join(' ', w, i, 8) + " ", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// Two scorings of the same CV merged: scores averaged; a requirement whose scores differ by more than 2 points asks
    /// for a person to look. The first scoring's words (evidence, quote, status) are kept.
    /// </summary>
    public static (IReadOnlyList<ModelItem> Items, IReadOnlyList<string> Disagreements) Merge(IReadOnlyList<ModelItem> a, IReadOnlyList<ModelItem> b, IReadOnlyList<Criterion> criteria)
    {
        var notes = new List<string>();
        var items = a.Select((x, i) =>
        {
            if (i >= b.Count) return x;
            if (Math.Abs(x.Score - b[i].Score) > 2) notes.Add($"Two scorings disagree on: {(i < criteria.Count ? criteria[i].Text : $"requirement {i + 1}")} ({x.Score} and {b[i].Score})");
            var status = x.Status == b[i].Status ? x.Status : "unclear";
            return x with { Score = (int)Math.Round((x.Score + b[i].Score) / 2m, MidpointRounding.AwayFromZero), Status = status };
        }).ToList();
        return (items, notes);
    }

    /// <summary>"poor" when the text read from the CV is short or mostly not words (a weak scan); otherwise "good".</summary>
    public static string ReadQuality(string text, string method)
    {
        var t = (text ?? "").Trim();
        if (t.Length < 400) return "poor";
        var letters = t.Count(char.IsLetter);
        var visible = t.Count(c => !char.IsWhiteSpace(c));
        if (visible == 0 || (decimal)letters / visible < 0.6m) return "poor";
        var tokens = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (method == "ocr" && tokens.Length > 0 && (decimal)tokens.Count(x => x.Length == 1) / tokens.Length > 0.25m) return "poor";
        return "good";
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
