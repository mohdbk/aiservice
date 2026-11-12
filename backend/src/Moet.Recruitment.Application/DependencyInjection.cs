using Microsoft.Extensions.DependencyInjection;
using Moet.Recruitment.Application.Admin;
using Moet.Recruitment.Application.Ai;
using Moet.Recruitment.Application.Assessments;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Application.Portal;

namespace Moet.Recruitment.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection s)
    {
        s.AddMemoryCache();
        s.AddScoped<ContentService>();
        s.AddScoped<PortalMailer>();
        s.AddScoped<JobQueue>();
        s.AddScoped<AssessmentWorkflow>();
        s.AddScoped<PortalSiteService>();
        s.AddScoped<PortalAccountService>();
        s.AddScoped<PortalApplicationService>();
        s.AddScoped<PortalAssessmentService>();
        s.AddScoped<IJobHandler, CvScoringHandler>();
        s.AddScoped<IJobHandler, JobDraftHandler>();
        s.AddScoped<IJobHandler, ScreeningDraftHandler>();
        s.AddScoped<IJobHandler, QuestionDraftHandler>();
        s.AddScoped<IJobHandler, AssessmentDraftHandler>();
        s.AddScoped<IJobHandler, MarkSuggestionHandler>();
        return s;
    }

    /// <summary>Use cases for the HR admin panel.</summary>
    public static IServiceCollection AddAdminApplication(this IServiceCollection s)
    {
        s.AddScoped<StaffAuthService>();
        s.AddScoped<AdminJobService>();
        s.AddScoped<AdminApplicantService>();
        s.AddScoped<AdminInterviewService>();
        s.AddScoped<AdminAssessmentService>();
        s.AddScoped<AdminSetupService>();
        s.AddScoped<AssistantService>();
        return s;
    }
}
