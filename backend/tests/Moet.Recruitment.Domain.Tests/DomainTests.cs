using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Assessments;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Content;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Domain.Tests;

public class TextTests
{
    [Fact]
    public void Languages_and_contacts()
    {
        Assert.True(TextUtil.IsRtl("ar"));
        Assert.True(TextUtil.IsRtl("ur-PK"));
        Assert.False(TextUtil.IsRtl("en"));
        Assert.True(TextUtil.ValidPhone("+971 50 123 4567"));
        Assert.False(TextUtil.ValidPhone("abc"));
        Assert.True(TextUtil.ValidEmail("layla@example.com"));
        Assert.False(TextUtil.ValidEmail("layla@"));
        Assert.Equal("ar", TextUtil.DetectLanguage("مهندس بيانات أول"));
        Assert.Equal("en", TextUtil.DetectLanguage("Senior Data Engineer"));
    }

    [Fact]
    public void Safe_next_path_and_summary()
    {
        Assert.Equal("/ar/jobs", TextUtil.SafeNext("/ar/jobs"));
        Assert.Equal("", TextUtil.SafeNext("//evil.example"));
        Assert.Equal("", TextUtil.SafeNext("https://evil.example"));
        Assert.Equal("One two", TextUtil.Summary("<p>One</p><p>two</p>"));
        Assert.EndsWith("…", TextUtil.Summary(new string('a', 10) + " " + new string('b', 300), 20));
    }

    [Fact]
    public void No_em_dashes_and_stable_reference()
    {
        Assert.DoesNotContain(TextUtil.EmDash, TextUtil.NoEmDash("A \u2014 B\u2014C"));
        var created = new DateTime(2026, 3, 1);
        Assert.Equal(TextUtil.Reference("APP-2026-00001", created), TextUtil.Reference("APP-2026-00001", created));
        Assert.StartsWith("MOET-2026-", TextUtil.Reference("APP-2026-00001", created));
    }
}

public class TranslationReviewTests
{
    [Fact]
    public void Review_rules()
    {
        Assert.Equal((ReviewStatus.Suggested, "h1"), TranslationReview.Decide(false, false, true, null, "h1"));
        Assert.Equal(ReviewStatus.Suggested, TranslationReview.Decide(false, false, false, "h1", "h1").Status);
        Assert.Equal(ReviewStatus.Approved, TranslationReview.Decide(true, false, false, "h1", "h1").Status);
        Assert.Equal(ReviewStatus.Approved, TranslationReview.Decide(false, false, false, "h1", "h2").Status);
        Assert.Equal(ReviewStatus.Approved, TranslationReview.Decide(false, false, false, null, "h2").Status);
        Assert.Equal((ReviewStatus.Suggested, "h3"), TranslationReview.Decide(false, true, false, null, "h3"));
    }

    [Fact]
    public void Approval_records_who()
    {
        var t = new JobOpeningTranslation { LanguageCode = "en", ReviewStatus = ReviewStatus.Suggested, SuggestedHash = "h1" };
        TranslationReview.Apply(t, true, false, "h1", 7, DateTime.UtcNow);
        Assert.Equal(ReviewStatus.Approved, t.ReviewStatus);
        Assert.Equal(7, t.ApprovedByStaffId);
        TranslationReview.Apply(t, false, false, "h1", 7, DateTime.UtcNow);
        Assert.Equal(ReviewStatus.Suggested, t.ReviewStatus);
        Assert.Null(t.ApprovedByStaffId);
    }
}

public class ScreeningTests
{
    static AskedQuestion Q(string key, AnswerType type, bool required = false, RequirementRule? rule = null, string? expected = null, params string[] options)
    {
        var q = new ScreeningQuestion { Id = key.GetHashCode(), Text = key, AnswerType = type };
        var id = 100;
        foreach (var o in options) q.Options.Add(new ScreeningQuestionOption { Id = id++, Text = o, SortOrder = id });
        return new AskedQuestion(key, q, required, rule, expected);
    }

