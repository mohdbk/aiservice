using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Assessments;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Content;

namespace Moet.Recruitment.Application.Assessments;

/// <summary>
/// Ready-made personality questionnaires that HR can add in one step: the statements go into the question bank (as
/// agreement-scale questions keyed to traits, Arabic with approved English) and a draft questionnaire is created.
/// Only public-domain content ships here (IPIP). 16PF questions are licensed by their publisher and added by HR.
/// </summary>
public sealed class PersonalityTemplates(IAppDbContext db, IClock clock, ICurrentActor actor)
{
    sealed record Text(string Ar, string En);
    sealed record Item(string Trait, bool Reverse, string En, string Ar);
    sealed record Template(string Key, string Model, string Category, string Source, Text Title, Text Instructions, List<Item> Items);

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static readonly Lazy<IReadOnlyList<Template>> All = new(Load);

    static IReadOnlyList<Template> Load()
    {
        var asm = typeof(PersonalityTemplates).Assembly;
        return asm.GetManifestResourceNames().Where(n => n.Contains(".Templates.") && n.EndsWith(".json")).Select(n =>
        {
            using var s = asm.GetManifestResourceStream(n)!;
            return JsonSerializer.Deserialize<Template>(s, Json)!;
        }).OrderBy(t => t.Key).ToList();
    }

    public object List() => All.Value.Select(t => new
    {
        key = t.Key, model = t.Model, title = t.Title.Ar, title_en = t.Title.En, items = t.Items.Count, source = t.Source,
        traits = Personality.Traits(Enum.Parse<PersonalityModel>(t.Model)).Select(x => new { code = x.Code, name_ar = x.NameAr, name_en = x.NameEn }),
    }).ToList();

    public async Task<object> CreateAsync(string key, CancellationToken ct = default)
    {
        var t = All.Value.FirstOrDefault(x => x.Key == key) ?? throw DomainException.NotFound();
        var now = clock.UtcNow;
        var questions = t.Items.Select(i =>
        {
            var q = new Question
            {
                QuestionType = QuestionType.Likert, Text = $"<p>{System.Net.WebUtility.HtmlEncode(i.Ar)}</p>", Category = t.Category, JobFamily = "",
                Difficulty = Difficulty.Medium, Points = 1, Trait = i.Trait, ReverseKeyed = i.Reverse, IsActive = true, SourceLanguageCode = "ar",
                CreatedAt = now, UpdatedAt = now,
                Translations =
                [
                    new QuestionTranslation
                    {
                        LanguageCode = "en", Text = $"<p>{System.Net.WebUtility.HtmlEncode(i.En)}</p>", ReviewStatus = ReviewStatus.Approved,
                        ApprovedByStaffId = actor.Id, ApprovedAt = now,
                    },
                ],
            };
            q.Validate();
            return q;
        }).ToList();
        db.Questions.AddRange(questions);
        await db.SaveChangesAsync(ct);

        var a = new Assessment
        {
            Title = t.Title.Ar, Instructions = t.Instructions.Ar, AssessmentType = AssessmentType.Personality, Status = AssessmentStatus.Draft,
            PersonalityModel = Enum.Parse<PersonalityModel>(t.Model), MaxAttempts = 1, ScorePolicy = ScorePolicy.Latest, ShuffleQuestions = false,
            DefaultDeadlineDays = 7, SourceLanguageCode = "ar", CreatedAt = now, UpdatedAt = now,
            Translations =
            [
                new AssessmentTranslation
                {
                    LanguageCode = "en", Title = t.Title.En, Instructions = t.Instructions.En, ReviewStatus = ReviewStatus.Approved,
                    ApprovedByStaffId = actor.Id, ApprovedAt = now,
                },
            ],
            Sections =
            [
                new Section
                {
                    SortOrder = 0, Title = t.Title.Ar, Minutes = 0,
                    Translations = [new SectionTranslation { LanguageCode = "en", Title = t.Title.En }],
                    Items = questions.Select((q, n) => new SectionItem { SortOrder = n, QuestionId = q.Id }).ToList(),
                },
            ],
        };
        a.Validate();
        db.Assessments.Add(a);
        await db.SaveChangesAsync(ct);
        return new { id = a.Id, questions = questions.Count };
    }
}
