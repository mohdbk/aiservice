using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Backoffice;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Ai;

public sealed record ProposalInput(string? Action, JsonElement Arguments, string? ThreadId, string? Lang);

/// <summary>
/// Changes proposed by the recruitment agent service. The agent can only propose; the backend stores the proposal,
/// describes it in the staff member's language, and carries it out through the normal backoffice services when the
/// same person approves it. Everything the approval changes goes through the usual audit trail.
/// </summary>
public sealed class AgentProposalService(
    IAppDbContext db, ICurrentActor actor, IClock clock, ContentService content, PortalMailer mailer, AgentSettingsService settings, IAuditContext audit,
    BackofficeApplicantService applicants, BackofficeInterviewService interviews, BackofficeJobService jobs, BackofficeSetupService setup,
    BookingService bookings, TalentPoolService talent, CriteriaService criteria, BackofficeAssessmentService assessments)
{

    static IReadOnlyDictionary<string, string[]> Editable => AgentActions.Editable;

    public async Task<object> ProposeAsync(ProposalInput input, CancellationToken ct = default)
    {
        var staffId = actor.Id ?? throw DomainException.Forbidden();
        var config = await settings.CurrentAsync(ct);
        if (!config.IsEnabled) throw DomainException.Forbidden("assistant_off");
        var name = input.Action ?? "";
        if (!Editable.ContainsKey(name)) throw DomainException.Invalid("invalid_value");
        var a = input.Arguments.ValueKind == JsonValueKind.Object ? input.Arguments : throw DomainException.Invalid("invalid_value");
        var ar = input.Lang != "en";
        var label = await DescribeAsync(name, a, ar, ct);
        var now = clock.UtcNow;
        var p = new AgentProposal
        {
            Id = Guid.NewGuid(), StaffUserId = staffId, ThreadId = TextUtil.Clip(input.ThreadId ?? "", 64), ActionName = name,
            Arguments = a.GetRawText(), Label = TextUtil.Clip(TextUtil.NoEmDash(label), 600), Status = ProposalStatus.Pending,
            CreatedAt = now, ExpiresAt = now.AddMinutes(config.ProposalMinutes),
        };
        db.AgentProposals.Add(p);
        await db.SaveChangesAsync(ct);
        return new { id = p.Id, action = name, label = p.Label, expires_at = p.ExpiresAt };
    }

    public async Task<object> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.AgentProposals.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("proposal_expired");
        p.Claim(actor.Id ?? throw DomainException.Forbidden(), clock.UtcNow, approve: false);
        p.Result = "Cancelled";
        await db.SaveChangesAsync(ct);
        return new { status = p.Status, result = p.Result };
    }

    /// <summary>Carries out a proposal: single use, only by the staff member it was proposed to, before it expires.</summary>
    public async Task<object> ApproveAsync(Guid id, string? lang, JsonElement? changes, CancellationToken ct = default)
    {
        var staffId = actor.Id ?? throw DomainException.Forbidden();
        var p = await db.AgentProposals.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("proposal_expired");
        if (changes is { ValueKind: JsonValueKind.Object } edits && p.StaffUserId == staffId)
        {
            // The staff member edited the card: take only the fields this action allows, then check and describe it again.
            var merged = JsonNode.Parse(p.Arguments)!.AsObject();
            foreach (var prop in edits.EnumerateObject())
                if (Editable.TryGetValue(p.ActionName, out var keys) && keys.Contains(prop.Name))
                    merged[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
            using var check = JsonDocument.Parse(merged.ToJsonString());
            p.Label = TextUtil.Clip(TextUtil.NoEmDash(await DescribeAsync(p.ActionName, check.RootElement, lang != "en", ct)), 600);
            p.Arguments = merged.ToJsonString();
        }
        p.Claim(staffId, clock.UtcNow, approve: true);
        // Everything this approval changes is logged as done through the assistant, with what was approved.
        audit.Via = "Assistant";
        audit.ProposalId = p.Id;
        audit.Comment = $"Approved assistant proposal: {p.Label}";
        await db.SaveChangesAsync(ct);
        using var doc = JsonDocument.Parse(p.Arguments);
        var ar = lang != "en";
        string result;
        object? link = null;
        try
        {
            (result, link) = await ExecuteAsync(p.ActionName, doc.RootElement, staffId, ar, ct);
        }
        catch (DomainException e)
        {
            p.Status = ProposalStatus.Failed;
            p.Result = TextUtil.Clip(e.Code, 600);
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        p.Result = TextUtil.Clip(TextUtil.NoEmDash(result), 600);
        await db.SaveChangesAsync(ct);
        return new { status = p.Status, result = p.Result, link };
    }

    async Task<(string, object?)> ExecuteAsync(string name, JsonElement a, int staffId, bool ar, CancellationToken ct)
    {
        switch (name)
        {
            case "set_applicant_status":
            {
                var app = await ApplicantAsync(a, ct);
                await applicants.SetStatusAsync(app.Id, S(a, "status"), ct);
                return (ar ? $"تم تغيير حالة {app.FullName} إلى «{StatusAr(S(a, "status"))}»." : $"{app.FullName} is now {S(a, "status")}.", $"/applicants/{app.Id}");
            }
            case "add_note":
            {
                var app = await ApplicantAsync(a, ct);
                await applicants.AddNoteAsync(app.Id, S(a, "text"), ct);
                return (ar ? $"تمت إضافة ملاحظة إلى ملف {app.FullName}." : $"Note added to {app.FullName}.", $"/applicants/{app.Id}");
            }
            case "schedule_interview":
            {
                var app = await ApplicantAsync(a, ct);
                var panel = a.TryGetProperty("panel_ids", out var pl) && pl.ValueKind == JsonValueKind.Array
                    ? pl.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetInt32() : int.TryParse(x.GetString(), out var v) ? v : 0).Where(x => x > 0).ToList() : [];
                await interviews.ScheduleAsync(null, new InterviewInput(app.Id, I(a, "round_id"), S(a, "date"), S(a, "from"), S(a, "to"),
                    Opt(a, "location"), Opt(a, "notes"), panel), ct);
                return (ar ? $"تمت جدولة مقابلة مع {app.FullName}." : $"Interview scheduled with {app.FullName}.", $"/applicants/{app.Id}");
            }
            case "invite_to_test":
            {
                var app = await ApplicantAsync(a, ct);
                await applicants.InviteAsync(app.Id, I(a, "assessment_id"), I(a, "deadline_days") is > 0 and var n ? n : null, ct, S(a, "deadline_at") is { Length: > 0 } at ? at : null);
                return (ar ? $"تم إرسال دعوة الاختبار إلى {app.FullName}." : $"Test invitation sent to {app.FullName}.", $"/applicants/{app.Id}");
            }
            case "make_offer":
            {
                var app = await ApplicantAsync(a, ct);
                await interviews.SaveOfferAsync(null, new OfferInput(app.Id, S(a, "position_title"), S(a, "offer_date"), Opt(a, "start_date"),
                    decimal.TryParse(S(a, "monthly_salary"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var sal) ? sal : null, Opt(a, "currency"), Opt(a, "terms")), ct);
                return (ar ? $"تم إنشاء مسودة عرض عمل لـ {app.FullName}." : $"Draft offer created for {app.FullName}.", $"/applicants/{app.Id}");
            }
            case "email_candidate":
            {
                var app = await ApplicantAsync(a, ct);
                if (!await mailer.CanSendAsync(ct)) throw DomainException.Conflict("email_not_configured");
                var subject = TextUtil.Clip(TextUtil.NoEmDash(S(a, "subject")), 300);
                var signature = await mailer.SignatureAsync(app.LanguageCode, ct);
                db.EmailMessages.Add(new EmailMessage
                {
                    ToAddress = app.Email, Subject = subject,
                    HtmlBody = PortalMailer.Html(TextUtil.NoEmDash(S(a, "body")), subject, "", signature, TextUtil.IsRtl(app.LanguageCode)),
                    TextBody = $"{TextUtil.NoEmDash(S(a, "body"))}\n\n{signature}",
                    Status = EmailStatus.Queued, NextAttemptAt = clock.UtcNow, CreatedAt = clock.UtcNow,
                });
                db.ApplicationNotes.Add(new ApplicationNote { JobApplicationId = app.Id, Kind = NoteKind.System, Text = $"Email sent: {subject}", AuthorStaffId = staffId, CreatedAt = clock.UtcNow });
                await db.SaveChangesAsync(ct);
                return (ar ? $"تمت جدولة الرسالة إلى {app.FullName} للإرسال." : $"Email to {app.FullName} queued.", $"/applicants/{app.Id}");
            }
            case "create_draft_job":
            {
                var saved = await jobs.SaveAsync(null, new JobInput(S(a, "title"), S(a, "description"), I(a, "department_id") is > 0 and var di ? di : null,
                    S(a, "location"), S(a, "employment_type") is { Length: > 0 } et ? et : "FullTime", "Draft", false, null, null, null, null, null, null, false,
                    null, null, null, null), ct);
                var id = JsonSerializer.SerializeToElement(saved).TryGetProperty("id", out var idv) ? idv.GetInt32() : 0;
                return (ar ? "تم إنشاء مسودة الوظيفة." : "Draft job created.", id > 0 ? $"/jobs/{id}" : "/jobs");
            }
            case "offer_interview_times":
            {
                var app = await ApplicantAsync(a, ct);
                var r = JsonSerializer.SerializeToElement(await bookings.CreateAsync(new BookingInput(app.Id, I(a, "round_id"), Opt(a, "location"), Ids(a, "panel_ids"), Times(S(a, "times"))), ct));
                var emailed = r.TryGetProperty("emailed", out var em) && em.ValueKind == JsonValueKind.True;
                return (ar ? $"تم إرسال مواعيد المقابلة إلى {app.FullName}{(emailed ? " بالبريد" : "")}، وسيختار أحدها في بوابة الوظائف." : $"Interview times sent to {app.FullName}{(emailed ? " by email" : "")}; they will pick one in the career portal.", $"/applicants/{app.Id}");
            }
            case "talent_invite":
            {
                var r = JsonSerializer.SerializeToElement(await talent.InviteAsync(I(a, "account_id"), I(a, "job_id"), ct));
                var emailed = r.TryGetProperty("emailed", out var em) && em.ValueKind == JsonValueKind.True;
                if (!emailed) throw DomainException.Conflict("email_not_configured");
                return (ar ? "تم إرسال دعوة التقديم." : "Invitation to apply sent.", "/talent-pool");
            }
            case "set_offer_status":
            {
                var offerId = I(a, "offer_id");
                await interviews.SetOfferStatusAsync(offerId, S(a, "status"), ct);
                return (ar ? $"تم تغيير حالة العرض إلى «{OfferAr(S(a, "status"))}»." : $"Offer is now {S(a, "status")}.", "/offers");
            }
            case "set_interview_status":
            {
                var interviewId = I(a, "interview_id");
                await interviews.SetStatusAsync(interviewId, S(a, "status"), ct);
                return (ar ? "تم تحديث حالة المقابلة." : $"Interview is now {S(a, "status")}.", $"/interviews/{interviewId}");
            }
            case "set_job_selection":
            {
                var jobId = I(a, "job_id");
                await jobs.SetSelectionAsync(jobId, I(a, "role_profile_id"), new SelectionWeights(I(a, "w_cv"), I(a, "w_tests"), I(a, "w_interview"), I(a, "w_personality")), ct);
                return (ar ? "تم تحديث إعدادات الاختيار للوظيفة." : "The job's selection settings were updated.", $"/jobs/{jobId}");
            }
            case "set_job_criteria":
            {
                var jobId = I(a, "job_id");
                var approve = B(a, "approve");
                await criteria.SaveAsync(jobId, new CriteriaInput(CriteriaOf(a), approve), ct);
                return (approve
                    ? ar ? "تم اعتماد معايير الفرز، ويجري تقييم السير الذاتية المنتظرة." : "Screening criteria approved; waiting CVs are being scored."
                    : ar ? "حُفظت معايير الفرز كمسودة لمراجعتها واعتمادها." : "Screening criteria saved as a draft for review and approval.", $"/jobs/{jobId}?tab=criteria");
            }
            case "score_job_again":
            {
                var jobId = I(a, "job_id");
                await criteria.ScoreAgainAsync(jobId, ct);
                return (ar ? "يجري إعادة تقييم السير الذاتية لهذه الوظيفة." : "The job's CVs are being scored again.", $"/jobs/{jobId}?tab=criteria");
            }
            case "allow_reexam":
            {
                var invId = I(a, "invitation_id");
                await assessments.ReexamAsync(invId, Math.Max(1, I(a, "extra_attempts")), I(a, "deadline_days") is > 0 and var days ? days : 7, B(a, "notify"), ct, Opt(a, "deadline_at"));
                return (ar ? "تم منح محاولة إضافية في التقييم." : "Another attempt was allowed.", await InvitationLinkAsync(invId, ct));
            }
            case "change_deadline":
            {
                var invId = I(a, "invitation_id");
                await assessments.ChangeDeadlineAsync(invId, S(a, "deadline_at"), B(a, "notify"), ct);
                return (ar ? "تم تغيير الموعد النهائي للتقييم." : "The assessment deadline was changed.", await InvitationLinkAsync(invId, ct));
            }
            case "set_extra_time":
            {
                var invId = I(a, "invitation_id");
                await assessments.ExtraTimeAsync(invId, I(a, "percent"), ct);
                return (ar ? "تم تحديث الوقت الإضافي للتقييم." : "The assessment's extra time was updated.", await InvitationLinkAsync(invId, ct));
            }
            case "suggest_english":
                await setup.SuggestEnglishAsync(S(a, "kind"), I(a, "id"), ct);
                return (ar ? "تم طلب النسخة الإنجليزية، وستكون جاهزة للمراجعة بعد قليل." : "English suggestion requested; it will be ready for review shortly.", null);
            default:
                throw DomainException.Invalid("invalid_value");
        }
    }

    /// <summary>A plain description of the change, written by the backend from current data (never by the model).</summary>
    async Task<string> DescribeAsync(string name, JsonElement a, bool ar, CancellationToken ct)
    {
        switch (name)
        {
            case "create_draft_job":
                if (S(a, "title").Length == 0) throw DomainException.Invalid("title_required");
                return ar ? $"إنشاء مسودة وظيفة: {S(a, "title")}" : $"Create a draft job: {S(a, "title")}";
            case "talent_invite":
            {
                var accountId = I(a, "account_id");
                var candidate = await db.ApplicantAccounts.Where(u => u.Id == accountId && u.InTalentPool).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
                var jobId = I(a, "job_id");
                var job = await db.JobOpenings.Include(j => j.Translations).FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw DomainException.NotFound();
                return ar ? $"دعوة {candidate} للتقديم على وظيفة {job.In("ar").Title}" : $"Invite {candidate} to apply for {job.In("en").Title}";
            }
            case "set_offer_status":
            {
                var offerId = I(a, "offer_id");
                var o = await db.JobOffers.Where(x => x.Id == offerId).Select(x => new { x.Code, x.PositionTitle, Name = x.JobApplication!.FullName }).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
                if (!Enum.TryParse<OfferStatus>(S(a, "status"), out _)) throw DomainException.Invalid("invalid_value");
                return ar ? $"تغيير حالة عرض {o.Name} ({o.Code}) إلى «{OfferAr(S(a, "status"))}»" : $"Move the offer to {o.Name} ({o.Code}) to {S(a, "status")}";
            }
            case "set_interview_status":
            {
                var interviewId = I(a, "interview_id");
                var i = await db.Interviews.Where(x => x.Id == interviewId).Select(x => new { x.Code, Name = x.JobApplication!.FullName }).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
                if (!Enum.TryParse<InterviewStatus>(S(a, "status"), out _)) throw DomainException.Invalid("invalid_value");
                return ar ? $"تغيير حالة مقابلة {i.Name} ({i.Code}) إلى {S(a, "status")}" : $"Move the interview with {i.Name} ({i.Code}) to {S(a, "status")}";
            }
            case "set_job_selection":
            {
                var jobId = I(a, "job_id");
                var job = await db.JobOpenings.Include(j => j.Translations).FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw DomainException.NotFound();
                new SelectionWeights(I(a, "w_cv"), I(a, "w_tests"), I(a, "w_interview"), I(a, "w_personality")).Validate();
                var weights = $"{I(a, "w_tests")}/{I(a, "w_interview")}/{I(a, "w_personality")}/{I(a, "w_cv")}";
                return ar ? $"إعدادات الاختيار لوظيفة {job.In("ar").Title}: الأوزان (اختبارات/مقابلات/شخصية/سيرة) {weights}"
                    : $"Selection settings for {job.In("en").Title}: weights (tests/interviews/personality/CV) {weights}";
            }
            case "set_job_criteria":
            {
                var jobId = I(a, "job_id");
                var job = await db.JobOpenings.Include(j => j.Translations).FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw DomainException.NotFound();
                var list = CriteriaOf(a);
                if (list.Count is 0 or > CriteriaService.Max || list.Any(c => string.IsNullOrWhiteSpace(c.criterion) || c.weight is <= 0 or > 100))
                    throw DomainException.Invalid("criteria_invalid");
                var lines = string.Join("; ", list.Select(c => $"{c.criterion} ({c.weight:0.##}{(c.must ? (ar ? "، إلزامي" : ", must-have") : "")})"));
                return B(a, "approve")
                    ? ar ? $"اعتماد معايير الفرز لوظيفة {job.In("ar").Title}: {lines}" : $"Approve screening criteria for {job.In("en").Title}: {lines}"
                    : ar ? $"حفظ معايير الفرز كمسودة لوظيفة {job.In("ar").Title}: {lines}" : $"Save draft screening criteria for {job.In("en").Title}: {lines}";
            }
            case "score_job_again":
            {
                var jobId = I(a, "job_id");
                var job = await db.JobOpenings.Include(j => j.Translations).FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw DomainException.NotFound();
                var n = await db.JobApplications.CountAsync(x => x.JobOpeningId == jobId && x.AnonymisedAt == null, ct);
                return ar ? $"إعادة تقييم {n} سيرة ذاتية لوظيفة {job.In("ar").Title} وفق المعايير المعتمدة" : $"Score the {n} CVs for {job.In("en").Title} again against the approved criteria";
            }
            case "allow_reexam" or "change_deadline" or "set_extra_time":
            {
                var invId = I(a, "invitation_id");
                var inv = await db.Invitations.Where(x => x.Id == invId).Select(x => new { x.Code, Name = x.JobApplication!.FullName, Test = x.Assessment!.Title })
                    .FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
                if (name == "set_extra_time" && I(a, "percent") is < 0 or > 100) throw DomainException.Invalid("extra_time_invalid");
                var deadline = Opt(a, "deadline_at") is { } at ? clock.LocalHuman(clock.Deadline(at)) : null;
                if (name == "change_deadline" && deadline == null) throw DomainException.Invalid("deadline_invalid");
                return name switch
                {
                    "allow_reexam" => ar ? $"منح {inv.Name} محاولة إضافية في {inv.Test} ({inv.Code})، الموعد النهائي {deadline ?? $"بعد {Math.Max(1, I(a, "deadline_days") is > 0 and var d ? d : 7)} أيام"}"
                        : $"Allow {inv.Name} another attempt at {inv.Test} ({inv.Code}), deadline {deadline ?? $"in {(I(a, "deadline_days") is > 0 and var d2 ? d2 : 7)} days"}",
                    "change_deadline" => ar ? $"تغيير الموعد النهائي لتقييم {inv.Name} في {inv.Test} ({inv.Code}) إلى {deadline}" : $"Change {inv.Name}'s deadline for {inv.Test} ({inv.Code}) to {deadline}",
                    _ => ar ? $"وقت إضافي {I(a, "percent")}% لـ {inv.Name} في {inv.Test} ({inv.Code})" : $"Extra time of {I(a, "percent")}% for {inv.Name} on {inv.Test} ({inv.Code})",
                };
            }
            case "suggest_english":
                if (S(a, "kind") is not ("job" or "screening" or "question" or "assessment")) throw DomainException.Invalid("invalid_value");
                return ar ? $"اقتراح النسخة الإنجليزية ({KindAr(S(a, "kind"))} رقم {I(a, "id")})" : $"Suggest English text ({S(a, "kind")} {I(a, "id")})";
        }
        var app = await ApplicantAsync(a, ct);
        var who = $"{app.FullName} ({app.Code})";
        switch (name)
        {
            case "set_applicant_status":
                if (!Enum.TryParse<ApplicationStatus>(S(a, "status"), out _)) throw DomainException.Invalid("invalid_value");
                return ar ? $"تغيير حالة {who} إلى «{StatusAr(S(a, "status"))}»" : $"Move {who} to {S(a, "status")}";
            case "add_note":
                if (S(a, "text").Trim().Length == 0) throw DomainException.Invalid("note_required");
                return ar ? $"إضافة ملاحظة إلى {who}: {TextUtil.Clip(S(a, "text"), 300)}" : $"Add a note to {who}: {TextUtil.Clip(S(a, "text"), 300)}";
            case "schedule_interview":
            {
                var roundId = I(a, "round_id");
                var round = await db.InterviewRounds.Where(r => r.Id == roundId).Select(r => r.Name).FirstOrDefaultAsync(ct) ?? throw DomainException.Invalid("round_required");
                return ar ? $"جدولة مقابلة ({round}) مع {who} يوم {S(a, "date")} من الساعة {S(a, "from")} إلى {S(a, "to")}"
                    : $"Schedule a {round} interview with {who} on {S(a, "date")} from {S(a, "from")} to {S(a, "to")}";
            }
            case "invite_to_test":
            {
                var testId = I(a, "assessment_id");
                var test = await db.Assessments.Where(t => t.Id == testId).Select(t => t.Title).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
                return ar ? $"دعوة {who} إلى اختبار {test}" : $"Invite {who} to the {test} test";
            }
            case "make_offer":
                return ar ? $"إنشاء مسودة عرض عمل لـ {who}: {S(a, "position_title")}" : $"Create a draft offer for {who}: {S(a, "position_title")}";
            case "offer_interview_times":
            {
                var times = Times(S(a, "times"));
                if (times.Count == 0) throw DomainException.Invalid("booking_slots");
                var roundId = I(a, "round_id");
                var round = await db.InterviewRounds.Where(r => r.Id == roundId).Select(r => ar || r.NameEn == "" ? r.Name : r.NameEn).FirstOrDefaultAsync(ct) ?? throw DomainException.Invalid("round_required");
                var list = string.Join(ar ? "، " : ", ", times.Select(t => $"{t.Date} {t.From}-{t.To}"));
                return ar ? $"اقتراح مواعيد مقابلة ({round}) على {who} ليختار منها: {list}" : $"Offer {who} these {round} times to choose from: {list}";
            }
            case "email_candidate":
                if (S(a, "subject").Length == 0 || S(a, "body").Length == 0) throw DomainException.Invalid("invalid_value");
                return ar ? $"إرسال رسالة إلى {who}: {S(a, "subject")}" : $"Email {who}: {S(a, "subject")}";
            default:
                throw DomainException.Invalid("invalid_value");
        }
    }

    async Task<JobApplication> ApplicantAsync(JsonElement a, CancellationToken ct)
    {
        var id = I(a, "applicant_id");
        return await db.JobApplications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("applicant_not_found");
    }

    static string OfferAr(string status) => status switch
    {
        "Draft" => "مسودة", "Sent" => "مرسل", "Accepted" => "مقبول", "Declined" => "مرفوض", "Withdrawn" => "مسحوب", _ => status,
    };

    static List<int> Ids(JsonElement a, string name) => a.TryGetProperty(name, out var pl) && pl.ValueKind == JsonValueKind.Array
        ? pl.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetInt32() : int.TryParse(x.GetString(), out var v) ? v : 0).Where(x => x > 0).ToList()
        : S(a, name).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x.Trim(), out var v) ? v : 0).Where(x => x > 0).ToList();

    /// <summary>Interview times written one per line (or separated by ;) as "YYYY-MM-DD HH:MM-HH:MM".</summary>
    static List<BookingSlotInput> Times(string text) =>
        text.Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => System.Text.RegularExpressions.Regex.Match(l, @"^(\d{4}-\d{2}-\d{2})\s+(\d{1,2}:\d{2})\s*-\s*(\d{1,2}:\d{2})$"))
            .Where(m => m.Success).Select(m => new BookingSlotInput(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)).Take(10).ToList();

    /// <summary>Applicant statuses as the backoffice shows them in Arabic.</summary>
    static string StatusAr(string status) => status switch
    {
        "Open" => "جديد", "Replied" => "تم التواصل", "Hold" => "معلّق", "Accepted" => "مقبول", "Rejected" => "لم يتم الاختيار", _ => status,
    };

    static string KindAr(string kind) => kind switch
    {
        "job" => "وظيفة", "screening" => "سؤال فرز", "question" => "سؤال", "assessment" => "اختبار", _ => kind,
    };

    static string S(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind is JsonValueKind.Null ? "" : v.GetRawText() : "";

    static string? Opt(JsonElement e, string name) => S(e, name) is { Length: > 0 } s ? s : null;

    static bool B(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() is "true" or "1" or "yes"));

    /// <summary>The criteria list of a set_job_criteria proposal.</summary>
    static List<CriterionInput> CriteriaOf(JsonElement a)
    {
        if (!a.TryGetProperty("criteria", out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        return arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(x => new CriterionInput(
            TextUtil.Clip(S(x, "criterion").Trim(), 300),
            x.TryGetProperty("weight", out var w) && (w.ValueKind == JsonValueKind.Number ? w.TryGetDecimal(out var n) : decimal.TryParse(S(x, "weight"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out n)) ? n : 0,
            B(x, "must"))).ToList();
    }

    async Task<string?> InvitationLinkAsync(int invitationId, CancellationToken ct) =>
        await db.Invitations.Where(x => x.Id == invitationId).Select(x => (int?)x.JobApplicationId).FirstOrDefaultAsync(ct) is { } appId ? $"/applicants/{appId}?tab=tests" : null;

    static int I(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.Number ? v.TryGetInt32(out var n) : int.TryParse(v.ValueKind == JsonValueKind.String ? v.GetString() : null, out n)) ? n : 0;
}