    [Fact]
    public void Requirements_flag_but_do_not_reject()
    {
        var asked = new List<AskedQuestion>
        {
            Q("sq1", AnswerType.YesNo, true, RequirementRule.MustBe, "Yes"),
            Q("sq2", AnswerType.Number, true, RequirementRule.AtLeast, "3"),
            Q("sq3", AnswerType.SingleChoice, false, null, null, "Bachelor", "Master"),
        };
        var raw = new Dictionary<string, IReadOnlyList<string>> { ["sq1"] = ["No"], ["sq2"] = ["5"], ["sq3"] = ["101"] };
        var r = ScreeningRules.Evaluate(asked, raw);
        Assert.Empty(r.Invalid);
        Assert.Empty(r.Missing);
        Assert.Equal(["sq1"], r.NotMet);
        Assert.Equal(ScreeningOutcome.DoesNotMeet, r.Outcome(asked));
        Assert.Equal("Master", r.Answers.Single(a => a.Key == "sq3").Text);
    }

    [Fact]
    public void Invalid_and_missing_answers()
    {
        var asked = new List<AskedQuestion> { Q("sq1", AnswerType.Number, true), Q("sq2", AnswerType.Date, true), Q("sq3", AnswerType.SingleChoice, false, null, null, "A", "B") };
        var r = ScreeningRules.Evaluate(asked, new Dictionary<string, IReadOnlyList<string>> { ["sq1"] = ["many"], ["sq3"] = ["999"] });
        Assert.Equal(["sq1", "sq3"], r.Invalid);
        Assert.Equal(["sq2"], r.Missing);
    }
}

public class AttemptTests
{
    static Question Q(int id, string cat = "excel", QuestionType t = QuestionType.SingleChoice, decimal points = 1)
    {
        var q = new Question { Id = id, Text = $"Q{id}", Category = cat, QuestionType = t, Points = points, Difficulty = id % 2 == 0 ? Difficulty.Easy : Difficulty.Medium };
        if (t is QuestionType.SingleChoice or QuestionType.MultipleChoice)
        {
            q.Options.Add(new QuestionOption { Text = "A", IsCorrect = true, SortOrder = 0, Translations = [new QuestionOptionTranslation { LanguageCode = "en", Text = "A en" }] });
            q.Options.Add(new QuestionOption { Text = "B", SortOrder = 1 });
            q.Options.Add(new QuestionOption { Text = "C", SortOrder = 2 });
        }
        return q;
    }

    static readonly List<Question> Bank = Enumerable.Range(1, 12).Select(i => Q(i, i < 9 ? "excel" : "policy")).ToList();

    static Assessment Test(params Section[] sections) => new() { Sections = sections.ToList(), ShuffleQuestions = true, ShuffleOptions = true, PassMark = 60 };

    static Section Sec(int order, int minutes, decimal pass, params SectionItem[] items) =>
        new() { SortOrder = order, Title = $"S{order}", Minutes = minutes, PassMark = pass, Items = items.ToList() };

    [Fact]
    public void Build_draws_without_repeats_and_keys_run_across_sections()
    {
        var test = Test(Sec(0, 10, 50, new SectionItem { QuestionId = 1 }, new SectionItem { DrawCategory = "excel", DrawCount = 4 }),
            Sec(1, 0, 0, new SectionItem { DrawCategory = "policy", DrawCount = 10 }), Sec(2, 5, 0, new SectionItem { DrawCategory = "none", DrawCount = 3 }));
        var a = AttemptBuilder.Build(test, Bank, "inv:1", []);
        Assert.Equal(2, a.Count);
        var qs = a.SelectMany(s => s.Questions.OrderBy(q => q.SortOrder)).ToList();
        Assert.Equal(qs.Count, qs.Select(q => q.QuestionId).Distinct().Count());
        Assert.Contains(qs, q => q.QuestionId == 1);
        Assert.Equal(Enumerable.Range(1, qs.Count).Select(i => $"q{i}"), qs.Select(q => q.QuestionKey));
        Assert.Equal(4, a[1].Questions.Count);
    }

