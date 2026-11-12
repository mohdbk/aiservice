using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Moet.Recruitment.Api.Hosting;
using Moet.Recruitment.Application.Admin;
using Moet.Recruitment.Application.Ai;

namespace Moet.Recruitment.Api.Endpoints;

/// <summary>The HR admin panel's API (/api/admin). Staff sign in with a cookie; changes need the X-Admin header.</summary>
public static class AdminEndpoints
{
    public sealed record SignInBody(string? Email, string? Password);
    public sealed record PasswordBody(string? Current, string? Next);
    public sealed record StatusBody(string? Status);
    public sealed record NoteBody(string? Text);
    public sealed record InviteBody(int AssessmentId, int? DeadlineDays);
    public sealed record ReexamBody(int ExtraAttempts, int DeadlineDays, bool Notify);
    public sealed record MarksBody(IReadOnlyList<MarkInput> Marks);
    public sealed record ChatBody(string? Message, string? SessionId, string? Page);
    public sealed record StaffBody(string? Email, string? FullName, string? Role, bool IsEnabled, string? Password);

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/admin");

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
        staff.MapGet("/dashboard", (AdminSetupService s, CancellationToken ct) => s.DashboardAsync(ct));
        staff.MapGet("/lookups", (AdminSetupService s, CancellationToken ct) => s.LookupsAsync(ct));

        // jobs
        staff.MapGet("/jobs", (string? status, string? q, AdminJobService s, CancellationToken ct) => s.ListAsync(status, q, ct));
        staff.MapGet("/jobs/{id:int}", (int id, AdminJobService s, CancellationToken ct) => s.GetAsync(id, ct));
        staff.MapPost("/jobs", (JobInput b, AdminJobService s, CancellationToken ct) => s.SaveAsync(null, b, ct));
        staff.MapPut("/jobs/{id:int}", (int id, JobInput b, AdminJobService s, CancellationToken ct) => s.SaveAsync(id, b, ct));
        staff.MapPost("/jobs/{id:int}/suggest-english", (int id, AdminJobService s, CancellationToken ct) => s.SuggestTranslationAsync(id, ct));

        // applicants
        staff.MapGet("/applicants", (int? job, string? status, string? screening, string? q, decimal? minScore, string? sort, int? page,
                AdminApplicantService s, CancellationToken ct) =>
            s.ListAsync(new ApplicantFilter(job, status, screening, q, minScore, sort, page ?? 1), ct));
        staff.MapGet("/applicants/{id:int}", (int id, AdminApplicantService s, CancellationToken ct) => s.GetAsync(id, ct));
        staff.MapGet("/applicants/{id:int}/cv", async (int id, bool? download, AdminApplicantService s, CancellationToken ct) =>
        {
            var (content, type, name) = await s.CvAsync(id, ct);
            return download == true ? Results.File(content, type, name) : Results.File(content, type, enableRangeProcessing: true);
        });
        staff.MapPost("/applicants/{id:int}/status", (int id, StatusBody b, AdminApplicantService s, CancellationToken ct) => s.SetStatusAsync(id, b.Status, ct));
        staff.MapPost("/applicants/{id:int}/notes", (int id, NoteBody b, AdminApplicantService s, CancellationToken ct) => s.AddNoteAsync(id, b.Text, ct));
        staff.MapPost("/applicants/{id:int}/rescore", (int id, AdminApplicantService s, CancellationToken ct) => s.RescoreAsync(id, ct));
        staff.MapPost("/applicants/{id:int}/invite", (int id, InviteBody b, AdminApplicantService s, CancellationToken ct) => s.InviteAsync(id, b.AssessmentId, b.DeadlineDays, ct));

