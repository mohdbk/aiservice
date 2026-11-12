using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Domain.Assessments;
using Moet.Recruitment.Domain.Content;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Identity;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Infrastructure.Persistence;

/// <summary>Tables, keys and relationships. Column names equal property names.</summary>
static class ModelMap
{
    public static void Configure(ModelBuilder m)
    {
        // ---------- core ----------
        m.Entity<Language>(e => { e.ToTable("Language", "core"); e.HasKey(x => x.Code); });
        m.Entity<PortalSetting>(e => { e.ToTable("PortalSetting", "core"); e.Property(x => x.Id).ValueGeneratedNever(); });
        m.Entity<PortalText>(e => e.ToTable("PortalText", "core"));
        m.Entity<Department>(e =>
        {
            e.ToTable("Department", "core");
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.DepartmentId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<DepartmentTranslation>(e => { e.ToTable("DepartmentTranslation", "core"); e.HasKey(x => new { x.DepartmentId, x.LanguageCode }); });

        // ---------- identity ----------
        m.Entity<StaffUser>(e => e.ToTable("StaffUser", "identity"));
        m.Entity<ApplicantAccount>(e => e.ToTable("ApplicantAccount", "identity"));
        m.Entity<AccountToken>(e =>
        {
            e.ToTable("AccountToken", "identity");
            e.HasOne(x => x.ApplicantAccount).WithMany().HasForeignKey(x => x.ApplicantAccountId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- hiring ----------
        m.Entity<JobOpening>(e =>
        {
            e.ToTable("JobOpening", "hiring");
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId);
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.JobOpeningId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Criteria).WithOne().HasForeignKey(c => c.JobOpeningId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ScreeningQuestions).WithOne().HasForeignKey(s => s.JobOpeningId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.AssessmentStages).WithOne().HasForeignKey(s => s.JobOpeningId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<JobOpeningTranslation>(e => { e.ToTable("JobOpeningTranslation", "hiring"); e.HasKey(x => new { x.JobOpeningId, x.LanguageCode }); });
        m.Entity<JobCriterion>(e => e.ToTable("JobCriterion", "hiring"));
        m.Entity<JobScreeningQuestion>(e =>
        {
            e.ToTable("JobScreeningQuestion", "hiring");
            e.HasKey(x => new { x.JobOpeningId, x.ScreeningQuestionId });
            e.HasOne(x => x.ScreeningQuestion).WithMany().HasForeignKey(x => x.ScreeningQuestionId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<JobAssessmentStage>(e =>
        {
            e.ToTable("JobAssessmentStage", "hiring");
            e.HasKey(x => new { x.JobOpeningId, x.AssessmentId });
            e.HasOne<Assessment>().WithMany().HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<ScreeningQuestion>(e =>
        {
            e.ToTable("ScreeningQuestion", "hiring");
            e.HasMany(x => x.Options).WithOne().HasForeignKey(o => o.ScreeningQuestionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.ScreeningQuestionId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<ScreeningQuestionOption>(e =>
        {
            e.ToTable("ScreeningQuestionOption", "hiring");
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.ScreeningQuestionOptionId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<ScreeningQuestionTranslation>(e => { e.ToTable("ScreeningQuestionTranslation", "hiring"); e.HasKey(x => new { x.ScreeningQuestionId, x.LanguageCode }); });
        m.Entity<ScreeningQuestionOptionTranslation>(e =>
        {
            e.ToTable("ScreeningQuestionOptionTranslation", "hiring");
            e.HasKey(x => new { x.ScreeningQuestionOptionId, x.LanguageCode });
        });

        m.Entity<JobApplication>(e =>
        {
            e.ToTable("JobApplication", "hiring");
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.JobOpening).WithMany().HasForeignKey(x => x.JobOpeningId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ApplicantAccount).WithMany().HasForeignKey(x => x.ApplicantAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.CvFileId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.ScreeningAnswers).WithOne().HasForeignKey(a => a.JobApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CvAnalysis).WithOne().HasForeignKey<CvAnalysis>(c => c.JobApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Notes).WithOne().HasForeignKey(n => n.JobApplicationId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<ScreeningAnswer>(e => { e.ToTable("ScreeningAnswer", "hiring"); e.HasKey(x => new { x.JobApplicationId, x.ScreeningQuestionId }); });
        m.Entity<CvAnalysis>(e =>
        {
            e.ToTable("CvAnalysis", "hiring");
            e.HasKey(x => x.JobApplicationId);
            e.Property(x => x.JobApplicationId).ValueGeneratedNever();
            e.HasMany(x => x.CriterionScores).WithOne().HasForeignKey(s => s.JobApplicationId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<CvCriterionScore>(e => { e.ToTable("CvCriterionScore", "hiring"); e.HasKey(x => new { x.JobApplicationId, x.SortOrder }); });
        m.Entity<ApplicationNote>(e =>
        {
            e.ToTable("ApplicationNote", "hiring");
            e.HasOne(x => x.AuthorStaff).WithMany().HasForeignKey(x => x.AuthorStaffId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicantAccount>().WithMany().HasForeignKey(x => x.AuthorApplicantId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<InterviewRound>(e =>
        {
            e.ToTable("InterviewRound", "hiring");
            e.HasMany(x => x.Skills).WithOne().HasForeignKey(s => s.InterviewRoundId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<InterviewRoundSkill>(e => e.ToTable("InterviewRoundSkill", "hiring"));
        m.Entity<Interview>(e =>
        {
            e.ToTable("Interview", "hiring");
            e.HasOne(x => x.JobApplication).WithMany().HasForeignKey(x => x.JobApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.InterviewRound).WithMany().HasForeignKey(x => x.InterviewRoundId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Panelists).WithOne().HasForeignKey(p => p.InterviewId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Feedback).WithOne().HasForeignKey(f => f.InterviewId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<InterviewPanelist>(e =>
        {
            e.ToTable("InterviewPanelist", "hiring");
            e.HasKey(x => new { x.InterviewId, x.StaffUserId });
            e.HasOne(x => x.StaffUser).WithMany().HasForeignKey(x => x.StaffUserId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<InterviewFeedback>(e =>
        {
            e.ToTable("InterviewFeedback", "hiring");
            e.HasOne(x => x.StaffUser).WithMany().HasForeignKey(x => x.StaffUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Ratings).WithOne().HasForeignKey(r => r.InterviewFeedbackId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<InterviewFeedbackRating>(e => { e.ToTable("InterviewFeedbackRating", "hiring"); e.HasKey(x => new { x.InterviewFeedbackId, x.Skill }); });
        m.Entity<JobOffer>(e =>
        {
            e.ToTable("JobOffer", "hiring");
            e.HasOne(x => x.JobApplication).WithMany().HasForeignKey(x => x.JobApplicationId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- assessment ----------
        m.Entity<Question>(e =>
        {
            e.ToTable("Question", "assessment");
            e.HasMany(x => x.Options).WithOne().HasForeignKey(o => o.QuestionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.AcceptedAnswers).WithOne().HasForeignKey(a => a.QuestionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<QuestionOption>(e =>
        {
            e.ToTable("QuestionOption", "assessment");
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.QuestionOptionId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<QuestionAcceptedAnswer>(e => e.ToTable("QuestionAcceptedAnswer", "assessment"));
        m.Entity<QuestionTranslation>(e => { e.ToTable("QuestionTranslation", "assessment"); e.HasKey(x => new { x.QuestionId, x.LanguageCode }); });
        m.Entity<QuestionOptionTranslation>(e => { e.ToTable("QuestionOptionTranslation", "assessment"); e.HasKey(x => new { x.QuestionOptionId, x.LanguageCode }); });

        m.Entity<Assessment>(e =>
        {
            e.ToTable("Assessment", "assessment");
            e.Ignore(x => x.OrderedSections);
            e.HasMany(x => x.Sections).WithOne().HasForeignKey(s => s.AssessmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<Section>(e =>
        {
            e.ToTable("Section", "assessment");
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.SectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.SectionId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<SectionItem>(e =>
        {
            e.ToTable("SectionItem", "assessment");
            e.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<AssessmentTranslation>(e => { e.ToTable("AssessmentTranslation", "assessment"); e.HasKey(x => new { x.AssessmentId, x.LanguageCode }); });
        m.Entity<SectionTranslation>(e => { e.ToTable("SectionTranslation", "assessment"); e.HasKey(x => new { x.SectionId, x.LanguageCode }); });

        m.Entity<Invitation>(e =>
        {
            e.ToTable("Invitation", "assessment");
            e.HasOne(x => x.JobApplication).WithMany().HasForeignKey(x => x.JobApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Assessment).WithMany().HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Attempts).WithOne(a => a.Invitation).HasForeignKey(a => a.InvitationId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<Attempt>(e =>
        {
            e.ToTable("Attempt", "assessment");
            e.Ignore(x => x.OrderedSections);
            e.Ignore(x => x.AllQuestions);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasMany(x => x.Sections).WithOne().HasForeignKey(s => s.AttemptId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<AttemptSection>(e =>
        {
            e.ToTable("AttemptSection", "assessment");
            e.Ignore(x => x.OrderedQuestions);
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.AttemptSectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Questions).WithOne().HasForeignKey(q => q.AttemptSectionId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<AttemptSectionTranslation>(e => { e.ToTable("AttemptSectionTranslation", "assessment"); e.HasKey(x => new { x.AttemptSectionId, x.LanguageCode }); });
        m.Entity<AttemptQuestion>(e =>
        {
            e.ToTable("AttemptQuestion", "assessment");
            e.Ignore(x => x.OrderedOptions);
            e.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.AttemptQuestionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Options).WithOne().HasForeignKey(o => o.AttemptQuestionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.AcceptedAnswers).WithOne().HasForeignKey(a => a.AttemptQuestionId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<AttemptQuestionTranslation>(e => { e.ToTable("AttemptQuestionTranslation", "assessment"); e.HasKey(x => new { x.AttemptQuestionId, x.LanguageCode }); });
        m.Entity<AttemptOption>(e =>
        {
            e.ToTable("AttemptOption", "assessment");
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.AttemptOptionId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<AttemptOptionTranslation>(e => { e.ToTable("AttemptOptionTranslation", "assessment"); e.HasKey(x => new { x.AttemptOptionId, x.LanguageCode }); });
        m.Entity<AttemptAcceptedAnswer>(e => e.ToTable("AttemptAcceptedAnswer", "assessment"));

        // ---------- ops ----------
        m.Entity<StoredFile>(e => { e.ToTable("StoredFile", "ops"); e.Property(x => x.Id).ValueGeneratedNever(); });
        m.Entity<EmailMessage>(e => e.ToTable("EmailMessage", "ops"));
        m.Entity<BackgroundJob>(e => e.ToTable("BackgroundJob", "ops"));
        m.Entity<AuditEntry>(e => e.ToTable("AuditEntry", "ops"));
        m.Entity<AgentSetting>(e => { e.ToTable("AgentSetting", "ops"); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Temperature).HasPrecision(3, 2); });
        m.Entity<AgentRun>(e => e.ToTable("AgentRun", "ops"));
        m.Entity<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey>(e => e.ToTable("DataProtectionKey", "ops"));
        m.Entity<EmailSetting>(e => { e.ToTable("EmailSetting", "ops"); e.Property(x => x.Id).ValueGeneratedNever(); });
        m.Entity<EmailTemplate>(e => { e.ToTable("EmailTemplate", "ops"); e.Property(x => x.RowVersion).IsRowVersion(); });
        m.Entity<DocumentTemplate>(e => { e.ToTable("DocumentTemplate", "core"); e.Property(x => x.RowVersion).IsRowVersion(); });
        m.Entity<RoleProfile>(e =>
        {
            e.ToTable("RoleProfile", "assessment");
            e.Property(x => x.RowVersion).IsRowVersion();
        });
        m.Entity<DataRequest>(e => e.ToTable("DataRequest", "ops"));
        m.Entity<InterviewBooking>(e =>
        {
            e.ToTable("InterviewBooking", "hiring");
            e.HasMany(x => x.Slots).WithOne().HasForeignKey(s => s.InterviewBookingId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<InterviewBookingSlot>(e => e.ToTable("InterviewBookingSlot", "hiring"));
        m.Entity<TextSetting>(e => { e.ToTable("TextSetting", "ops"); e.Property(x => x.Id).ValueGeneratedNever(); });
        m.Entity<TextMessage>(e => e.ToTable("TextMessage", "ops"));
        m.Entity<HandOffSetting>(e => { e.ToTable("HandOffSetting", "ops"); e.Property(x => x.Id).ValueGeneratedNever(); });
        m.Entity<ConsentRecord>(e => e.ToTable("ConsentRecord", "ops"));
        m.Entity<RetentionSetting>(e => { e.ToTable("RetentionSetting", "ops"); e.Property(x => x.Id).ValueGeneratedNever(); });
        m.Entity<TypeNote>(e => { e.ToTable("TypeNote", "assessment"); e.HasKey(x => x.Type); });
        m.Entity<PersonalitySetting>(e => { e.ToTable("PersonalitySetting", "assessment"); e.Property(x => x.Id).ValueGeneratedNever(); });
        m.Entity<AgentComponent>(e =>
        {
            e.ToTable("AgentComponent", "ops");
            e.Property(x => x.RowVersion).IsRowVersion();
        });
        m.Entity<AgentProposal>(e => { e.ToTable("AgentProposal", "ops"); e.Property(x => x.Id).ValueGeneratedNever(); });
    }
}
