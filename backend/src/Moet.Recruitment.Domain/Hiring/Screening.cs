using System.Globalization;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Content;

namespace Moet.Recruitment.Domain.Hiring;

public class ScreeningQuestion
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public AnswerType AnswerType { get; set; } = AnswerType.YesNo;
    public string Category { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string SourceLanguageCode { get; set; } = "ar";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ScreeningQuestionOption> Options { get; set; } = [];
    public List<ScreeningQuestionTranslation> Translations { get; set; } = [];

    public bool HasOptions => AnswerType is AnswerType.SingleChoice or AnswerType.MultipleChoice;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Text)) throw DomainException.Invalid("question_required");
        if (HasOptions && Options.Count(o => o.Text.Trim().Length > 0) < 2) throw DomainException.Invalid("two_options");
        if (!HasOptions) Options.Clear();
    }
}

public class ScreeningQuestionOption
{
    public int Id { get; set; }
    public int ScreeningQuestionId { get; set; }
    public int SortOrder { get; set; }
    public string Text { get; set; } = "";
    public List<ScreeningQuestionOptionTranslation> Translations { get; set; } = [];
}

public class ScreeningQuestionTranslation : ITranslation
{
    public int ScreeningQuestionId { get; set; }
    public string LanguageCode { get; set; } = "";
    public string Text { get; set; } = "";
    public ReviewStatus ReviewStatus { get; set; }
    public string? SuggestedHash { get; set; }
    public int? ApprovedByStaffId { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public class ScreeningQuestionOptionTranslation
{
    public int ScreeningQuestionOptionId { get; set; }
    public string LanguageCode { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>One screening question as asked for one job (with the job's requirement).</summary>
public sealed record AskedQuestion(string Key, ScreeningQuestion Question, bool Required, RequirementRule? Rule, string? Expected)
{
    public const string Yes = "Yes";
    public const string No = "No";

    /// <summary>Stable answer values: Yes/No, or the option ids as text.</summary>
    public IReadOnlyList<string> Values => Question.AnswerType == AnswerType.YesNo
        ? [Yes, No]
        : Question.Options.OrderBy(o => o.SortOrder).Select(o => o.Id.ToString(CultureInfo.InvariantCulture)).ToList();

    public string OptionText(string value) =>
        Question.Options.FirstOrDefault(o => o.Id.ToString(CultureInfo.InvariantCulture) == value)?.Text ?? value;
}

/// <summary>An answer, normalised: Text for display (in the question's original language) and Values for rules.</summary>
public sealed record ScreeningResponse(string Key, IReadOnlyList<string> Values, string Text, bool? Meets);

public sealed record ScreeningEvaluation(
    IReadOnlyList<ScreeningResponse> Answers, IReadOnlyList<string> Missing, IReadOnlyList<string> Invalid, IReadOnlyList<string> NotMet)
{
    public ScreeningOutcome Outcome(IReadOnlyList<AskedQuestion> asked) =>
        asked.Count == 0 ? ScreeningOutcome.NoQuestions
        : !asked.Any(q => q.Rule != null) ? ScreeningOutcome.NoRequirements
        : NotMet.Count > 0 ? ScreeningOutcome.DoesNotMeet : ScreeningOutcome.Meets;
}

/// <summary>Screening rules (pure). Requirements flag applicants for HR; they never reject anyone automatically.</summary>
public static class ScreeningRules
{
    /// <summary>Normalises one raw answer (string or list of strings). Returns null for empty; throws for invalid.</summary>
    public static List<string>? Clean(AskedQuestion q, IReadOnlyList<string> raw)
    {
        var vals = raw.Select(v => (v ?? "").Trim()).Where(v => v.Length > 0).ToList();
        if (vals.Count == 0) return null;
        var t = q.Question.AnswerType;
        switch (t)
        {
            case AnswerType.YesNo:
            case AnswerType.SingleChoice:
                if (vals.Count != 1 || !q.Values.Contains(vals[0])) throw new FormatException();
                return vals;
            case AnswerType.MultipleChoice:
                if (!vals.All(q.Values.Contains)) throw new FormatException();
                return q.Values.Where(vals.Contains).ToList();
            case AnswerType.Number:
                if (vals.Count != 1 || !decimal.TryParse(vals[0].Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
                    || n < 0 || n > 1_000_000_000) throw new FormatException();
                return [n.ToString("0.####", CultureInfo.InvariantCulture)];
            case AnswerType.Date:
                if (vals.Count != 1 || !DateOnly.TryParseExact(vals[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    throw new FormatException();
                return vals;
            case AnswerType.ShortText:
                return [TextUtil.Clip(string.Join(" ", vals), 300)];
            case AnswerType.LongText:
                return [TextUtil.Clip(string.Join("\n", vals), 4000)];
            default:
                throw new FormatException();
        }
    }

    /// <summary>True or false against the job's requirement, or null when there is none.</summary>
    public static bool? Meets(AskedQuestion q, IReadOnlyList<string>? values)
    {
        if (q.Rule == null || string.IsNullOrWhiteSpace(q.Expected)) return null;
        if (values == null || values.Count == 0) return false;
        var expected = q.Expected.Trim();
        if (q.Rule == RequirementRule.MustBe)
        {
            // Expected holds the option id, Yes/No, or a value; compare against stored values and option text.
            return values.Any(v => string.Equals(v, expected, StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(q.OptionText(v), expected, StringComparison.OrdinalIgnoreCase));
        }
        if (!decimal.TryParse(values[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var a)
            || !decimal.TryParse(expected.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var e)) return false;
        return q.Rule == RequirementRule.AtLeast ? a >= e : a <= e;
    }

    public static ScreeningEvaluation Evaluate(IReadOnlyList<AskedQuestion> asked, IReadOnlyDictionary<string, IReadOnlyList<string>> raw)
    {
        var answers = new List<ScreeningResponse>();
        var missing = new List<string>();
        var invalid = new List<string>();
        var notMet = new List<string>();
        foreach (var q in asked)
        {
            List<string>? v;
            try { v = Clean(q, raw.TryGetValue(q.Key, out var r) ? r : []); }
            catch (FormatException) { invalid.Add(q.Key); continue; }
            var meets = Meets(q, v);
            if (v == null)
            {
                if (q.Required) missing.Add(q.Key);
                if (meets == false) notMet.Add(q.Key);
                continue;
            }
            if (meets == false) notMet.Add(q.Key);
            var text = q.Question.AnswerType switch
            {
                AnswerType.SingleChoice or AnswerType.MultipleChoice => string.Join(", ", v.Select(q.OptionText)),
                _ => string.Join(", ", v),
            };
            answers.Add(new ScreeningResponse(q.Key, v, text, meets));
        }
        return new ScreeningEvaluation(answers, missing, invalid, notMet);
    }
}
