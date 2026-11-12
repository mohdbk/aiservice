using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Assessments;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Content;
using Moet.Recruitment.Domain.Hiring;
using static Moet.Recruitment.Application.Ai.CvScoringHandler;

namespace Moet.Recruitment.Application.Ai;

/// <summary>English drafts from the language model. A draft is Suggested and never reaches the portal until HR approves it.</summary>
public abstract class EnglishDraftHandler(ILanguageModel model, IClock clock) : IJobHandler
{
    protected const string En = "en";
    public abstract string JobType { get; }
    public abstract Task RunAsync(int targetId, CancellationToken ct);

    protected ILanguageModel Model => model;
    protected DateTime Now => clock.UtcNow;

    protected static T Draft<T>(List<T> list, Func<T> create) where T : class, ITranslation
    {
        var t = list.FirstOrDefault(x => x.LanguageCode == En);
        if (t == null) list.Add(t = create());
        return t;
    }

    /// <summary>Marks a fresh draft for review (the hash lets the app notice later edits, which approve it).</summary>
    protected void Suggested(ITranslation t, string hash) => TranslationReview.Apply(t, false, true, hash, null, Now);

    protected static string Clean(string s) => TextUtil.NoEmDash(s).Trim();
}

public sealed class JobDraftHandler(IAppDbContext db, ILanguageModel model, IClock clock, IHtmlCleaner html) : EnglishDraftHandler(model, clock)
{
    public override string JobType => JobTypes.SuggestJobTranslation;

    public override async Task RunAsync(int id, CancellationToken ct)
    {
        var j = await db.JobOpenings.Include(x => x.Translations).FirstAsync(x => x.Id == id, ct);
        if (j.Translations.Any(t => t.LanguageCode == En && TranslationReview.IsFilled(t.Title, t.Description))) return;
        var o = await Model.CompleteJsonAsync(Prompts.TranslateJob,
            JsonSerializer.Serialize(new { title = j.Title, location = j.Location, description_html = j.Description }), Prompts.TranslateJobSchema, "job_translation", ct);
        var t = Draft(j.Translations, () => new JobOpeningTranslation { LanguageCode = En });
        t.Title = TextUtil.Clip(Clean(Str(o, "title")), 140);
        t.Description = html.Clean(Clean(Str(o, "description_html")));
        t.Location = TextUtil.Clip(Clean(Str(o, "location")), 140);
        Suggested(t, TextUtil.ContentHash(t.Title, t.Description));
        await db.SaveChangesAsync(ct);
    }
}

public sealed class ScreeningDraftHandler(IAppDbContext db, ILanguageModel model, IClock clock) : EnglishDraftHandler(model, clock)
{
    public override string JobType => JobTypes.SuggestScreeningTranslation;

    public override async Task RunAsync(int id, CancellationToken ct)
    {
        var q = await db.ScreeningQuestions.Include(x => x.Translations).Include(x => x.Options).ThenInclude(o => o.Translations)
            .AsSplitQuery().FirstAsync(x => x.Id == id, ct);
        var opts = q.Options.OrderBy(o => o.SortOrder).ToList();
        var o = await Model.CompleteJsonAsync(Prompts.TranslateTest,
            JsonSerializer.Serialize(new { question = q.Text, options = opts.Select(x => x.Text) }), Prompts.TranslateQuestionSchema, "screening_translation", ct);
        var t = Draft(q.Translations, () => new ScreeningQuestionTranslation { LanguageCode = En });
        t.Text = TextUtil.Clip(Clean(Str(o, "question")), 1000);
        var translated = Arr(o, "options").Select(x => x.GetString() ?? "").ToList();
        for (var i = 0; i < opts.Count && i < translated.Count; i++)
        {
            var ot = opts[i].Translations.FirstOrDefault(x => x.LanguageCode == En);
            if (ot == null) opts[i].Translations.Add(ot = new ScreeningQuestionOptionTranslation { LanguageCode = En });
            ot.Text = TextUtil.Clip(Clean(translated[i]), 300);
        }
        Suggested(t, TextUtil.ContentHash(t.Text, string.Join("\n", opts.Select(x => x.Translations.FirstOrDefault(y => y.LanguageCode == En)?.Text ?? ""))));
        await db.SaveChangesAsync(ct);
    }
}

public sealed class QuestionDraftHandler(IAppDbContext db, ILanguageModel model, IClock clock, IHtmlCleaner html) : EnglishDraftHandler(model, clock)
{
    public override string JobType => JobTypes.SuggestQuestionTranslation;

