using System.Text.Json;
using System.Text.RegularExpressions;
using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Assessments;

public sealed record Trait(string Code, string NameAr, string NameEn);

/// <summary>Average and spread of a trait's item mean (1 to 5) in a reference group, to turn scores into percentiles and stens.</summary>
public sealed record TraitNorm(double Mean, double Sd);

public sealed record TraitScore(
    string Code, string NameAr, string NameEn, int Items, int Answered, double RawMean, double Score, double? Percentile, int Sten, string Band);

/// <summary>
/// Personality questionnaires: agreement-scale statements (Likert, 1 to 5) keyed to traits, some reverse-scored.
/// There are no right answers and no pass mark; a completed questionnaire gives a trait profile for people to read
/// alongside interviews and other evidence.
/// </summary>
public static partial class Personality
{
    public static readonly IReadOnlyList<Trait> BigFive =
    [
        new("E", "الانبساط", "Extraversion"),
        new("A", "الوداعة والتعاون", "Agreeableness"),
        new("C", "يقظة الضمير", "Conscientiousness"),
        new("ES", "الاتزان الانفعالي", "Emotional Stability"),
        new("O", "الانفتاح والخيال", "Openness and Intellect"),
    ];

    /// <summary>The 16 primary factors. The questions themselves are licensed by the publisher and are added by HR.</summary>
    public static readonly IReadOnlyList<Trait> SixteenPF =
    [
        new("A", "الدفء", "Warmth"), new("B", "الاستدلال", "Reasoning"), new("C", "الاستقرار الانفعالي", "Emotional Stability"),
        new("E", "الهيمنة", "Dominance"), new("F", "الحيوية", "Liveliness"), new("G", "الالتزام بالقواعد", "Rule-Consciousness"),
        new("H", "الجرأة الاجتماعية", "Social Boldness"), new("I", "الحساسية", "Sensitivity"), new("L", "اليقظة والحذر", "Vigilance"),
        new("M", "التجريد", "Abstractedness"), new("N", "التحفظ", "Privateness"), new("O", "القلق والتوجس", "Apprehension"),
        new("Q1", "الانفتاح على التغيير", "Openness to Change"), new("Q2", "الاعتماد على الذات", "Self-Reliance"),
        new("Q3", "الدقة والكمال", "Perfectionism"), new("Q4", "التوتر", "Tension"),
    ];

    public static readonly string[] ScaleAr = ["لا أوافق بشدة", "لا أوافق", "محايد", "أوافق", "أوافق بشدة"];
    public static readonly string[] ScaleEn = ["Strongly Disagree", "Disagree", "Neutral", "Agree", "Strongly Agree"];

    [GeneratedRegex("^[A-Z][A-Z0-9]{0,9}$")]
    public static partial Regex TraitCode();

    public static IReadOnlyList<Trait> Traits(PersonalityModel model) => model switch
    {
        PersonalityModel.BigFive => BigFive,
        PersonalityModel.SixteenPF => SixteenPF,
        _ => [],
    };

    public static Dictionary<string, TraitNorm> ParseNorms(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var result = new Dictionary<string, TraitNorm>();
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.TryGetProperty("mean", out var m) && p.Value.TryGetProperty("sd", out var s) && m.TryGetDouble(out var mean) && s.TryGetDouble(out var sd))
                    result[p.Name] = new TraitNorm(mean, sd);
            return result;
        }
        catch (JsonException) { throw DomainException.Invalid("norms_invalid"); }
    }

    public static void ValidateNorms(IReadOnlyDictionary<string, TraitNorm> norms, PersonalityModel model)
    {
        var codes = Traits(model).Select(t => t.Code).ToHashSet();
        foreach (var (code, n) in norms)
            if (!codes.Contains(code) || n.Mean is < 1 or > 5 || n.Sd is <= 0 or > 4) throw DomainException.Invalid("norms_invalid", new Dictionary<string, object?> { ["trait"] = code });
    }

    /// <summary>The answer as 1 to 5 after reverse keying, or null when not answered.</summary>
    public static int? ItemValue(AttemptQuestion q)
    {
        var chosen = q.Options.FirstOrDefault(o => o.IsSelected);
        if (chosen == null || !int.TryParse(chosen.OptionKey, out var v) || v is < 1 or > 5) return null;
        return q.ReverseKeyed ? 6 - v : v;
    }

    /// <summary>
    /// Scores each trait of the model from the attempt's agreement items:
    /// <list type="bullet">
    /// <item>raw mean of the answered items (1 to 5) and a 0 to 100 scale score;</item>
    /// <item>with norms, a percentile (normal approximation) and a sten from the z score; without norms, a sten from the scale score;</item>
    /// <item>a band: Low (sten 1 to 3), Average (4 to 7) or High (8 to 10).</item>
    /// </list>
    /// Traits with no answered items are left out.
    /// </summary>
    public static IReadOnlyList<TraitScore> Profile(IEnumerable<AttemptQuestion> questions, PersonalityModel model, IReadOnlyDictionary<string, TraitNorm> norms)
    {
        var items = questions.Where(q => q.QuestionType == QuestionType.Likert && q.Trait.Length > 0).ToList();
        var result = new List<TraitScore>();
        foreach (var trait in Traits(model))
        {
            var mine = items.Where(q => q.Trait == trait.Code).ToList();
            var values = mine.Select(ItemValue).Where(v => v != null).Select(v => (double)v!.Value).ToList();
            if (values.Count == 0) continue;
            var mean = values.Average();
            var score = Math.Round((mean - 1) / 4 * 100, 1);
            double? percentile = null;
            int sten;
            if (norms.TryGetValue(trait.Code, out var n))
            {
                var z = (mean - n.Mean) / n.Sd;
                percentile = Math.Round(NormalCdf(z) * 100, 0);
                sten = (int)Math.Clamp(Math.Round(2 * z + 5.5, MidpointRounding.AwayFromZero), 1, 10);
            }
            else sten = (int)Math.Clamp(Math.Round(1 + (mean - 1) / 4 * 9, MidpointRounding.AwayFromZero), 1, 10);
            var band = sten <= 3 ? "Low" : sten >= 8 ? "High" : "Average";
            result.Add(new TraitScore(trait.Code, trait.NameAr, trait.NameEn, mine.Count, values.Count, Math.Round(mean, 2), score, percentile, sten, band));
        }
        return result;
    }

    /// <summary>Standard normal cumulative distribution (Abramowitz and Stegun 26.2.17, error below 1e-7).</summary>
    public static double NormalCdf(double z)
    {
        var t = 1 / (1 + 0.2316419 * Math.Abs(z));
        var d = 0.3989422804014327 * Math.Exp(-z * z / 2);
        var p = d * t * (0.319381530 + t * (-0.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
        return z >= 0 ? 1 - p : p;
    }
}