    [Fact]
    public void Same_seed_same_attempt()
    {
        var test = Test(Sec(0, 10, 0, new SectionItem { DrawCategory = "excel", DrawCount = 5 }));
        var a1 = AttemptBuilder.Build(test, Bank, "inv:1", []).SelectMany(s => s.Questions).Select(q => q.QuestionId).ToList();
        var a2 = AttemptBuilder.Build(test, Bank, "inv:1", []).SelectMany(s => s.Questions).Select(q => q.QuestionId).ToList();
        Assert.Equal(a1, a2);
        var others = Enumerable.Range(2, 6).Select(n => string.Join(",", AttemptBuilder.Build(test, Bank, $"inv:{n}", []).SelectMany(s => s.Questions).Select(q => q.QuestionId))).Distinct();
        Assert.True(others.Count() > 1);
    }

    static Attempt Attempt(params AttemptQuestion[] qs)
    {
        var sec = new AttemptSection { SectionNo = 0, Title = "S", PassMark = 0, Questions = qs.ToList() };
        return new Attempt { Sections = [sec], Status = AttemptStatus.InProgress };
    }

    static AttemptQuestion Choice(string key, QuestionType t, decimal points, params (string Key, bool Correct)[] opts) => new()
    {
        QuestionKey = key, QuestionType = t, Points = points,
        Options = opts.Select((o, i) => new AttemptOption { OptionKey = o.Key, IsCorrect = o.Correct, SortOrder = i }).ToList(),
    };

    [Fact]
    public void Scoring_single_multiple_short_and_number()
    {
        var single = Choice("q1", QuestionType.SingleChoice, 1, ("a", true), ("b", false));
        var multi = Choice("q2", QuestionType.MultipleChoice, 2, ("a", true), ("b", true), ("c", false));
        var shortQ = new AttemptQuestion { QuestionKey = "q3", QuestionType = QuestionType.ShortAnswer, Points = 1, AcceptedAnswers = [new() { Answer = "وزارة الاقتصاد" }] };
        var num = new AttemptQuestion { QuestionKey = "q4", QuestionType = QuestionType.Number, Points = 3, Tolerance = 50, AcceptedAnswers = [new() { Answer = "12,500" }] };
        var a = Attempt(single, multi, shortQ, num);
        var now = DateTime.UtcNow;
        a.SaveAnswer("q1", new AnswerInput(null, ["a"], null, null), now);
        a.SaveAnswer("q2", new AnswerInput(null, ["a"], null, null), now);
        a.SaveAnswer("q3", new AnswerInput("  وزارة الإقتصاد ", null, null, null), now);
        a.SaveAnswer("q4", new AnswerInput("12540", null, null, null), now);
        a.Submit(now);
        Assert.Equal(AttemptStatus.Marked, a.Status);
        Assert.Equal(1 + 1 + 1 + 3, a.Earned);
        Assert.Equal(7, a.Total);
    }

    [Fact]
    public void Essays_wait_for_a_marker_and_marks_are_clamped()
    {
        var essay = new AttemptQuestion { QuestionKey = "q1", QuestionType = QuestionType.Essay, Points = 10 };
        var single = Choice("q2", QuestionType.SingleChoice, 1, ("a", true));
        var a = Attempt(essay, single);
        a.SaveAnswer("q1", new AnswerInput("My answer", null, null, null), DateTime.UtcNow);
        a.SaveAnswer("q2", new AnswerInput(null, ["a"], null, null), DateTime.UtcNow);
        a.Submit(DateTime.UtcNow);
        Assert.Equal(AttemptStatus.Submitted, a.Status);
        essay.Mark(99, "", 1, DateTime.UtcNow);
        a.Score();
        Assert.Equal(AttemptStatus.Marked, a.Status);
        Assert.Equal(11, a.Earned);
        a.DecidePass(60);
        Assert.True(a.Passed);
    }

    [Fact]
    public void Invalid_answers_are_refused()
    {
        var a = Attempt(Choice("q1", QuestionType.MultipleChoice, 1, ("a", true), ("b", false)),
            new AttemptQuestion { QuestionKey = "q2", QuestionType = QuestionType.Number, Points = 1 });
        Assert.Throws<DomainException>(() => a.SaveAnswer("q1", new AnswerInput(null, ["z"], null, null), DateTime.UtcNow));
        Assert.Throws<DomainException>(() => a.SaveAnswer("q2", new AnswerInput("abc", null, null, null), DateTime.UtcNow));
        Assert.Throws<DomainException>(() => a.SaveAnswer("q9", new AnswerInput("x", null, null, null), DateTime.UtcNow));
    }

