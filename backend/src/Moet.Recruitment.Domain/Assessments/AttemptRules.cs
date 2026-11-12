using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Assessments;

/// <summary>Pure rules for attempts: timing, starting, automatic scoring and result policy.</summary>
public static partial class AttemptRules
{
    [GeneratedRegex(@"\S+")] private static partial Regex WordRe();
    [GeneratedRegex(@"[ً-ْـ]")] private static partial Regex DiacriticsRe();
    [GeneratedRegex(@"[^\w\s.-]")] private static partial Regex PunctRe();
    [GeneratedRegex(@"\s+")] private static partial Regex SpaceRe();

    public static int WordCount(string? text) => WordRe().Matches(text ?? "").Count;

    /// <summary>Seconds a section lasts with extra time; 0 means untimed.</summary>
    public static int SectionSeconds(int minutes, int extraTimePercent) =>
        minutes <= 0 ? 0 : (int)Math.Round(minutes * 60 * (1 + Math.Max(0, extraTimePercent) / 100.0));

    public static string CanStart(InvitationStatus status, int used, int allowed, bool deadlinePassed)
    {
        if (status == InvitationStatus.Cancelled) return "assessment_cancelled";
        if (deadlinePassed) return "assessment_expired";
        if (used >= allowed) return "no_attempts_left";
        return "";
    }

    public static decimal? PickResult(IReadOnlyList<decimal?> scores, ScorePolicy policy)
    {
        var s = scores.Where(x => x != null).Select(x => x!.Value).ToList();
        if (s.Count == 0) return null;
        return policy == ScorePolicy.Highest ? s.Max() : s[^1];
    }

    /// <summary>Comparable form of a short answer: Arabic letter variants and diacritics folded, punctuation dropped.</summary>
    public static string Normalise(string? s)
    {
        var t = (s ?? "").Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        t = DiacriticsRe().Replace(t, "");
        t = t.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ة', 'ه').Replace('ى', 'ي');
        return SpaceRe().Replace(PunctRe().Replace(t, ""), " ");
    }

    /// <summary>Automatic points and whether a person has to mark the answer.</summary>
    public static (decimal Points, bool NeedsMarking) Auto(AttemptQuestion q)
    {
        var pts = q.Points;
        switch (q.QuestionType)
        {
            case QuestionType.Essay:
                return (0, !string.IsNullOrWhiteSpace(q.AnswerText));
            case QuestionType.FileUpload:
                return (0, q.AnswerFileId != null);
            case QuestionType.SingleChoice or QuestionType.TrueFalse:
            {
                var chosen = q.Options.Where(o => o.IsSelected).ToList();
                return (chosen.Count == 1 && chosen[0].IsCorrect ? pts : 0, false);
            }
            case QuestionType.MultipleChoice:
            {
                var good = q.Options.Count(o => o.IsCorrect);
                if (good == 0) return (0, false);
                var right = q.Options.Count(o => o.IsSelected && o.IsCorrect);
                var wrong = q.Options.Count(o => o.IsSelected && !o.IsCorrect);
                return (Math.Round(Math.Max(0m, (right - wrong) / (decimal)good) * pts, 2), false);
            }
            case QuestionType.ShortAnswer:
            {
                if (string.IsNullOrWhiteSpace(q.AnswerText)) return (0, false);
                var given = Normalise(q.AnswerText);
                return (q.AcceptedAnswers.Any(a => a.Answer.Trim().Length > 0 && Normalise(a.Answer) == given) ? pts : 0, false);
            }
            case QuestionType.Number:
            {
                if (string.IsNullOrWhiteSpace(q.AnswerText) || q.AcceptedAnswers.Count == 0) return (0, false);
                if (!decimal.TryParse(q.AnswerText.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var given)
                    || !decimal.TryParse(q.AcceptedAnswers[0].Answer.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var target))
                    return (0, false);
                return (Math.Abs(given - target) <= q.Tolerance ? pts : 0, false);
            }
            default:
                return (0, false);
        }
    }

    public static Random Rng(string seed) =>
        new(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(seed)), 0));
}

