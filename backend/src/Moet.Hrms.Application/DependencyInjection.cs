using Microsoft.Extensions.DependencyInjection;
using Moet.Hrms.Application.Backoffice;
using Moet.Hrms.Application.Ai;
using Moet.Hrms.Application.Assessments;
using Moet.Hrms.Application.Common;
using Moet.Hrms.Application.Portal;

namespace Moet.Hrms.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection s)
    {
        s.AddMemoryCache();
        s.AddScoped<ContentService>();
        s.AddScoped<MailConfig>();
        s.AddScoped<PortalMailer>();
        s.AddScoped<JobQueue>();
        s.AddScoped<AssessmentWorkflow>();
        s.AddScoped<PortalSiteService>();
        s.AddScoped<PortalAccountService>();
        s.AddScoped<PortalApplicationService>();
        s.AddScoped<CandidateCvService>();
        s.AddScoped<IJobHandler, CandidateCvReadHandler>();
        s.AddScoped<PortalAssessmentService>();
        s.AddScoped<IJobHandler, CvScoringHandler>();
        s.AddScoped<IJobHandler, JobDraftHandler>();
        s.AddScoped<IJobHandler, ScreeningDraftHandler>();
        s.AddScoped<IJobHandler, QuestionDraftHandler>();
        s.AddScoped<IJobHandler, AssessmentDraftHandler>();
        s.AddScoped<IJobHandler, MarkSuggestionHandler>();
        return s;
    }

    /// <summary>Use cases for the recruitment backoffice.</summary>
    public static IServiceCollection AddBackofficeApplication(this IServiceCollection s)
    {
        s.AddScoped<StaffAuthService>();
        s.AddScoped<BackofficeJobService>();
        s.AddScoped<BackofficeApplicantService>();
        s.AddScoped<BackofficeInterviewService>();
        s.AddScoped<BackofficeAssessmentService>();
        s.AddScoped<PersonalityTemplates>();
        s.AddScoped<RoleProfileService>();
        s.AddScoped<BackofficeEmailService>();
        s.AddScoped<DocumentService>();
        s.AddScoped<SelectionService>();
        s.AddScoped<ReportService>();
        s.AddScoped<SystemService>();
        s.AddScoped<DataProtectionService>();
        s.AddScoped<TalentPoolService>();
        s.AddScoped<ApprovalService>();
        s.AddScoped<CriteriaService>();
        s.AddScoped<CandidateService>();
        s.AddScoped<HealthAlertService>();
        s.AddScoped<StaffNotificationService>();
        s.AddScoped<Portal.JobAlertService>();
        s.AddScoped<BookingService>();
        s.AddScoped<TextService>();
        s.AddScoped<HandOffService>();
        s.AddScoped<AgentConversationService>();
        s.AddScoped<AgentAttachmentService>();
        s.AddScoped<IJobHandler>(sp => sp.GetRequiredService<HandOffService>());
        s.AddScoped<PersonalitySettingsService>();
        s.AddScoped<BackofficeSetupService>();
        s.AddScoped<AgentProposalService>();
        s.AddScoped<AgentSettingsService>();
        s.AddScoped<BackofficeComponentService>();
        return s;
    }
}