    [Fact]
    public void Timed_sections_advance_and_submit()
    {
        var a = new Attempt
        {
            Status = AttemptStatus.InProgress,
            Sections =
            [
                new AttemptSection { SectionNo = 0, Minutes = 10, Questions = [Choice("q1", QuestionType.SingleChoice, 1, ("a", true))] },
                new AttemptSection { SectionNo = 1, Minutes = 5, Questions = [Choice("q2", QuestionType.SingleChoice, 1, ("a", true))] },
            ],
        };
        var start = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        a.StartSection(0, 50, start);
        Assert.Equal(start.AddMinutes(15), a.SectionEndsAt);
        Assert.True(a.CatchUp(50, start.AddMinutes(16)));
        Assert.Equal(1, a.CurrentSectionNo);
        a.CatchUp(50, start.AddHours(1));
        Assert.NotEqual(AttemptStatus.InProgress, a.Status);
    }

    [Fact]
    public void Rules_for_starting_and_results()
    {
        Assert.Equal(2250, AttemptRules.SectionSeconds(30, 25));
        Assert.Equal(0, AttemptRules.SectionSeconds(0, 50));
        Assert.Equal("", AttemptRules.CanStart(InvitationStatus.Invited, 0, 1, false));
        Assert.Equal("no_attempts_left", AttemptRules.CanStart(InvitationStatus.Marked, 1, 1, false));
        Assert.Equal("", AttemptRules.CanStart(InvitationStatus.Marked, 1, 2, false));
        Assert.Equal("assessment_expired", AttemptRules.CanStart(InvitationStatus.Invited, 0, 1, true));
        Assert.Equal("assessment_cancelled", AttemptRules.CanStart(InvitationStatus.Cancelled, 0, 1, false));
        Assert.Equal(80, AttemptRules.PickResult([40, 80, 60], ScorePolicy.Highest));
        Assert.Equal(60, AttemptRules.PickResult([40, 80, 60], ScorePolicy.Latest));
    }

    [Fact]
    public void Reexam_gives_more_attempts()
    {
        var inv = new Invitation { Status = InvitationStatus.Expired, AttemptsAllowed = 1, AttemptsUsed = 1 };
        inv.AllowReexam(1, 7, DateTime.UtcNow);
        Assert.Equal(2, inv.AttemptsAllowed);
        Assert.Equal(InvitationStatus.Invited, inv.Status);
        Assert.Equal("", inv.CannotStartReason(DateTime.UtcNow));
    }
}

public class CvScoringTests
{
    [Fact]
    public void Redaction_removes_name_contacts_and_personal_labels()
    {
        var text = "Layla Hassan\nlayla@example.com +971 50 123 4567\nNationality: X\nالجنسية: Y\nPython developer";
        var r = CvScoring.Redact(text, "Layla Hassan");
        Assert.DoesNotContain("Layla", r);
        Assert.DoesNotContain("example.com", r);
        Assert.DoesNotContain("123 4567", r);
        Assert.DoesNotContain(": X", r);
        Assert.DoesNotContain(": Y", r);
        Assert.Contains("Python developer", r);
    }

