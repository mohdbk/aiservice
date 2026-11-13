using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Moet.Recruitment.Api.Hosting;
using Moet.Recruitment.Application.Backoffice;
using Moet.Recruitment.Application.Ai;
using Moet.Recruitment.Application.Assessments;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Api.Endpoints;

/// <summary>The recruitment backoffice's API (/api/backoffice). Staff sign in with a cookie; changes need the X-Backoffice header.</summary>
public static class BackofficeEndpoints
{
    public sealed record SignInBody(string? Email, string? Password, string? Code = null);
    public sealed record CodeBody(string? Code);
    public sealed record DisableTwoStepBody(string? Password, string? Code);
    public sealed record PasswordBody(string? Current, string? Next);
    public sealed record StatusBody(string? Status);
    public sealed record NoteBody(string? Text);
    public sealed record InviteBody(int AssessmentId, int? DeadlineDays, string? DeadlineAt = null);
    public sealed record ReexamBody(int ExtraAttempts, int DeadlineDays, bool Notify, string? DeadlineAt = null);
    public sealed record DeadlineBody(string? DeadlineAt, bool Notify);
    public sealed record MarksBody(IReadOnlyList<MarkInput> Marks);
    public sealed record ApproveBody(System.Text.Json.JsonElement? Changes);
    public sealed record StaffBody(string? Email, string? FullName, string? Role, bool IsEnabled, string? Password);

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/backoffice");

