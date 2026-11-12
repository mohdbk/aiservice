using System.Text.Json;
using System.Text.RegularExpressions;
using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Assessments;

public sealed record Trait(string Code, string NameAr, string NameEn);

/// <summary>Average and spread of a trait's item mean (1 to 5) in a reference group, to turn scores into percentiles and stens.</summary>
public sealed record TraitNorm(double Mean, double Sd);

/// <summary>
/// Where each band of a workplace Big Five dimension (a score out of 25) starts, set by HR: by default Low from 6, Moderate from
/// 11, High from 16 and Very High from 21 (Very Low below Low).
/// </summary>
public sealed record BandCutoffs(int Low, int Moderate, int High, int VeryHigh)
{
    public static readonly BandCutoffs Default = new(6, 11, 16, 21);

    public void Validate()
    {
        if (!(Low > 5 && Low < Moderate && Moderate < High && High < VeryHigh && VeryHigh <= 25)) throw DomainException.Invalid("band_cutoffs_invalid");
    }

    public override string ToString() => $"{Low},{Moderate},{High},{VeryHigh}";

    public static BandCutoffs Parse(string? s)
    {
        var p = (s ?? "").Split(',', StringSplitOptions.TrimEntries);
        if (p.Length == 4 && p.All(x => int.TryParse(x, System.Globalization.CultureInfo.InvariantCulture, out _)))
        {
            var c = new BandCutoffs(int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture), int.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture));
            try { c.Validate(); return c; } catch (DomainException) { }
        }
        return Default;
    }
}

/// <summary>
/// One trait's result. Sum and MaxSum are set for the workplace questionnaires (sum of the 1 to 5 answers, as HR scores them on paper).
/// Bands: Low, Average or High (sten models); VeryLow, Low, Moderate, High or VeryHigh (WorkplaceBigFive); empty for type poles.
/// </summary>
public sealed record TraitScore(
    string Code, string NameAr, string NameEn, int Items, int Answered, double RawMean, double Score, double? Percentile, int Sten, string Band,
    int? Sum = null, int? MaxSum = null);