    [Fact]
    public void Weighted_score_and_gaps()
    {
        var (rows, total, gaps) = CvScoring.Combine(
            [new("Python", 60, true), new("Cloud", 40, false)], [(10, "x"), (5, "y")]);
        Assert.Equal(80, total);
        Assert.Empty(gaps);
        (_, _, gaps) = CvScoring.Combine([new("Python", 60, true), new("Cloud", 40, false)], [(2, "x")]);
        Assert.Equal(["Python"], gaps);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Experience_merges_overlaps()
    {
        var today = new DateOnly(2026, 1, 1);
        Assert.Equal(3.0m, CvScoring.YearsOfExperience([("2018-01", "2020-12")], today));
        Assert.Equal(3.0m, CvScoring.YearsOfExperience([("2018-01", "2020-12"), ("2019-01", "2019-06")], today));
        Assert.Equal(6m, CvScoring.ResolveExperience(0, 6));
        Assert.Equal(5m, CvScoring.ResolveExperience(5, 6));
    }
}

public class ContentTests
{
    [Fact]
    public void Merge_ignores_unknown_and_empty_values()
    {
        var b = new Dictionary<string, string> { ["a"] = "A", ["b"] = "B" };
        var r = ContentService.Merge(b, new Dictionary<string, string> { ["a"] = "أ", ["x"] = "X" }, new Dictionary<string, string> { ["b"] = " " });
        Assert.Equal("أ", r["a"]);
        Assert.Equal("B", r["b"]);
        Assert.False(r.ContainsKey("x"));
    }

    [Fact]
    public void Cv_files_must_match_their_type()
    {
        Assert.Equal("pdf", CvFile.Validate("cv.PDF", "%PDF-1.7"u8.ToArray(), 5));
        var e = Assert.Throws<DomainException>(() => CvFile.Validate("cv.pdf", "PK\u0003\u0004"u8.ToArray(), 5));
        Assert.Equal("cv_type", e.Code);
        Assert.Equal("cv_empty", Assert.Throws<DomainException>(() => CvFile.Validate("cv.pdf", [], 5)).Code);
    }
}

public class AgentComponentTests
{
    static AgentComponent Approval(string action, string nodes) => new()
    {
        Key = "interview_card", Name = "بطاقة المقابلة", Kind = ComponentKind.Approval, ActionName = action, Definition = $$"""{"nodes":{{nodes}}}""",
    };

    [Fact]
    public void Approval_cards_may_only_edit_what_the_action_allows()
    {
        const string ok = """[{"type":"heading","text":{"ar":"مقابلة","en":"Interview"}},{"type":"row","children":[{"type":"field","name":"date","input":"date"},{"type":"field","name":"from","input":"time"}]},{"type":"field","name":"round_id","input":"choice","source":"rounds"}]""";
        Approval("schedule_interview", ok).Validate();

        var e = Assert.Throws<DomainException>(() => Approval("schedule_interview", """[{"type":"field","name":"applicant_id","input":"number"}]""").Validate());
        Assert.Equal("component_field_not_editable", e.Code);
        Assert.Equal("component_action_required", Assert.Throws<DomainException>(() => Approval("delete_all", ok).Validate()).Code);
    }

    [Fact]
    public void Definitions_are_checked()
    {
        var ask = new AgentComponent { Key = "start_date", Name = "Start", Kind = ComponentKind.Ask, ActionName = "add_note", Definition = """{"nodes":[{"type":"field","name":"when","input":"date"}]}""" };
        ask.Validate();
        Assert.Null(ask.ActionName);

        ask.Definition = """{"nodes":[{"type":"field","name":"pick","input":"choice"}]}""";
        Assert.Equal("component_options_required", Assert.Throws<DomainException>(ask.Validate).Code);
        ask.Definition = """{"nodes":[{"type":"field","name":"a","input":"text"},{"type":"field","name":"a","input":"text"}]}""";
        Assert.Equal("component_field_name", Assert.Throws<DomainException>(ask.Validate).Code);
        ask.Definition = """{"nodes":[{"type":"row","children":[]}]}""";
        Assert.Equal("component_row_invalid", Assert.Throws<DomainException>(ask.Validate).Code);
        ask.Definition = """{"nodes":[{"type":"row","children":[{"type":"row","children":[{"type":"field","name":"a","input":"text"}]}]}]}""";
        Assert.Equal("component_invalid", Assert.Throws<DomainException>(ask.Validate).Code);
        ask.Definition = """{"nodes":[{"type":"heading","text":{"ar":"x"}}]}""";
        Assert.Equal("component_needs_field", Assert.Throws<DomainException>(ask.Validate).Code);
        ask.Key = "Bad Key";
        Assert.Equal("component_key_invalid", Assert.Throws<DomainException>(ask.Validate).Code);
    }
}
