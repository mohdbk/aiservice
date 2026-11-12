using Moet.Recruitment.Domain;

namespace Moet.Recruitment.Application.Common;

/// <summary>Names used on the portal's wire format and labels shown to people.</summary>
public static class Labels
{
    /// <summary>Type names the career portal expects for screening questions.</summary>
    public static string Wire(AnswerType t) => t switch
    {
        AnswerType.YesNo => "Yes or No",
        AnswerType.SingleChoice => "Single Choice",
        AnswerType.MultipleChoice => "Multiple Choice",
        AnswerType.Number => "Number",
        AnswerType.Date => "Date",
        AnswerType.ShortText => "Short Text",
        _ => "Long Text",
    };

    /// <summary>Type names the career portal expects for test questions.</summary>
    public static string Wire(QuestionType t) => t switch
    {
        QuestionType.SingleChoice => "Single Choice",
        QuestionType.MultipleChoice => "Multiple Choice",
        QuestionType.TrueFalse => "True or False",
        QuestionType.ShortAnswer => "Short Answer",
        QuestionType.Number => "Number",
        QuestionType.Essay => "Essay",
        _ => "File Upload",
    };

    public static string Employment(EmploymentType t, string lang) => (lang, t) switch
    {
        ("ar", EmploymentType.FullTime) => "دوام كامل",
        ("ar", EmploymentType.PartTime) => "دوام جزئي",
        ("ar", EmploymentType.Contract) => "عقد",
        ("ar", EmploymentType.Temporary) => "مؤقت",
        ("ar", EmploymentType.Internship) => "تدريب",
        (_, EmploymentType.FullTime) => "Full Time",
        (_, EmploymentType.PartTime) => "Part Time",
        (_, EmploymentType.Contract) => "Contract",
        (_, EmploymentType.Temporary) => "Temporary",
        _ => "Internship",
    };

    public static string AssessmentTypeName(AssessmentType t) => t switch
    {
        AssessmentType.KnowledgeTest => "Knowledge Test",
        AssessmentType.LanguageCheck => "Language Check",
        AssessmentType.SituationalJudgement => "Situational Judgement",
        AssessmentType.WorkSample => "Work Sample",
        AssessmentType.Aptitude => "Aptitude",
        _ => "Other",
    };
}