        // interviews and offers
        staff.MapGet("/interviews", (string? scope, AdminInterviewService s, CancellationToken ct) => s.ListAsync(scope, ct));
        staff.MapGet("/interviews/{id:int}", (int id, AdminInterviewService s, CancellationToken ct) => s.GetAsync(id, ct));
        staff.MapPost("/interviews", (InterviewInput b, AdminInterviewService s, CancellationToken ct) => s.ScheduleAsync(null, b, ct));
        staff.MapPut("/interviews/{id:int}", (int id, InterviewInput b, AdminInterviewService s, CancellationToken ct) => s.ScheduleAsync(id, b, ct));
        staff.MapPost("/interviews/{id:int}/status", (int id, StatusBody b, AdminInterviewService s, CancellationToken ct) => s.SetStatusAsync(id, b.Status, ct));
        staff.MapPost("/interviews/{id:int}/feedback", (int id, FeedbackInput b, AdminInterviewService s, CancellationToken ct) => s.FeedbackAsync(id, b, ct));
        staff.MapGet("/offers", (string? status, AdminInterviewService s, CancellationToken ct) => s.OffersAsync(status, ct));
        staff.MapGet("/offers/{id:int}", (int id, AdminInterviewService s, CancellationToken ct) => s.OfferAsync(id, ct));
        staff.MapPost("/offers", (OfferInput b, AdminInterviewService s, CancellationToken ct) => s.SaveOfferAsync(null, b, ct));
        staff.MapPut("/offers/{id:int}", (int id, OfferInput b, AdminInterviewService s, CancellationToken ct) => s.SaveOfferAsync(id, b, ct));
        staff.MapPost("/offers/{id:int}/status", (int id, StatusBody b, AdminInterviewService s, CancellationToken ct) => s.SetOfferStatusAsync(id, b.Status, ct));

        // assessments
        staff.MapGet("/questions", (string? q, string? category, bool? active, AdminAssessmentService s, CancellationToken ct) => s.QuestionsAsync(q, category, active, ct));
        staff.MapGet("/questions/{id:int}", (int id, AdminAssessmentService s, CancellationToken ct) => s.QuestionAsync(id, ct));
        staff.MapPost("/questions", (QuestionInput b, AdminAssessmentService s, CancellationToken ct) => s.SaveQuestionAsync(null, b, ct));
        staff.MapPut("/questions/{id:int}", (int id, QuestionInput b, AdminAssessmentService s, CancellationToken ct) => s.SaveQuestionAsync(id, b, ct));
        staff.MapGet("/assessments", (AdminAssessmentService s, CancellationToken ct) => s.AssessmentsAsync(ct));
        staff.MapGet("/assessments/{id:int}", (int id, AdminAssessmentService s, CancellationToken ct) => s.AssessmentAsync(id, ct));
        staff.MapPost("/assessments", (AssessmentInput b, AdminAssessmentService s, CancellationToken ct) => s.SaveAssessmentAsync(null, b, ct));
        staff.MapPut("/assessments/{id:int}", (int id, AssessmentInput b, AdminAssessmentService s, CancellationToken ct) => s.SaveAssessmentAsync(id, b, ct));
        staff.MapGet("/invitations", (string? status, int? assessment, AdminAssessmentService s, CancellationToken ct) => s.InvitationsAsync(status, assessment, ct));
        staff.MapPost("/invitations/{id:int}/reexam", (int id, ReexamBody b, AdminAssessmentService s, CancellationToken ct) => s.ReexamAsync(id, b.ExtraAttempts, b.DeadlineDays, b.Notify, ct));
        staff.MapPost("/invitations/{id:int}/cancel", (int id, AdminAssessmentService s, CancellationToken ct) => s.CancelInvitationAsync(id, ct));
        staff.MapGet("/attempts/{id:int}", (int id, AdminAssessmentService s, CancellationToken ct) => s.AttemptAsync(id, ct));
        staff.MapPost("/attempts/{id:int}/marks", (int id, MarksBody b, AdminAssessmentService s, CancellationToken ct) => s.SaveMarksAsync(id, b.Marks, ct));
        staff.MapPost("/attempts/{id:int}/suggest-marks", (int id, AdminAssessmentService s, CancellationToken ct) => s.SuggestMarksAsync(id, ct));
        staff.MapGet("/attempts/{id:int}/files/{questionId:int}", async (int id, int questionId, AdminAssessmentService s, CancellationToken ct) =>
        {
            var (content, type, name) = await s.AnswerFileAsync(id, questionId, ct);
            return Results.File(content, type, name);
        });

