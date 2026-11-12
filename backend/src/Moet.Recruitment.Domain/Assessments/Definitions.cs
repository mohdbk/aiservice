using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Content;

namespace Moet.Recruitment.Domain.Assessments;

public class Question
{
    public int Id { get; set; }
    public QuestionType QuestionType { get; set; } = QuestionType.SingleChoice;
    public string Text { get; set; } = "";
    public string Category { get; set; } = "";
    public string JobFamily { get; set; } = "";
    public Difficulty Difficulty { get; set; } = Difficulty.Medium;
    public decimal Points { get; set; } = 1;
    public decimal Tolerance { get; set; }
    public string Rubric { get; set; } = "";
    public int MaxWords { get; set; }
    public bool IsActive { get; set; } = true;
    public string SourceLanguageCode { get; set; } = "ar";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<QuestionOption> Options { get; set; } = [];
    public List<QuestionAcceptedAnswer> AcceptedAnswers { get; set; } = [];
    public List<QuestionTranslation> Translations { get; set; } = [];

    public bool IsChoice => QuestionType is QuestionType.SingleChoice or QuestionType.MultipleChoice or QuestionType.TrueFalse;

    public void Validate()
    {
        if (TextUtil.StripHtml(Text).Length == 0) throw DomainException.Invalid("question_required");
        if (Points <= 0) Points = 1;
        if (!IsChoice) Options.Clear();
        if (IsChoice)
        {
            if (Options.Count < 2) throw DomainException.Invalid("two_options");
            var right = Options.Count(o => o.IsCorrect);
            if (QuestionType != QuestionType.MultipleChoice && right != 1) throw DomainException.Invalid("one_correct");
            if (QuestionType == QuestionType.MultipleChoice && right < 1) throw DomainException.Invalid("some_correct");
        }
        if (QuestionType is QuestionType.ShortAnswer or QuestionType.Number && AcceptedAnswers.Count == 0)
            throw DomainException.Invalid("accepted_required");
        if (QuestionType == QuestionType.Number &&
            !decimal.TryParse(AcceptedAnswers[0].Answer.Replace(",", ""), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            throw DomainException.Invalid("accepted_number");
        if (QuestionType is not (QuestionType.ShortAnswer or QuestionType.Number)) AcceptedAnswers.Clear();
    }
}

public class QuestionOption
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public int SortOrder { get; set; }
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
    public List<QuestionOptionTranslation> Translations { get; set; } = [];
}

public class QuestionAcceptedAnswer
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public int SortOrder { get; set; }
    public string Answer { get; set; } = "";
}

public class QuestionTranslation : ITranslation
{
    public int QuestionId { get; set; }
    public string LanguageCode { get; set; } = "";
    public string Text { get; set; } = "";
    public ReviewStatus ReviewStatus { get; set; }
    public string? SuggestedHash { get; set; }
    public int? ApprovedByStaffId { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public class QuestionOptionTranslation
{
    public int QuestionOptionId { get; set; }
    public string LanguageCode { get; set; } = "";
    public string Text { get; set; } = "";
}

public class Assessment
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Instructions { get; set; } = "";
    public AssessmentType AssessmentType { get; set; } = AssessmentType.KnowledgeTest;
    public AssessmentStatus Status { get; set; } = AssessmentStatus.Draft;
    public decimal PassMark { get; set; } = 60;
    public byte MaxAttempts { get; set; } = 1;
    public ScorePolicy ScorePolicy { get; set; } = ScorePolicy.Highest;
    public bool ShuffleQuestions { get; set; } = true;
    public bool ShuffleOptions { get; set; } = true;
    public bool ShowScoreToCandidate { get; set; }
    public byte DefaultDeadlineDays { get; set; } = 7;
    public string SourceLanguageCode { get; set; } = "ar";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<Section> Sections { get; set; } = [];
    public List<AssessmentTranslation> Translations { get; set; } = [];

    public IEnumerable<Section> OrderedSections => Sections.OrderBy(s => s.SortOrder);

    public int TotalMinutes => Sections.Sum(s => s.Minutes);

    public AssessmentTranslation? ApprovedTranslation(string lang) =>
        lang == SourceLanguageCode ? null : Translations.FirstOrDefault(t => t.LanguageCode == lang && t.ReviewStatus == ReviewStatus.Approved);

    public (string Title, string Instructions) In(string lang)
    {
        var tr = ApprovedTranslation(lang);
        return tr == null ? (Title, Instructions)
            : (tr.Title.Trim().Length > 0 ? tr.Title : Title, TextUtil.StripHtml(tr.Instructions).Length > 0 ? tr.Instructions : Instructions);
    }

    public string SectionTitleIn(Section s, string lang)
    {
        if (ApprovedTranslation(lang) == null) return s.Title;
        var t = s.Translations.FirstOrDefault(x => x.LanguageCode == lang)?.Title;
        return string.IsNullOrWhiteSpace(t) ? s.Title : t;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title)) throw DomainException.Invalid("title_required");
        if (Sections.Count == 0) throw DomainException.Invalid("section_required");
        if (MaxAttempts < 1) MaxAttempts = 1;
        foreach (var s in Sections)
            if (s.Items.Any(i => i.QuestionId == null && i.DrawCount <= 0)) throw DomainException.Invalid("item_incomplete");
    }
}

public class Section
{
    public int Id { get; set; }
    public int AssessmentId { get; set; }
    public int SortOrder { get; set; }
    public string Title { get; set; } = "";
    public string Instructions { get; set; } = "";
    public int Minutes { get; set; }
    public decimal PassMark { get; set; }
    public List<SectionItem> Items { get; set; } = [];
    public List<SectionTranslation> Translations { get; set; } = [];
}

/// <summary>A fixed question, or a random draw of DrawCount questions matching the filters.</summary>
public class SectionItem
{
    public int Id { get; set; }
    public int SectionId { get; set; }
    public int SortOrder { get; set; }
    public int? QuestionId { get; set; }
    public string DrawCategory { get; set; } = "";
    public string DrawDifficulty { get; set; } = "";
    public string DrawJobFamily { get; set; } = "";
    public int DrawCount { get; set; }
    public decimal? PointsOverride { get; set; }
}

public class AssessmentTranslation : ITranslation
{
    public int AssessmentId { get; set; }
    public string LanguageCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string Instructions { get; set; } = "";
    public ReviewStatus ReviewStatus { get; set; }
    public string? SuggestedHash { get; set; }
    public int? ApprovedByStaffId { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public class SectionTranslation
{
    public int SectionId { get; set; }
    public string LanguageCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string Instructions { get; set; } = "";
}