        api.MapPost("/sign-in", async (SignInBody b, HttpContext ctx, StaffAuthService auth, IClock clock, CancellationToken ct) =>
        {
            var s = await auth.SignInAsync(b.Email, b.Password, b.Code, ct);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, StaffCookie.Principal(s));
            return Results.Ok(Me(s.FullName, s.Email, s.Role.ToString(), s.Id, clock.Zone.Id));
        }).RequireRateLimiting("sign-in");

        var staff = api.MapGroup("").RequireAuthorization(StaffPolicies.Staff);
        staff.MapPost("/sign-out", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { ok = true });
        });
        staff.MapGet("/me", (ClaimsPrincipal u, IClock clock) =>
            Results.Ok(Me(u.Identity!.Name!, u.FindFirstValue(ClaimTypes.Email)!, u.FindFirstValue(ClaimTypes.Role)!, int.Parse(u.FindFirstValue("sid")!), clock.Zone.Id)));
        staff.MapPost("/password", async (PasswordBody b, ClaimsPrincipal u, HttpContext ctx, StaffAuthService auth, CancellationToken ct) =>
        {
            await auth.ChangePasswordAsync(int.Parse(u.FindFirstValue("sid")!), b.Current, b.Next, ct);
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { ok = true });
        });

        // two-step sign-in (the signed-in person's own)
        staff.MapGet("/two-step", (ClaimsPrincipal u, StaffAuthService auth, CancellationToken ct) => auth.TwoStepAsync(int.Parse(u.FindFirstValue("sid")!), ct));
        staff.MapPost("/two-step/setup", (ClaimsPrincipal u, StaffAuthService auth, CancellationToken ct) => auth.SetUpTwoStepAsync(int.Parse(u.FindFirstValue("sid")!), ct));
        staff.MapPost("/two-step/enable", (CodeBody b, ClaimsPrincipal u, StaffAuthService auth, CancellationToken ct) => auth.EnableTwoStepAsync(int.Parse(u.FindFirstValue("sid")!), b.Code, ct));
        staff.MapPost("/two-step/disable", (DisableTwoStepBody b, ClaimsPrincipal u, StaffAuthService auth, CancellationToken ct) =>
            auth.DisableTwoStepAsync(int.Parse(u.FindFirstValue("sid")!), b.Password, b.Code, ct));

        // dashboard and lookups
        staff.MapGet("/dashboard", (BackofficeSetupService s, CancellationToken ct) => s.DashboardAsync(ct));
        staff.MapGet("/reports/hiring", (DateOnly? from, DateOnly? to, int? job, int? department, ReportService s, CancellationToken ct) =>
            s.HiringAsync(from, to, job, department, ct));
        staff.MapGet("/lookups", (BackofficeSetupService s, CancellationToken ct) => s.LookupsAsync(ct));

        // jobs
        staff.MapGet("/jobs", ([AsParameters] ListQuery lq, string? status, bool? published, int? department, string? english, BackofficeJobService s, CancellationToken ct) =>
            s.ListAsync(lq, status, published, department, english, ct));
        staff.MapGet("/jobs/{id:int}", (int id, BackofficeJobService s, CancellationToken ct) => s.GetAsync(id, ct));
        staff.MapPost("/jobs", (JobInput b, BackofficeJobService s, CancellationToken ct) => s.SaveAsync(null, b, ct));
        staff.MapPut("/jobs/{id:int}", (int id, JobInput b, BackofficeJobService s, CancellationToken ct) => s.SaveAsync(id, b, ct));
        staff.MapGet("/jobs/{id:int}/ranking", (int id, [AsParameters] ListQuery lq, bool? all, SelectionService s, CancellationToken ct) =>
            s.RankingAsync(id, lq, all == true, ct));
        staff.MapPost("/jobs/{id:int}/suggest-english", (int id, BackofficeJobService s, CancellationToken ct) => s.SuggestTranslationAsync(id, ct));

        // applicants
        staff.MapGet("/applicants", ([AsParameters] ListQuery lq, int? job, string? status, string? screening, string? ai, decimal? minScore, string? country,
                DateOnly? from, DateOnly? to, string? cv, BackofficeApplicantService s, CancellationToken ct) =>
            s.ListAsync(lq, new ApplicantFilter(job, status, screening, ai, minScore, country, from, to, cv), ct));
        staff.MapGet("/applicants/{id:int}", (int id, BackofficeApplicantService s, CancellationToken ct) => s.GetAsync(id, ct));
        staff.MapGet("/applicants/{id:int}/bookings", (int id, BookingService s, CancellationToken ct) => s.ForApplicantAsync(id, ct));
        staff.MapPost("/bookings", (BookingInput b, BookingService s, CancellationToken ct) => s.CreateAsync(b, ct));
        staff.MapPost("/bookings/{id:int}/cancel", (int id, BookingService s, CancellationToken ct) => s.CancelAsync(id, ct));
        staff.MapGet("/offers/{id:int}/handoff", async (int id, HandOffService s, CancellationToken ct) =>
            Results.File(System.Text.Encoding.UTF8.GetBytes(await s.PayloadJsonAsync(id, ct)), "application/json", $"hire-{id}.json"));
        staff.MapPost("/offers/{id:int}/handoff", (int id, HandOffService s, CancellationToken ct) => s.SendNowAsync(id, ct));
        staff.MapGet("/reports/hires.csv", async (DateOnly? from, DateOnly? to, HandOffService s, CancellationToken ct) =>
            Results.File(await s.HiresCsvAsync(from, to, ct), "text/csv; charset=utf-8", "hires.csv"));
        staff.MapGet("/talent-pool", ([AsParameters] ListQuery lq, string? cv, int? job, TalentPoolService s, CancellationToken ct) => s.ListAsync(lq, cv, job, ct));
        staff.MapGet("/approvals", ([AsParameters] ListQuery lq, string? status, ApprovalService s, CancellationToken ct) => s.ListAsync(lq, status, ct));
        staff.MapPost("/approvals/{id:int}/decide", async (int id, DecisionInput b, ApprovalService s, BackofficeInterviewService offers, CancellationToken ct) =>
        {
            var r = await s.DecideAsync(id, b, ct);
            // An approved offer is sent now (by the approver, so it needs no further approval).
            if (b.Approve && r.kind == "OfferSend") await offers.SetOfferStatusAsync(r.record_id, "Sent", ct);
            return r;
        });
        staff.MapGet("/requisitions", ([AsParameters] ListQuery lq, string? status, ApprovalService s, CancellationToken ct) => s.RequisitionsAsync(lq, status, ct));
        staff.MapPost("/requisitions", (RequisitionInput b, ApprovalService s, CancellationToken ct) => s.SaveRequisitionAsync(null, b, ct));
        staff.MapPut("/requisitions/{id:int}", (int id, RequisitionInput b, ApprovalService s, CancellationToken ct) => s.SaveRequisitionAsync(id, b, ct));
        staff.MapPost("/requisitions/{id:int}/cancel", (int id, ApprovalService s, CancellationToken ct) => s.CancelRequisitionAsync(id, ct));
        staff.MapPost("/talent-pool/{id:int}/invite", (int id, TalentInviteBody b, TalentPoolService s, CancellationToken ct) => s.InviteAsync(id, b.JobId, ct));
        staff.MapGet("/applicants/{id:int}/cv", async (int id, bool? download, BackofficeApplicantService s, CancellationToken ct) =>
        {
            var (content, type, name) = await s.CvAsync(id, ct);
            return download == true ? Results.File(content, type, name) : Results.File(content, type, enableRangeProcessing: true);
        });
        staff.MapPost("/applicants/{id:int}/status", (int id, StatusBody b, BackofficeApplicantService s, CancellationToken ct) => s.SetStatusAsync(id, b.Status, ct));
        staff.MapPost("/applicants/{id:int}/notes", (int id, NoteBody b, BackofficeApplicantService s, CancellationToken ct) => s.AddNoteAsync(id, b.Text, ct));
        staff.MapPost("/applicants/{id:int}/rescore", (int id, BackofficeApplicantService s, CancellationToken ct) => s.RescoreAsync(id, ct));
        staff.MapPost("/applicants/{id:int}/invite", (int id, InviteBody b, BackofficeApplicantService s, CancellationToken ct) => s.InviteAsync(id, b.AssessmentId, b.DeadlineDays, ct, b.DeadlineAt));

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
        staff.MapGet("/documents/{kind}/{id:int}/pdf", async (string kind, int id, int? template, string? lang, DocumentService s, CancellationToken ct) =>
            await s.PdfAsync(kind, id, template, lang, ct) is { } f ? Results.File(f.Content, "application/pdf", f.FileName) : Results.NotFound());
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
        staff.MapGet("/personality-settings", (PersonalitySettingsService s, CancellationToken ct) => s.GetAsync(ct));
        staff.MapGet("/role-profiles/{id:int}", (int id, RoleProfileService s, CancellationToken ct) => s.GetAsync(id, ct));
        staff.MapGet("/invitations", ([AsParameters] ListQuery lq, string? status, int? assessment, string? result, int? job, BackofficeAssessmentService s, CancellationToken ct) =>
            s.InvitationsAsync(lq, status, assessment, result, job, ct));
        staff.MapPost("/invitations/{id:int}/reexam", (int id, ReexamBody b, BackofficeAssessmentService s, CancellationToken ct) => s.ReexamAsync(id, b.ExtraAttempts, b.DeadlineDays, b.Notify, ct, b.DeadlineAt));
        staff.MapPost("/invitations/{id:int}/deadline", (int id, DeadlineBody b, BackofficeAssessmentService s, CancellationToken ct) => s.ChangeDeadlineAsync(id, b.DeadlineAt, b.Notify, ct));
        staff.MapGet("/invitations/{id:int}/link", (int id, BackofficeAssessmentService s, CancellationToken ct) => s.LinkAsync(id, ct));
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
        staff.MapGet("/agent/conversations", ([AsParameters] ListQuery lq, AgentConversationService s, CancellationToken ct) => s.ListAsync(lq, ct));
        staff.MapGet("/agent/conversations/{thread}", (string thread, AgentConversationService s, CancellationToken ct) => s.GetAsync(thread, ct));
        staff.MapPut("/agent/conversations/{thread}", (string thread, ConversationInput b, AgentConversationService s, CancellationToken ct) => s.SaveAsync(thread, b, ct));
        staff.MapPost("/agent/conversations/{thread}/share", (string thread, ShareBody b, AgentConversationService s, CancellationToken ct) => s.ShareAsync(thread, b.Shared, ct));
        staff.MapDelete("/agent/conversations/{thread}", (string thread, AgentConversationService s, CancellationToken ct) => s.DeleteAsync(thread, ct));
        staff.MapPost("/agent/attachments", async (HttpRequest req, AgentAttachmentService s, CancellationToken ct) =>
        {
            if (!req.HasFormContentType) throw DomainException.Invalid("file_required");
            var form = await req.ReadFormAsync(ct);
            var f = form.Files.GetFile("file") ?? throw DomainException.Invalid("file_required");
            if (f.Length > 10 * 1024 * 1024) throw DomainException.Invalid("cv_too_large", new Dictionary<string, object?> { ["max_mb"] = 10 });
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            return await s.UploadAsync(f.FileName, ms.ToArray(), ct);
        }).DisableAntiforgery();
        staff.MapGet("/agent/attachments/{id:guid}/text", (Guid id, AgentAttachmentService s, CancellationToken ct) => s.TextAsync(id, ct));
        staff.MapGet("/agent/suggestions", (AgentConversationService s, CancellationToken ct) => s.SuggestionsAsync(ct));
        staff.MapGet("/agent/info", (AgentSettingsService s, CancellationToken ct) => s.InfoAsync(ct));
        staff.MapGet("/agent/components", (BackofficeComponentService s, CancellationToken ct) => s.ActiveAsync(ct));
        staff.MapPost("/agent/proposals/{id:guid}/cancel", (Guid id, AgentProposalService s, CancellationToken ct) => s.CancelAsync(id, ct));

        // setup (recruitment admins only)
        var setup = api.MapGroup("").RequireAuthorization(StaffPolicies.Admin);
        setup.MapGet("/approval-settings", (ApprovalService s, CancellationToken ct) => s.SettingsAsync(ct));
        setup.MapPut("/approval-settings", (ApprovalSettingsInput b, ApprovalService s, CancellationToken ct) => s.SaveSettingsAsync(b, ct));
        setup.MapGet("/locations", ([AsParameters] ListQuery lq, BackofficeSetupService s, CancellationToken ct) => s.LocationsAsync(lq, ct));
        setup.MapPost("/locations", (LocationInput b, BackofficeSetupService s, CancellationToken ct) => s.SaveLocationAsync(null, b, ct));
        setup.MapPut("/locations/{id:int}", (int id, LocationInput b, BackofficeSetupService s, CancellationToken ct) => s.SaveLocationAsync(id, b, ct));
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
        setup.MapPut("/personality-settings/bands", (BandCutoffsInput b, PersonalitySettingsService s, CancellationToken ct) => s.SaveCutoffsAsync(b, ct));
        setup.MapPut("/personality-settings/types/{type}", (string type, TypeNoteInput b, PersonalitySettingsService s, CancellationToken ct) => s.SaveTypeAsync(type, b, ct));
        setup.MapDelete("/personality-settings/types/{type}", (string type, PersonalitySettingsService s, CancellationToken ct) => s.ResetTypeAsync(type, ct));
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
        sys.MapGet("/audit", ([AsParameters] ListQuery lq, string? record, string? action, string? key, DateOnly? from, DateOnly? to, string? via, string? kind,
                int? staff, string? request, SystemService s, CancellationToken ct) =>
            s.AuditAsync(lq, record, action, key, from, to, ct, via, kind, staff, request));
        sys.MapPost("/audit/{id:long}/comments", (long id, NoteBody b, SystemService s, CancellationToken ct) => s.CommentAsync(id, b.Text, ct));
        sys.MapGet("/data-protection", (DataProtectionService s, CancellationToken ct) => s.RetentionViewAsync(ct));
        sys.MapPut("/data-protection", (RetentionInput b, DataProtectionService s, CancellationToken ct) => s.SaveRetentionAsync(b, ct));
        sys.MapPost("/data-protection/run", async (DataProtectionService s, CancellationToken ct) => new { anonymised = await s.RunRetentionAsync(true, ct) });
        sys.MapGet("/data-requests", ([AsParameters] ListQuery lq, string? status, string? kind, DataProtectionService s, CancellationToken ct) => s.RequestsAsync(lq, status, kind, ct));
        sys.MapPost("/data-requests/{id:int}/approve", (int id, ResponseBody b, DataProtectionService s, CancellationToken ct) => s.ApproveAsync(id, b.Response, ct));
        sys.MapPost("/data-requests/{id:int}/decline", (int id, ResponseBody b, DataProtectionService s, CancellationToken ct) => s.DeclineAsync(id, b.Response, ct));
        sys.MapGet("/consents", ([AsParameters] ListQuery lq, string? kind, DataProtectionService s, CancellationToken ct) => s.ConsentsAsync(lq, kind, ct));
        sys.MapGet("/handoff-settings", (HandOffService s, CancellationToken ct) => s.GetAsync(ct));
        sys.MapPut("/handoff-settings", (HandOffInput b, HandOffService s, CancellationToken ct) => s.SaveAsync(b, ct));
        sys.MapGet("/backups", async (IBackups b, CancellationToken ct) =>
        {
            var items = b.IsConfigured ? await b.ListAsync(ct) : [];
            return new
            {
                configured = b.IsConfigured, interval_hours = b.IntervalHours,
                items = items.Select(x => new { key = x.Key, size = x.Size, modified = x.Modified }),
            };
        });
        sys.MapPost("/backups", async (IBackups b, CancellationToken ct) =>
        {
            if (!b.IsConfigured) throw DomainException.Conflict("backups_off");
            var (key, size) = await b.BackUpAsync(ct);
            return new { key, size };
        });
        sys.MapGet("/text-settings", (TextService s, CancellationToken ct) => s.GetAsync(ct));
        sys.MapPut("/text-settings", (TextSettingInput b, TextService s, CancellationToken ct) => s.SaveAsync(b, ct));
        sys.MapPost("/text-settings/test", (TextTestBody b, TextService s, CancellationToken ct) => s.TestAsync(b.To, ct));
        sys.MapGet("/assistant-usage", (SystemService s, CancellationToken ct) => s.AssistantUsageAsync(ct));
        sys.MapPut("/assistant-usage/{staffId:int}", (int staffId, AssistantLimitBody b, SystemService s, CancellationToken ct) => s.SetAssistantLimitAsync(staffId, b.Limit, ct));
        sys.MapPost("/staff/{id:int}/two-step/reset", (int id, StaffAuthService s, CancellationToken ct) => s.ResetTwoStepAsync(id, ct));
        sys.MapGet("/health-alerts", (HealthAlertService s, CancellationToken ct) => s.GetAsync(ct));
        sys.MapPut("/health-alerts", (HealthAlertInput b, HealthAlertService s, CancellationToken ct) => s.SaveAsync(b, ct));
        sys.MapPost("/health-alerts/check", async (HealthAlertService s, CancellationToken ct) =>
            new { problems = (await s.ProblemsAsync(null, ct)).Select(p => new { key = p.Key, en = p.En, ar = p.Ar }), emailed = await s.CheckAsync(null, ct) });
        sys.MapGet("/health", async (SystemService s, IHttpClientFactory http, IConfiguration cfg, CancellationToken ct) =>
        {
            // The assistant service answers on /healthz when it is set up and running.
            object assistant;
            var url = cfg["AGENT_URL"];
            if (string.IsNullOrWhiteSpace(url)) assistant = new { state = "off", ms = 0L };
            else
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(5));
                    var res = await http.CreateClient().GetAsync(url.TrimEnd('/') + "/healthz", cts.Token);
                    assistant = new { state = res.IsSuccessStatusCode ? "ok" : "down", ms = sw.ElapsedMilliseconds };
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { assistant = new { state = "down", ms = sw.ElapsedMilliseconds }; }
            }
            return await s.HealthAsync(assistant, ct);
        });
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

    static object Me(string name, string email, string role, int id, string timeZone) => new
    {
        id, full_name = name, email, role, time_zone = timeZone,
        can_administer = role is "RecruitmentAdmin" or "Administrator", is_administrator = role == "Administrator",
    };
}

public sealed record AssistantLimitBody(int? Limit);

public sealed record ResponseBody(string? Response);
public sealed record TalentInviteBody(int JobId);
public sealed record TextTestBody(string? To);
public sealed record ShareBody(bool Shared);