        // screening questions and English drafts
        staff.MapGet("/screening-questions", (AdminSetupService s, CancellationToken ct) => s.ScreeningQuestionsAsync(ct));
        staff.MapGet("/screening-questions/{id:int}", (int id, AdminSetupService s, CancellationToken ct) => s.ScreeningQuestionAsync(id, ct));
        staff.MapPost("/screening-questions", (ScreeningQuestionInput b, AdminSetupService s, CancellationToken ct) => s.SaveScreeningQuestionAsync(null, b, ct));
        staff.MapPut("/screening-questions/{id:int}", (int id, ScreeningQuestionInput b, AdminSetupService s, CancellationToken ct) => s.SaveScreeningQuestionAsync(id, b, ct));
        staff.MapPost("/suggest-english/{kind}/{id:int}", (string kind, int id, AdminSetupService s, CancellationToken ct) => s.SuggestEnglishAsync(kind, id, ct));

        // assistant
        staff.MapPost("/assistant/chat", (ChatBody b, AssistantService s, CancellationToken ct) => s.ChatAsync(b.Message, b.SessionId, b.Page, ct))
            .RequireRateLimiting("assistant");
        staff.MapGet("/assistant/history", (string? session, AssistantService s, CancellationToken ct) => s.HistoryAsync(session, ct));
        staff.MapPost("/assistant/actions/{id:guid}/approve", (Guid id, AssistantService s, CancellationToken ct) => s.ApproveAsync(id, ct));

        // setup (recruitment admins)
        var admin = api.MapGroup("").RequireAuthorization(StaffPolicies.Admin);
        admin.MapGet("/departments", (AdminSetupService s, CancellationToken ct) => s.DepartmentsAsync(ct));
        admin.MapPost("/departments", (NamedInput b, AdminSetupService s, CancellationToken ct) => s.SaveDepartmentAsync(null, b, ct));
        admin.MapPut("/departments/{id:int}", (int id, NamedInput b, AdminSetupService s, CancellationToken ct) => s.SaveDepartmentAsync(id, b, ct));
        admin.MapGet("/rounds", (AdminSetupService s, CancellationToken ct) => s.RoundsAsync(ct));
        admin.MapPost("/rounds", (NamedInput b, AdminSetupService s, CancellationToken ct) => s.SaveRoundAsync(null, b, ct));
        admin.MapPut("/rounds/{id:int}", (int id, NamedInput b, AdminSetupService s, CancellationToken ct) => s.SaveRoundAsync(id, b, ct));
        admin.MapGet("/settings", (AdminSetupService s, CancellationToken ct) => s.SettingsAsync(ct));
        admin.MapPut("/settings", (SettingsInput b, AdminSetupService s, CancellationToken ct) => s.SaveSettingsAsync(b, ct));
        admin.MapGet("/portal-text/{lang}", (string lang, AdminSetupService s, CancellationToken ct) => s.PortalTextAsync(lang, ct));
        admin.MapPut("/portal-text", (PortalTextInput b, AdminSetupService s, CancellationToken ct) => s.SavePortalTextAsync(b, ct));

        // staff accounts (administrators)
        var sys = api.MapGroup("").RequireAuthorization(StaffPolicies.SystemAdmin);
        sys.MapGet("/staff", (StaffAuthService s, CancellationToken ct) => s.ListAsync(ct));
        sys.MapPost("/staff", (StaffBody b, StaffAuthService s, CancellationToken ct) => s.SaveAsync(null, b.Email, b.FullName, b.Role, b.IsEnabled, b.Password, ct));
        sys.MapPut("/staff/{id:int}", (int id, StaffBody b, StaffAuthService s, CancellationToken ct) => s.SaveAsync(id, b.Email, b.FullName, b.Role, b.IsEnabled, b.Password, ct));
    }

    static object Me(string name, string email, string role, int id) => new
    {
        id, full_name = name, email, role,
        can_administer = role is "RecruitmentAdmin" or "Administrator", is_administrator = role == "Administrator",
    };
}