/// <summary>One of the four preference pairs of the 16-type questionnaire and the letter that won.</summary>
public sealed record TypePair(string First, string Second, double FirstMean, double SecondMean, string Letter);

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

    /// <summary>The Ministry's workplace Big Five: five dimensions, five statements each, scored 5 to 25.</summary>
    public static readonly IReadOnlyList<Trait> WorkplaceBigFive =
    [
        new("C", "يقظة الضمير (التخطيط والاعتمادية)", "Conscientiousness (Planning & Reliability)"),
        new("E", "الانبساط (الطاقة والتفاعل)", "Extraversion (Energy & Interaction)"),
        new("A", "الوداعة (العمل الجماعي والتعاون)", "Agreeableness (Teamwork & Cooperation)"),
        new("ES", "الاتزان الانفعالي (إدارة الضغوط)", "Emotional Stability (Stress Management)"),
        new("O", "الانفتاح (الابتكار والتعلم)", "Openness (Innovation & Learning)"),
    ];

    /// <summary>The Ministry's 16-type questionnaire: eight poles in four pairs. Each statement counts towards one pole.</summary>
    public static readonly IReadOnlyList<Trait> WorkplaceType16 =
    [
        new("E", "الانبساط", "Extraversion"), new("I", "الانطواء", "Introversion"),
        new("N", "الحدس", "Intuition"), new("S", "الحس", "Sensing"),
        new("T", "التفكير", "Thinking"), new("F", "الشعور", "Feeling"),
        new("J", "الحكم والتنظيم", "Judging"), new("P", "الإدراك والمرونة", "Perceiving"),
    ];

    /// <summary>The pairs in type order. A tie goes to the second letter (I, S, F, P), as in HR's scoring sheet.</summary>
    public static readonly (string First, string Second)[] TypePairs = [("E", "I"), ("N", "S"), ("T", "F"), ("J", "P")];

    public static readonly string[] ScaleAr = ["لا أوافق بشدة", "لا أوافق", "محايد", "أوافق", "أوافق بشدة"];
    public static readonly string[] ScaleEn = ["Strongly Disagree", "Disagree", "Neutral", "Agree", "Strongly Agree"];

    [GeneratedRegex("^[A-Z][A-Z0-9]{0,9}$")]
    public static partial Regex TraitCode();

    public static IReadOnlyList<Trait> Traits(PersonalityModel model) => model switch
    {
        PersonalityModel.BigFive => BigFive,
        PersonalityModel.SixteenPF => SixteenPF,
        PersonalityModel.WorkplaceBigFive => WorkplaceBigFive,
        PersonalityModel.WorkplaceType16 => WorkplaceType16,
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

    /// <summary>Norms only apply to the sten models; the workplace questionnaires use fixed sum bands.</summary>
    public static bool UsesNorms(PersonalityModel model) => model is PersonalityModel.BigFive or PersonalityModel.SixteenPF;

    public static void ValidateNorms(IReadOnlyDictionary<string, TraitNorm> norms, PersonalityModel model)
    {
        if (!UsesNorms(model) && norms.Count > 0) throw DomainException.Invalid("norms_invalid");
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
    public static IReadOnlyList<TraitScore> Profile(IEnumerable<AttemptQuestion> questions, PersonalityModel model, IReadOnlyDictionary<string, TraitNorm> norms,
        BandCutoffs? cutoffs = null)
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
            if (!UsesNorms(model))
            {
                var stenW = (int)Math.Clamp(Math.Round(1 + (mean - 1) / 4 * 9, MidpointRounding.AwayFromZero), 1, 10);
                var bandW = model == PersonalityModel.WorkplaceBigFive ? SumBand(mean * 5, cutoffs) : "";
                result.Add(new TraitScore(trait.Code, trait.NameAr, trait.NameEn, mine.Count, values.Count, Math.Round(mean, 2), score, null, stenW, bandW,
                    (int)values.Sum(), values.Count * 5));
                continue;
            }
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

    /// <summary>
    /// HR's interpretation of a dimension score out of 25, by default 21 to 25 Very High, 16 to 20 High, 11 to 15 Moderate, 6 to 10
    /// Low, 5 Very Low (the cut-offs are set in the backoffice). With unanswered statements the score is the answered mean times
    /// five, so the bands keep their meaning.
    /// </summary>
    public static string SumBand(double outOf25, BandCutoffs? cutoffs = null)
    {
        var c = cutoffs ?? BandCutoffs.Default;
        var v = Math.Round(outOf25, MidpointRounding.AwayFromZero);
        return v >= c.VeryHigh ? "VeryHigh" : v >= c.High ? "High" : v >= c.Moderate ? "Moderate" : v >= c.Low ? "Low" : "VeryLow";
    }

    /// <summary>A dimension score out of 25 (the sum when all five statements are answered).</summary>
    public static double OutOf25(TraitScore t) => t.Answered == 5 && t.Sum is { } s ? s : Math.Round(t.RawMean * 5, 2);

    public static readonly string[] SumBands = ["VeryLow", "Low", "Moderate", "High", "VeryHigh"];

    /// <summary>
    /// The 4-letter type from a WorkplaceType16 profile, or null when a pair has no answers. Each pair compares the average answer
    /// of its two poles (the same as comparing sums when both poles have the same number of statements); a tie goes to I, S, F or P.
    /// </summary>
    public static (string Type, IReadOnlyList<TypePair> Pairs)? TypeOf(IReadOnlyList<TraitScore> profile)
    {
        var pairs = new List<TypePair>();
        foreach (var (first, second) in TypePairs)
        {
            var a = profile.FirstOrDefault(t => t.Code == first);
            var b = profile.FirstOrDefault(t => t.Code == second);
            if (a == null || b == null) return null;
            pairs.Add(new TypePair(first, second, a.RawMean, b.RawMean, a.RawMean > b.RawMean ? first : second));
        }
        return (string.Concat(pairs.Select(p => p.Letter)), pairs);
    }

    [GeneratedRegex("^[EI][NS][TF][JP]$")]
    public static partial Regex TypeCode();

    /// <summary>Standard normal cumulative distribution (Abramowitz and Stegun 26.2.17, error below 1e-7).</summary>
    public static double NormalCdf(double z)
    {
        var t = 1 / (1 + 0.2316419 * Math.Abs(z));
        var d = 0.3989422804014327 * Math.Exp(-z * z / 2);
        var p = d * t * (0.319381530 + t * (-0.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
        return z >= 0 ? 1 - p : p;
    }
}