/// <summary>Freezes the sections, questions, options and accepted answers of one attempt (with their translations).</summary>
public static class AttemptBuilder
{
    /// <param name="bank">Active questions (with options, accepted answers and translations).</param>
    /// <param name="approvedLangs">Languages whose translation of the test was approved (section titles and instructions).</param>
    public static List<AttemptSection> Build(Assessment test, IReadOnlyList<Question> bank, string seed, IReadOnlyCollection<string> approvedLangs)
    {
        var rng = AttemptRules.Rng(seed);
        var byId = bank.ToDictionary(q => q.Id);
        var used = new HashSet<int>();
        var result = new List<AttemptSection>();
        var n = 0;
        var sectionNo = 0;
        foreach (var sec in test.OrderedSections)
        {
            var picked = new List<(Question Q, decimal Points)>();
            foreach (var item in sec.Items.OrderBy(i => i.SortOrder))
            {
                if (item.QuestionId is { } qid)
                {
                    if (byId.TryGetValue(qid, out var q) && used.Add(q.Id)) picked.Add((q, item.PointsOverride ?? q.Points));
                    continue;
                }
                var pool = bank.Where(q => !used.Contains(q.Id)
                        && (item.DrawCategory.Length == 0 || q.Category == item.DrawCategory)
                        && (item.DrawDifficulty.Length == 0 || q.Difficulty.ToString() == item.DrawDifficulty)
                        && (item.DrawJobFamily.Length == 0 || q.JobFamily == item.DrawJobFamily))
                    .OrderBy(q => q.Id).ToList();
                foreach (var q in Sample(pool, Math.Min(item.DrawCount, pool.Count), rng))
                {
                    used.Add(q.Id);
                    picked.Add((q, item.PointsOverride ?? q.Points));
                }
            }
            if (picked.Count == 0) continue;
            if (test.ShuffleQuestions) Shuffle(picked, rng);
            var aSec = new AttemptSection
            {
                SectionNo = sectionNo++, Title = sec.Title, Instructions = sec.Instructions, Minutes = sec.Minutes, PassMark = sec.PassMark,
                Translations = sec.Translations.Where(t => approvedLangs.Contains(t.LanguageCode))
                    .Select(t => new AttemptSectionTranslation { LanguageCode = t.LanguageCode, Title = t.Title, Instructions = t.Instructions }).ToList(),
            };
            var order = 0;
            foreach (var (q, points) in picked)
            {
                n++;
                var aq = new AttemptQuestion
                {
                    QuestionKey = $"q{n}", SortOrder = order++, QuestionId = q.Id, QuestionType = q.QuestionType, Text = q.Text,
                    Points = points, Tolerance = q.Tolerance, Rubric = q.Rubric, MaxWords = q.MaxWords,
                    Translations = q.Translations.Where(t => t.ReviewStatus == ReviewStatus.Approved)
                        .Select(t => new AttemptQuestionTranslation { LanguageCode = t.LanguageCode, Text = t.Text }).ToList(),
                    AcceptedAnswers = q.AcceptedAnswers.OrderBy(a => a.SortOrder).Select(a => new AttemptAcceptedAnswer { Answer = a.Answer }).ToList(),
                };
                var approved = q.Translations.Where(t => t.ReviewStatus == ReviewStatus.Approved).Select(t => t.LanguageCode).ToHashSet();
                var opts = q.Options.OrderBy(o => o.SortOrder).Select((o, j) => new AttemptOption
                {
                    OptionKey = ((char)('a' + j)).ToString(), Text = o.Text, IsCorrect = o.IsCorrect,
                    Translations = o.Translations.Where(t => approved.Contains(t.LanguageCode) && t.Text.Trim().Length > 0)
                        .Select(t => new AttemptOptionTranslation { LanguageCode = t.LanguageCode, Text = t.Text }).ToList(),
                }).ToList();
                if (test.ShuffleOptions && q.QuestionType is QuestionType.SingleChoice or QuestionType.MultipleChoice) Shuffle(opts, rng);
                for (var j = 0; j < opts.Count; j++) opts[j].SortOrder = j;
                aq.Options = opts;
                aSec.Questions.Add(aq);
            }
            result.Add(aSec);
        }
        return result;
    }

    /// <summary>How many questions an attempt would have (for the test summary and publishing check).</summary>
    public static int CountQuestions(Assessment test, IReadOnlyList<Question> bank) =>
        Build(new Assessment { Sections = test.Sections, ShuffleQuestions = false, ShuffleOptions = false }, bank, "preview", [])
            .Sum(s => s.Questions.Count);

    static IEnumerable<T> Sample<T>(List<T> pool, int count, Random rng)
    {
        var copy = pool.ToList();
        for (var i = 0; i < count; i++)
        {
            var j = rng.Next(i, copy.Count);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy.Take(count);
    }

    static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