    public override async Task RunAsync(int id, CancellationToken ct)
    {
        var q = await db.Questions.Include(x => x.Translations).Include(x => x.Options).ThenInclude(o => o.Translations)
            .AsSplitQuery().FirstAsync(x => x.Id == id, ct);
        var opts = q.Options.OrderBy(o => o.SortOrder).ToList();
        var o = await Model.CompleteJsonAsync(Prompts.TranslateTest,
            JsonSerializer.Serialize(new { question = q.Text, options = opts.Select(x => x.Text) }), Prompts.TranslateQuestionSchema, "question_translation", ct);
        var t = Draft(q.Translations, () => new QuestionTranslation { LanguageCode = En });
        t.Text = html.Clean(Clean(Str(o, "question")));
        var translated = Arr(o, "options").Select(x => x.GetString() ?? "").ToList();
        for (var i = 0; i < opts.Count && i < translated.Count; i++)
        {
            var ot = opts[i].Translations.FirstOrDefault(x => x.LanguageCode == En);
            if (ot == null) opts[i].Translations.Add(ot = new QuestionOptionTranslation { LanguageCode = En });
            ot.Text = TextUtil.Clip(Clean(translated[i]), 500);
        }
        Suggested(t, TextUtil.ContentHash(t.Text, string.Join("\n", opts.Select(x => x.Translations.FirstOrDefault(y => y.LanguageCode == En)?.Text ?? ""))));
        await db.SaveChangesAsync(ct);
    }
}

public sealed class AssessmentDraftHandler(IAppDbContext db, ILanguageModel model, IClock clock, IHtmlCleaner html) : EnglishDraftHandler(model, clock)
{
    public override string JobType => JobTypes.SuggestAssessmentTranslation;

    public override async Task RunAsync(int id, CancellationToken ct)
    {
        var a = await db.Assessments.Include(x => x.Translations).Include(x => x.Sections).ThenInclude(s => s.Translations)
            .AsSplitQuery().FirstAsync(x => x.Id == id, ct);
        var secs = a.OrderedSections.ToList();
        var o = await Model.CompleteJsonAsync(Prompts.TranslateTest, JsonSerializer.Serialize(new
        {
            title = a.Title, instructions_html = a.Instructions, sections = secs.Select(s => new { title = s.Title, instructions = s.Instructions }),
        }), Prompts.TranslateAssessmentSchema, "assessment_translation", ct);
        var t = Draft(a.Translations, () => new AssessmentTranslation { LanguageCode = En });
        t.Title = TextUtil.Clip(Clean(Str(o, "title")), 140);
        t.Instructions = html.Clean(Clean(Str(o, "instructions_html")));
        var translated = Arr(o, "sections").ToList();
        for (var i = 0; i < secs.Count && i < translated.Count; i++)
        {
            var st = secs[i].Translations.FirstOrDefault(x => x.LanguageCode == En);
            if (st == null) secs[i].Translations.Add(st = new SectionTranslation { LanguageCode = En });
            st.Title = TextUtil.Clip(Clean(Str(translated[i], "title")), 140);
            st.Instructions = TextUtil.Clip(Clean(Str(translated[i], "instructions")), 2000);
        }
        Suggested(t, TextUtil.ContentHash(t.Title, t.Instructions, string.Join("\n", secs.Select(s =>
            (s.Translations.FirstOrDefault(x => x.LanguageCode == En)?.Title ?? "") + "|" + (s.Translations.FirstOrDefault(x => x.LanguageCode == En)?.Instructions ?? "")))));
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Suggested points for written answers, shown to markers only. A person decides the mark.</summary>
public sealed class MarkSuggestionHandler(IAppDbContext db, ILanguageModel model) : IJobHandler
{
    public string JobType => JobTypes.SuggestMarks;

    public async Task RunAsync(int attemptId, CancellationToken ct)
    {
        if (!model.IsConfigured) return;
        var questions = await db.AttemptQuestions
            .Where(q => db.Attempts.Any(a => a.Id == attemptId && a.Sections.Any(s => s.Id == q.AttemptSectionId))
                        && q.NeedsMarking && q.QuestionType == QuestionType.Essay)
            .ToListAsync(ct);
        foreach (var q in questions.Where(q => !string.IsNullOrWhiteSpace(q.AnswerText)))
        {
            var o = await model.CompleteJsonAsync(Prompts.Mark, JsonSerializer.Serialize(new
            {
                question = TextUtil.StripHtml(q.Text), rubric = q.Rubric.Length > 0 ? q.Rubric : "Not given: judge accuracy and completeness.",
                max_points = q.Points, answer = TextUtil.Clip(q.AnswerText, 12000),
            }), Prompts.MarkSchema, "mark_suggestion", ct);
            q.SuggestedPoints = Math.Clamp(Num(o, "points"), 0, q.Points);
            q.SuggestionReason = TextUtil.Clip(TextUtil.NoEmDash(Str(o, "reason")), 600);
        }
        await db.SaveChangesAsync(ct);
    }
}
