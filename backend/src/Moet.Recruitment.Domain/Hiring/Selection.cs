using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Hiring;

/// <summary>
/// How much each part counts in a job's combined selection score, as percentages adding up to 100. The default is
/// assessments (technical tests) 40, interviews 40 and personality fit 20, with the CV score not counted.
/// </summary>
public sealed record SelectionWeights(int Cv, int Tests, int Interview, int Personality)
{
    public static readonly SelectionWeights Default = new(0, 40, 40, 20);

    public void Validate()
    {
        if (Cv < 0 || Tests < 0 || Interview < 0 || Personality < 0 || Cv + Tests + Interview + Personality != 100)
            throw DomainException.Invalid("selection_weights_total");
    }
}

/// <summary>One part of the combined score: its weight and the applicant's score out of 100 (null when there is none yet).</summary>
public sealed record SelectionPart(string Key, int Weight, double? Score);

/// <summary>
/// The combined score out of 100. Parts without a score yet are left out and the others are scaled up, so the total is
/// comparable while an applicant is still in the process; Complete says whether every weighted part has a score.
/// </summary>
public sealed record SelectionScore(double? Total, bool Complete, IReadOnlyList<SelectionPart> Parts);

public static class Selection
{
    public const string Cv = "cv", Tests = "tests", Interview = "interview", Personality = "personality";

    public static SelectionScore Combine(SelectionWeights w, double? cv, double? tests, double? interview, double? personality)
    {
        var parts = new List<SelectionPart>
        {
            new(Cv, w.Cv, Round(cv)), new(Tests, w.Tests, Round(tests)), new(Interview, w.Interview, Round(interview)),
            new(Personality, w.Personality, Round(personality)),
        };
        var counted = parts.Where(p => p.Weight > 0 && p.Score != null).ToList();
        var weight = counted.Sum(p => p.Weight);
        double? total = weight == 0 ? null : Math.Round(counted.Sum(p => p.Weight * p.Score!.Value) / weight, 1);
        return new SelectionScore(total, parts.Where(p => p.Weight > 0).All(p => p.Score != null), parts);
    }

    /// <summary>
    /// The interview part from the panel's scorecards: each panelist's average skill rating out of 5 (or, without ratings,
    /// 100 for Cleared and 0 for Rejected), then the average over panelists so every panelist counts the same.
    /// </summary>
    public static double? InterviewPercent(IEnumerable<(Recommendation Recommendation, IReadOnlyList<byte> Ratings)> feedback)
    {
        var each = feedback.Select(f => f.Ratings.Count > 0 ? f.Ratings.Average(r => (double)r) / 5 * 100 : f.Recommendation == Recommendation.Cleared ? 100 : 0).ToList();
        return each.Count == 0 ? null : each.Average();
    }

    /// <summary>The assessments part: the average result of the technical tests that have one (personality questionnaires are not marked).</summary>
    public static double? TestsPercent(IEnumerable<decimal?> scores)
    {
        var s = scores.Where(x => x != null).Select(x => (double)x!.Value).ToList();
        return s.Count == 0 ? null : s.Average();
    }

    static double? Round(double? v) => v == null ? null : Math.Round(Math.Clamp(v.Value, 0, 100), 1);
}
