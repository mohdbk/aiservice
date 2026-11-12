using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Moet.Recruitment.Api.Hosting;
using Moet.Recruitment.Application.Backoffice;
using Moet.Recruitment.Application.Ai;
using Moet.Recruitment.Application.Assessments;
using Moet.Recruitment.Application.Common;

namespace Moet.Recruitment.Api.Endpoints;

/// <summary>The recruitment backoffice's API (/api/backoffice). Staff sign in with a cookie; changes need the X-Backoffice header.</summary>
public static class BackofficeEndpoints
{
    public sealed record SignInBody(string? Email, string? Password);
    public sealed record PasswordBody(string? Current, string? Next);
    public sealed record StatusBody(string? Status);
    public sealed record NoteBody(string? Text);
    public sealed record InviteBody(int AssessmentId, int? DeadlineDays);
    public sealed record ReexamBody(int ExtraAttempts, int DeadlineDays, bool Notify);
    public sealed record MarksBody(IReadOnlyList<MarkInput> Marks);
    public sealed record ApproveBody(System.Text.Json.JsonElement? Changes);
    public sealed record StaffBody(string? Email, string? FullName, string? Role, bool IsEnabled, string? Password);

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/backoffice");

        api.MapPost("/sign-in", async (SignInBody b, HttpContext ctx, StaffAuthService auth, CancellationToken ct) =>
        {
            var s = await auth.SignInAsync(b.Email, b.Password, ct);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, StaffCookie.Principal(s));
            return Results.Ok(Me(s.FullName, s.Email, s.Role.ToString(), s.Id));
        }).RequireRateLimiting("sign-in");

        var staff = api.MapGroup("").RequireAuthorization(StaffPolicies.Staff);
        staff.MapPost("/sign-out", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { ok = true });
        });
        staff.MapGet("/me", (ClaimsPrincipal u) =>
            Results.Ok(Me(u.Identity!.Name!, u.FindFirstValue(ClaimTypes.Email)!, u.FindFirstValue(ClaimTypes.Role)!, int.Parse(u.FindFirstValue("sid")!))));
        staff.MapPost("/password", async (PasswordBody b, ClaimsPrincipal u, HttpContext ctx, StaffAuthService auth, CancellationToken ct) =>
        {
            await auth.ChangePasswordAsync(int.Parse(u.FindFirstValue("sid")!), b.Current, b.Next, ct);
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { ok = true });
        });

        // dashboard and lookups
        staff.MapGet("/dashboard", (BackofficeSetupService s, CancellationToken ct) => s.DashboardAsync(ct));
        staff.MapGet("/lookups", (BackofficeSetupService s, CancellationToken ct) => s.LookupsAsync(ct));

        // jobs
        staff.MapGet("/jobs", ([AsParameters] ListQuery lq, string? status, bool? published, int? department, string? english, BackofficeJobService s, CancellationToken ct) =>
            s.ListAsync(lq, status, published, department, english, ct));
        staff.MapGet("/jobs/{id:int}", (int id, BackofficeJobService s, CancellationToken ct) => s.GetAsync(id, ct));
        staff.MapPost("/jobs", (JobInput b, BackofficeJobService s, CancellationToken ct) => s.SaveAsync(null, b, ct));
        staff.MapPut("/jobs/{id:int}", (int id, JobInput b, BackofficeJobService s, CancellationToken ct) => s.SaveAsync(id, b, ct));
        staff.MapPost("/jobs/{id:int}/suggest-english", (int id, BackofficeJobService s, CancellationToken ct) => s.SuggestTranslationAsync(id, ct));

        // applicants
        staff.MapGet("/applicants", ([AsParameters] ListQuery lq, int? job, string? status, string? screening, string? ai, decimal? minScore, string? country,
                DateOnly? from, DateOnly? to, string? cv, BackofficeApplicantService s, CancellationToken ct) =>
            s.ListAsync(lq, new ApplicantFilter(job, status, screening, ai, minScore, country, from, to, cv), ct));
        staff.MapGet("/applicants/{id:int}", (int id, BackofficeApplicantService s, CancellationToken ct) => s.GetAsync(id, ct));
        staff.MapGet("/applicants/{id:int}/cv", async (int id, bool? download, BackofficeApplicantService s, CancellationToken ct) =>
        {
            var (content, type, name) = await s.CvAsync(id, ct);
            return download == true ? Results.File(content, type, name) : Results.File(content, type, enableRangeProcessing: true);
        });
        staff.MapPost("/applicants/{id:int}/status", (int id, StatusBody b, BackofficeApplicantService s, CancellationToken ct) => s.SetStatusAsync(id, b.Status, ct));
        staff.MapPost("/applicants/{id:int}/notes", (int id, NoteBody b, BackofficeApplicantService s, CancellationToken ct) => s.AddNoteAsync(id, b.Text, ct));
        staff.MapPost("/applicants/{id:int}/rescore", (int id, BackofficeApplicantService s, CancellationToken ct) => s.RescoreAsync(id, ct));
        staff.MapPost("/applicants/{id:int}/invite", (int id, InviteBody b, BackofficeApplicantService s, CancellationToken ct) => s.InviteAsync(id, b.AssessmentId, b.DeadlineDays, ct));

        // interviews and offers
        staff.MapGet("/interviews", ([AsParameters] ListQuery lq, string? scope, string? status, int? round, int? job, BackofficeInterviewService s, CancellationToken ct) =>
            s.ListAsync(lq, scope, status, round, job, ct));
        staff.MapGet("/interviews/{id:int}", (int id, BackofficeInterviewService s, CancellationToken ct) => s.GetAsync(id, ct));
        staff.MapPost("/interviews", (InterviewInput b, BackofficeInterviewService s, CancellationToken ct) => s.ScheduleAsync(null, b, ct));
        staff.MapPut("/interviews/{id:int}", (int id, InterviewInput b, BackofficeInterviewService s, CancellationToken ct) => s.ScheduleAsync(id, b, ct));
        staff.MapPost("/interviews/{id:int}/status", (int id, StatusBody b, BackofficeInterviewService s, CancellationToken ct) => s.SetStatusAsync(id, b.Status, ct));
        staff.MapPost("/interviews/{id:int}/feedback", (int id, FeedbackInput b, BackofficeInterviewService s, CancellationToken ct) => s.FeedbackAsync(id, b, ct));
        staff.MapGet("/offers", ([AsParameters] ListQuery lq, string? status, BackofficeInterviewService s, CancellationToken ct) => s.OffersAsync(lq, status, ct));
        staff.MapGet("/offers/{id:int}", (int id, BackofficeInterviewService s, CancellationToken ct) => s.OfferAsync(id, ct));
        staff.MapPost("/offers", (OfferInput b, BackofficeInterviewService s, CancellationToken ct) => s.SaveOfferAsync(null, b, ct));
        staff.MapPut("/offers/{id:int}", (int id, OfferInput b, BackofficeInterviewService s, CancellationToken ct) => s.SaveOfferAsync(id, b, ct));
        staff.MapGet("/offers/{id:int}/letter", (int id, string? lang, DocumentService s, CancellationToken ct) => s.DocumentAsync("OfferLetter", id, null, lang, ct));
        staff.MapGet("/documents/{kind}/{id:int}", (string kind, int id, int? template, string? lang, DocumentService s, CancellationToken ct) =>
            s.DocumentAsync(kind, id, template, lang, ct));
        staff.MapGet("/document-templates", (string? kind, bool? active, DocumentService s, CancellationToken ct) => s.ListAsync(kind, active, ct));
        staff.MapGet("/email-wording", (BackofficeEmailService s, CancellationToken ct) => s.WordingAsync(ct));
        staff.MapPost("/offers/{id:int}/status", (int id, StatusBody b, BackofficeInterviewService s, CancellationToken ct) => s.SetOfferStatusAsync(id, b.Status, ct));

        // assessments
        staff.MapGet("/questions", ([AsParameters] ListQuery lq, string? type, string? category, string? difficulty, string? family, bool? active, string? english,
            BackofficeAssessmentService s, CancellationToken ct) => s.QuestionsAsync(lq, type, category, difficulty, family, active, english, ct));
        staff.MapGet("/questions/{id:int}", (int id, BackofficeAssessmentService s, CancellationToken ct) => s.QuestionAsync(id, ct));
        staff.MapPost("/questions", (QuestionInput b, BackofficeAssessmentService s, CancellationToken ct) => s.SaveQuestionAsync(null, b, ct));
        staff.MapPut("/questions/{id:int}", (int id, QuestionInput b, BackofficeAssessmentService s, CancellationToken ct) => s.SaveQuestionAsync(id, b, ct));
        staff.MapGet("/assessments", ([AsParameters] ListQuery lq, string? status, string? type, BackofficeAssessmentService s, CancellationToken ct) => s.AssessmentsAsync(lq, status, type, ct));
        staff.MapGet("/assessments/{id:int}", (int id, BackofficeAssessmentService s, CancellationToken ct) => s.AssessmentAsync(id, ct));
        staff.MapPost("/assessments", (AssessmentInput b, BackofficeAssessmentService s, CancellationToken ct) => s.SaveAssessmentAsync(null, b, ct));
        staff.MapPut("/assessments/{id:int}", (int id, AssessmentInput b, BackofficeAssessmentService s, CancellationToken ct) => s.SaveAssessmentAsync(id, b, ct));
        staff.MapGet("/assessment-templates", (PersonalityTemplates s) => s.List());
        staff.MapPost("/assessment-templates/{key}", (string key, PersonalityTemplates s, CancellationToken ct) => s.CreateAsync(key, ct));
        staff.MapGet("/role-profiles", (string? model, RoleProfileService s, CancellationToken ct) => s.ListAsync(model, ct));
        staff.MapGet("/role-profiles/{id:int}", (int id, RoleProfileService s, CancellationToken ct) => s.GetAsync(id, ct));
        staff.MapGet("/invitations", ([AsParameters] ListQuery lq, string? status, int? assessment, string? result, int? job, BackofficeAssessmentService s, CancellationToken ct) =>
            s.InvitationsAsync(lq, status, assessment, result, job, ct));
        staff.MapPost("/invitations/{id:int}/reexam", (int id, ReexamBody b, BackofficeAssessmentService s, CancellationToken ct) => s.ReexamAsync(id, b.ExtraAttempts, b.DeadlineDays, b.Notify, ct));
        staff.MapPost("/invitations/{id:int}/cancel", (int id, BackofficeAssessmentService s, CancellationToken ct) => s.CancelInvitationAsync(id, ct));
        staff.MapGet("/attempts/{id:int}", (int id, BackofficeAssessmentService s, CancellationToken ct) => s.AttemptAsync(id, ct));
        staff.MapPost("/attempts/{id:int}/marks", (int id, MarksBody b, BackofficeAssessmentService s, CancellationToken ct) => s.SaveMarksAsync(id, b.Marks, ct));
        staff.MapPost("/attempts/{id:int}/suggest-marks", (int id, BackofficeAssessmentService s, CancellationToken ct) => s.SuggestMarksAsync(id, ct));
        staff.MapGet("/attempts/{id:int}/files/{questionId:int}", async (int id, int questionId, BackofficeAssessmentService s, CancellationToken ct) =>
        {
            var (content, type, name) = await s.AnswerFileAsync(id, questionId, ct);
            return Results.File(content, type, name);
        });

        // screening questions and English drafts
        staff.MapGet("/screening-questions", ([AsParameters] ListQuery lq, string? type, string? category, bool? active, string? english, BackofficeSetupService s, CancellationToken ct) =>
            s.ScreeningQuestionsAsync(lq, type, category, active, english, ct));
        staff.MapGet("/screening-questions/{id:int}", (int id, BackofficeSetupService s, CancellationToken ct) => s.ScreeningQuestionAsync(id, ct));
        staff.MapPost("/screening-questions", (ScreeningQuestionInput b, BackofficeSetupService s, CancellationToken ct) => s.SaveScreeningQuestionAsync(null, b, ct));
        staff.MapPut("/screening-questions/{id:int}", (int id, ScreeningQuestionInput b, BackofficeSetupService s, CancellationToken ct) => s.SaveScreeningQuestionAsync(id, b, ct));
        staff.MapPost("/suggest-english/{kind}/{id:int}", (string kind, int id, BackofficeSetupService s, CancellationToken ct) => s.SuggestEnglishAsync(kind, id, ct));

        // recruitment agent: the browser talks AG-UI to the agent service through this proxy; the agent can only propose changes
        staff.MapPost("/agent/run", AgentProxy.RunAsync).RequireRateLimiting("assistant");
        staff.MapPost("/agent/proposals", (ProposalInput b, AgentProposalService s, CancellationToken ct) => s.ProposeAsync(b, ct));
        staff.MapPost("/agent/proposals/{id:guid}/approve", (Guid id, string? lang, ApproveBody? b, AgentProposalService s, CancellationToken ct) => s.ApproveAsync(id, lang, b?.Changes, ct));
        staff.MapGet("/agent/info", (AgentSettingsService s, CancellationToken ct) => s.InfoAsync(ct));
        staff.MapGet("/agent/components", (BackofficeComponentService s, CancellationToken ct) => s.ActiveAsync(ct));
        staff.MapPost("/agent/proposals/{id:guid}/cancel", (Guid id, AgentProposalService s, CancellationToken ct) => s.CancelAsync(id, ct));

        // setup (recruitment admins only)
        var setup = api.MapGroup("").RequireAuthorization(StaffPolicies.Admin);
        setup.MapGet("/departments", ([AsParameters] ListQuery lq, BackofficeSetupService s, CancellationToken ct) => s.DepartmentsAsync(lq, ct));
        setup.MapPost("/departments", (NamedInput b, BackofficeSetupService s, CancellationToken ct) => s.SaveDepartmentAsync(null, b, ct));
        setup.MapPut("/departments/{id:int}", (int id, NamedInput b, BackofficeSetupService s, CancellationToken ct) => s.SaveDepartmentAsync(id, b, ct));
        setup.MapGet("/rounds", ([AsParameters] ListQuery lq, BackofficeSetupService s, CancellationToken ct) => s.RoundsAsync(lq, ct));
        setup.MapPost("/rounds", (NamedInput b, BackofficeSetupService s, CancellationToken ct) => s.SaveRoundAsync(null, b, ct));
        setup.MapPut("/rounds/{id:int}", (int id, NamedInput b, BackofficeSetupService s, CancellationToken ct) => s.SaveRoundAsync(id, b, ct));
        setup.MapGet("/components", ([AsParameters] ListQuery lq, string? kind, bool? active, string? action, BackofficeComponentService s, CancellationToken ct) =>
            s.ListAsync(lq, kind, active, action, ct));
        setup.MapGet("/components/{id:int}", (int id, BackofficeComponentService s, CancellationToken ct) => s.GetAsync(id, ct));
        setup.MapPost("/components", (ComponentInput b, BackofficeComponentService s, CancellationToken ct) => s.SaveAsync(null, b, ct));
        setup.MapPut("/components/{id:int}", (int id, ComponentInput b, BackofficeComponentService s, CancellationToken ct) => s.SaveAsync(id, b, ct));
        setup.MapDelete("/components/{id:int}", (int id, BackofficeComponentService s, CancellationToken ct) => s.DeleteAsync(id, ct));
        setup.MapPost("/role-profiles", (RoleProfileInput b, RoleProfileService s, CancellationToken ct) => s.SaveAsync(null, b, ct));
        setup.MapPut("/role-profiles/{id:int}", (int id, RoleProfileInput b, RoleProfileService s, CancellationToken ct) => s.SaveAsync(id, b, ct));
        setup.MapDelete("/role-profiles/{id:int}", (int id, RoleProfileService s, CancellationToken ct) => s.DeleteAsync(id, ct));
        setup.MapGet("/document-templates/{id:int}", (int id, DocumentService s, CancellationToken ct) => s.GetAsync(id, ct));
        setup.MapPost("/document-templates", (DocumentTemplateInput b, DocumentService s, CancellationToken ct) => s.SaveAsync(null, b, ct));
        setup.MapPut("/document-templates/{id:int}", (int id, DocumentTemplateInput b, DocumentService s, CancellationToken ct) => s.SaveAsync(id, b, ct));
        setup.MapDelete("/document-templates/{id:int}", (int id, DocumentService s, CancellationToken ct) => s.DeleteAsync(id, ct));
        setup.MapGet("/email-templates", (BackofficeEmailService s, CancellationToken ct) => s.TemplatesAsync(ct));
        setup.MapGet("/email-templates/{id:int}", (int id, BackofficeEmailService s, CancellationToken ct) => s.TemplateAsync(id, ct));
        setup.MapPost("/email-templates", (EmailTemplateInput b, BackofficeEmailService s, CancellationToken ct) => s.SaveTemplateAsync(null, b, ct));
        setup.MapPut("/email-templates/{id:int}", (int id, EmailTemplateInput b, BackofficeEmailService s, CancellationToken ct) => s.SaveTemplateAsync(id, b, ct));
        setup.MapDelete("/email-templates/{id:int}", (int id, BackofficeEmailService s, CancellationToken ct) => s.DeleteTemplateAsync(id, ct));
        setup.MapPost("/email-templates/{id:int}/test", (int id, TestEmailInput b, BackofficeEmailService s, CancellationToken ct) => s.TestTemplateAsync(id, b, ct));
        setup.MapGet("/settings", (BackofficeSetupService s, CancellationToken ct) => s.SettingsAsync(ct));
        setup.MapPut("/settings", (SettingsInput b, BackofficeSetupService s, CancellationToken ct) => s.SaveSettingsAsync(b, ct));
        setup.MapGet("/portal-text/{lang}", (string lang, [AsParameters] ListQuery lq, string? changed, BackofficeSetupService s, CancellationToken ct) => s.PortalTextAsync(lang, lq, changed, ct));
        setup.MapPut("/portal-text", (PortalTextInput b, BackofficeSetupService s, CancellationToken ct) => s.SavePortalTextAsync(b, ct));

        // staff accounts (administrators)
        var sys = api.MapGroup("").RequireAuthorization(StaffPolicies.SystemAdmin);
        sys.MapGet("/staff", ([AsParameters] ListQuery lq, string? role, bool? enabled, StaffAuthService s, CancellationToken ct) => s.ListAsync(lq, role, enabled, ct));
        sys.MapPost("/staff", (StaffBody b, StaffAuthService s, CancellationToken ct) => s.SaveAsync(null, b.Email, b.FullName, b.Role, b.IsEnabled, b.Password, ct));
        sys.MapGet("/agent-settings", (AgentSettingsService s, CancellationToken ct) => s.GetAsync(ct));
        sys.MapPut("/agent-settings", (AgentSettingsInput b, AgentSettingsService s, CancellationToken ct) => s.SaveAsync(b, ct));
        sys.MapGet("/email-settings", (BackofficeEmailService s, CancellationToken ct) => s.SettingsAsync(ct));
        sys.MapPut("/email-settings", (EmailSettingInput b, BackofficeEmailService s, CancellationToken ct) => s.SaveSettingsAsync(b, ct));
        sys.MapPost("/email-settings/test", (TestEmailInput b, BackofficeEmailService s, CancellationToken ct) => s.TestServerAsync(b, ct));
        sys.MapGet("/email-log", ([AsParameters] ListQuery lq, string? status, BackofficeEmailService s, CancellationToken ct) => s.LogAsync(lq, status, ct));
        sys.MapGet("/email-log/{id:long}", (long id, BackofficeEmailService s, CancellationToken ct) => s.LogItemAsync(id, ct));
        sys.MapPost("/email-log/{id:long}/retry", (long id, BackofficeEmailService s, CancellationToken ct) => s.RetryAsync(id, ct));
        sys.MapPut("/staff/{id:int}", (int id, StaffBody b, StaffAuthService s, CancellationToken ct) => s.SaveAsync(id, b.Email, b.FullName, b.Role, b.IsEnabled, b.Password, ct));
    }

    static object Me(string name, string email, string role, int id) => new
    {
        id, full_name = name, email, role,
        can_administer = role is "RecruitmentAdmin" or "Administrator", is_administrator = role == "Administrator",
    };
}
