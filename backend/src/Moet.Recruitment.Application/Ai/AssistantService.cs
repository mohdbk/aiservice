using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Admin;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Ai;

/// <summary>
/// The HR assistant: answers questions with read-only tools and proposes changes that the same staff member must
/// approve. Every turn and tool call is logged (ops.AssistantMessage).
/// </summary>
public sealed class AssistantService(
    IAppDbContext db, ILanguageModel model, ICurrentActor actor, IClock clock, ContentService content,
    AdminApplicantService applicants, AdminInterviewService interviews, PortalMailer mailer)
{
    const int MaxSteps = 8;
    const int DailyMessages = 200;
    static readonly TimeSpan ActionLifetime = TimeSpan.FromMinutes(30);

    const string SystemPrompt = $"""
        You are the Ministry of Economy & Tourism's recruitment assistant inside the HR admin panel.
        - Writing style: {Prompts.WritingRule}
        - Reply in the user's language (Arabic or English). Be concise. Markdown is rendered: use short lists, **bold** and small tables.
        - Use the tools; never guess codes, scores, people or statuses. Applicants are referenced by code (APP-...), jobs by code (JOB-...).
        - To find people by skill, employer or name use search_applicants. To compare, use compare_applicants and answer with a table.
        - Scores are recommendations only. People make hiring decisions; you cannot reject, accept or delete anything.
        - set_applicant_status, schedule_interview and send_candidate_email only PROPOSE an action that waits for the user's Approve button. Never claim a proposed action is done.
        - Candidate emails must be polite and neutral, written as the hiring team. Never mention AI, automation, scores or this assistant to candidates.
        - The user's current page is given as context; "this applicant" or "this job" refers to it.
        """;

    static object P(string type, string description) => new { type, description };

    static readonly ToolSpec[] Tools =
    [
        new("list_jobs", "Job openings with applicant counts.", Schema(new() { ["status"] = P("string", "Draft, Open or Closed (default Open)") })),
        new("get_job", "A job opening: description, criteria and pipeline counts.", Schema(new() { ["code"] = P("string", "Job code, JOB-...") }, "code")),
        new("search_applicants", "Find applicants by name, skill, employer or summary text, optionally for one job.",
            Schema(new() { ["query"] = P("string", "Words to look for"), ["job_code"] = P("string", "Optional job code") }, "query")),
        new("get_applicant", "An applicant's record: status, score, criteria, screening, tests and interviews.", Schema(new() { ["code"] = P("string", "Applicant code, APP-...") }, "code")),
        new("compare_applicants", "Side-by-side data for 2 to 5 applicants.",
            Schema(new() { ["codes"] = new { type = "array", items = new { type = "string" }, description = "Applicant codes" } }, "codes")),
        new("upcoming_interviews", "Interviews in the next days.", Schema(new() { ["days"] = P("integer", "How many days ahead (default 7)") })),
        new("set_applicant_status", "PROPOSE moving an applicant to Open, Replied or Hold.",
            Schema(new() { ["code"] = P("string", "Applicant code"), ["status"] = new { type = "string", @enum = new[] { "Open", "Replied", "Hold" } } }, "code", "status")),
        new("schedule_interview", "PROPOSE an interview. date YYYY-MM-DD, times HH:MM UAE time, panel is a list of staff emails.",
            Schema(new()
            {
                ["code"] = P("string", "Applicant code"), ["round"] = P("string", "Interview round name"), ["date"] = P("string", "YYYY-MM-DD"),
                ["from"] = P("string", "HH:MM"), ["to"] = P("string", "HH:MM"), ["location"] = P("string", "Room or meeting link"),
                ["panel"] = new { type = "array", items = new { type = "string" } },
            }, "code", "round", "date", "from", "to")),
        new("send_candidate_email", "PROPOSE an email to an applicant (plain text body).",
            Schema(new() { ["code"] = P("string", "Applicant code"), ["subject"] = P("string", "Subject"), ["body"] = P("string", "Body") }, "code", "subject", "body")),
    ];

    static readonly HashSet<string> WriteTools = ["set_applicant_status", "schedule_interview", "send_candidate_email"];

    static object Schema(Dictionary<string, object> props, params string[] required) =>
        new Dictionary<string, object> { ["type"] = "object", ["properties"] = props, ["required"] = required };

    public async Task<object> ChatAsync(string? message, string? sessionId, string? page, CancellationToken ct = default)
    {
        var staffId = actor.Id ?? throw DomainException.Forbidden();
        if (!(await content.SettingsAsync(ct)).AssistantEnabled || !model.IsConfigured) throw DomainException.Forbidden("assistant_off");
        message = TextUtil.Clip((message ?? "").Trim(), 4000);
        if (message.Length == 0) throw DomainException.Invalid("message_required");
        sessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N")[..16] : TextUtil.Clip(sessionId, 40);
        var since = clock.UtcNow.AddDays(-1);
        if (await db.AssistantMessages.CountAsync(m => m.StaffUserId == staffId && m.CreatedAt >= since, ct) >= DailyMessages)
            throw new DomainException("too_many_messages", 429);

        var sw = Stopwatch.StartNew();
        var history = await db.AssistantMessages.AsNoTracking().Where(m => m.StaffUserId == staffId && m.SessionId == sessionId)
            .OrderByDescending(m => m.CreatedAt).Take(10).ToListAsync(ct);
        var msgs = new List<ChatMessage>
        {
            new("system", SystemPrompt),
            new("system", $"Current user: {actor.Name}. Today: {clock.Today():yyyy-MM-dd}. Current page: {TextUtil.Clip(page, 300)}"),
        };
        foreach (var h in history.AsEnumerable().Reverse())
        {
            msgs.Add(new ChatMessage("user", h.Question));
            msgs.Add(new ChatMessage("assistant", h.Reply));
        }
        msgs.Add(new ChatMessage("user", message));

        var trace = new List<object>();
        var actions = new List<object>();
        int prompt = 0, completion = 0;
        string? reply = null;
        var log = new AssistantMessage { StaffUserId = staffId, SessionId = sessionId, Question = message, Model = TextUtil.Clip(model.ModelName, 100), CreatedAt = clock.UtcNow };
        try
        {
            for (var step = 0; step < MaxSteps && reply == null; step++)
            {
                var turn = await model.ChatAsync(msgs, Tools, ct);
                prompt += turn.PromptTokens;
                completion += turn.CompletionTokens;
                if (turn.ToolCalls.Count == 0)
                {
                    reply = TextUtil.NoEmDash(turn.Content ?? "");
                    break;
                }
                msgs.Add(new ChatMessage("assistant", turn.Content ?? "", turn.ToolCalls));
                foreach (var call in turn.ToolCalls)
                {
                    object result;
                    try
                    {
                        using var args = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
                        if (WriteTools.Contains(call.Name))
                        {
                            var a = await ProposeAsync(staffId, sessionId, call.Name, args.RootElement, ct);
                            actions.Add(a);
                            result = "Proposed and shown to the user with an Approve button. NOT applied yet.";
                        }
                        else result = await ReadToolAsync(call.Name, args.RootElement, ct);
                    }
                    catch (DomainException e) { result = $"Error: {e.Code}"; }
                    catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { result = $"Error: {TextUtil.Clip(e.Message, 300)}"; }
                    var json = JsonSerializer.Serialize(result);
                    trace.Add(new { tool = call.Name, args = call.Arguments, result = TextUtil.Clip(json, 600) });
                    msgs.Add(new ChatMessage("tool", TextUtil.Clip(json, 8000), ToolCallId: call.Id));
                }
            }
            reply ??= "I could not finish that in a few steps. Try a narrower question.";
            log.Status = AssistantStatus.Success;
            return new { session_id = sessionId, reply, actions };
        }
        catch (Exception) when (reply == null)
        {
            log.Status = AssistantStatus.Error;
            reply = "";
            throw;
        }
        finally
        {
            log.Reply = reply ?? "";
            log.ToolTrace = JsonSerializer.Serialize(trace);
            log.PromptTokens = prompt;
            log.CompletionTokens = completion;
            log.LatencyMs = (int)sw.ElapsedMilliseconds;
            db.AssistantMessages.Add(log);
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    public async Task<object> HistoryAsync(string? sessionId, CancellationToken ct = default)
    {
        var staffId = actor.Id ?? throw DomainException.Forbidden();
        sessionId ??= await db.AssistantMessages.Where(m => m.StaffUserId == staffId).OrderByDescending(m => m.CreatedAt).Select(m => m.SessionId).FirstOrDefaultAsync(ct);
        var rows = await db.AssistantMessages.AsNoTracking()
            .Where(m => sessionId != null && m.StaffUserId == staffId && m.SessionId == sessionId).OrderBy(m => m.CreatedAt).Take(60)
            .Select(m => new { question = m.Question, reply = m.Reply, created_at = m.CreatedAt }).ToListAsync(ct);
        return new { session_id = sessionId, messages = rows };
    }

    // ---------------- read tools ----------------

    async Task<object> ReadToolAsync(string name, JsonElement a, CancellationToken ct)
    {
        switch (name)
        {
            case "list_jobs":
            {
                var status = Enum.TryParse<JobStatus>(S(a, "status"), out var st) ? st : JobStatus.Open;
                return await db.JobOpenings.AsNoTracking().Where(j => j.Status == status).OrderByDescending(j => j.CreatedAt).Take(50)
                    .Select(j => new { j.Code, j.Title, status = j.Status, applicants = db.JobApplications.Count(x => x.JobOpeningId == j.Id) }).ToListAsync(ct);
            }
            case "get_job":
            {
                var code = S(a, "code");
                var j = await db.JobOpenings.AsNoTracking().Include(x => x.Criteria).FirstOrDefaultAsync(x => x.Code == code, ct) ?? throw DomainException.NotFound();
                var counts = await db.JobApplications.Where(x => x.JobOpeningId == j.Id).GroupBy(x => x.Status)
                    .Select(g => new { status = g.Key, count = g.Count() }).ToListAsync(ct);
                return new
                {
                    j.Code, j.Title, status = j.Status, description = TextUtil.Clip(TextUtil.StripHtml(j.Description), 4000),
                    criteria = j.Criteria.OrderBy(c => c.SortOrder).Select(c => new { c.Criterion, c.Weight, c.IsMustHave }), pipeline = counts,
                };
            }
            case "search_applicants":
            {
                var q = S(a, "query");
                var job = S(a, "job_code");
                var query = db.JobApplications.AsNoTracking().Where(x => x.FullName.Contains(q) || x.Email.Contains(q) || x.Code == q
                    || x.CvAnalysis!.Summary.Contains(q) || x.CvAnalysis.ProfileJson!.Contains(q));
                if (job.Length > 0) query = query.Where(x => x.JobOpening!.Code == job);
                return await query.OrderByDescending(x => x.CvAnalysis!.Score).Take(10).Select(x => new
                {
                    x.Code, x.FullName, job = x.JobOpening!.Code, status = x.Status, score = x.CvAnalysis == null ? null : x.CvAnalysis.Score,
                }).ToListAsync(ct);
            }
            case "get_applicant":
                return await ApplicantBriefAsync(S(a, "code"), ct);
            case "compare_applicants":
            {
                var codes = a.TryGetProperty("codes", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().Select(x => x.GetString() ?? "").Distinct().ToList() : [];
                if (codes.Count is < 2 or > 5) throw DomainException.Invalid("compare_2_to_5");
                var list = new List<object>();
                foreach (var code in codes) list.Add(await ApplicantBriefAsync(code, ct));
                return list;
            }
            case "upcoming_interviews":
            {
                var days = a.TryGetProperty("days", out var d) && d.TryGetInt32(out var n) ? Math.Clamp(n, 1, 60) : 7;
                var now = clock.UtcNow;
                var until = now.AddDays(days);
                var rows = await db.Interviews.AsNoTracking().Where(i => i.StartsAt >= now && i.StartsAt <= until && i.Status == InterviewStatus.Scheduled)
                    .OrderBy(i => i.StartsAt).Select(i => new { i.Code, i.StartsAt, applicant = i.JobApplication!.Code, name = i.JobApplication.FullName, round = i.InterviewRound!.Name })
                    .ToListAsync(ct);
                return rows.Select(r => new { r.Code, starts = clock.LocalMinute(r.StartsAt), r.applicant, r.name, r.round });
            }
            default:
                throw new KeyNotFoundException($"Unknown tool {name}");
        }
    }

    async Task<object> ApplicantBriefAsync(string code, CancellationToken ct)
    {
        var x = await db.JobApplications.AsNoTracking().Where(a => a.Code == code)
                    .Include(a => a.JobOpening).Include(a => a.ScreeningAnswers).Include(a => a.CvAnalysis!).ThenInclude(c => c.CriterionScores)
                    .AsSplitQuery().FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("applicant_not_found");
        var tests = await db.Invitations.AsNoTracking().Where(i => i.JobApplicationId == x.Id)
            .Select(i => new { test = i.Assessment!.Title, status = i.Status, i.Score, i.Result }).ToListAsync(ct);
        var c = x.CvAnalysis;
        return new
        {
            x.Code, x.FullName, job = x.JobOpening!.Code, job_title = x.JobOpening.Title, status = x.Status.ToString(),
            screening = x.ScreeningOutcome.ToString(), score = c?.Score, years = c?.YearsOfExperience, summary = c?.Summary, gaps = c?.MustHaveGaps,
            criteria = c?.CriterionScores.OrderBy(s => s.SortOrder).Select(s => new { s.Criterion, s.Score, s.Evidence }),
            answers = x.ScreeningAnswers.OrderBy(s => s.SortOrder).Select(s => new { s.QuestionText, s.AnswerText }), tests,
        };
    }

    // ---------------- proposals ----------------

    async Task<object> ProposeAsync(int staffId, string sessionId, string name, JsonElement a, CancellationToken ct)
    {
        var code = S(a, "code");
        var app = await db.JobApplications.AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct) ?? throw DomainException.NotFound("applicant_not_found");
        var label = name switch
        {
            "set_applicant_status" => $"Move {app.FullName} ({app.Code}) to {S(a, "status")}",
            "schedule_interview" => $"Schedule a {S(a, "round")} interview with {app.FullName} on {S(a, "date")} from {S(a, "from")} to {S(a, "to")}",
            _ => $"Email {app.FullName}: {S(a, "subject")}",
        };
        var action = new AssistantAction
        {
            Id = Guid.NewGuid(), StaffUserId = staffId, SessionId = sessionId, ActionName = name, Arguments = a.GetRawText(),
            Label = TextUtil.Clip(TextUtil.NoEmDash(label), 600), ExpiresAt = clock.UtcNow + ActionLifetime,
        };
        db.AssistantActions.Add(action);
        await db.SaveChangesAsync(ct);
        return new { id = action.Id, label = action.Label, name };
    }

    /// <summary>Carries out a proposal. Single use, only by the staff member it was proposed to, within 30 minutes.</summary>
    public async Task<object> ApproveAsync(Guid id, CancellationToken ct = default)
    {
        var staffId = actor.Id ?? throw DomainException.Forbidden();
        var action = await db.AssistantActions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("proposal_expired");
        if (action.StaffUserId != staffId) throw DomainException.Forbidden();
        if (action.UsedAt != null || action.ExpiresAt < clock.UtcNow) throw DomainException.Conflict("proposal_expired");
        action.UsedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        using var doc = JsonDocument.Parse(action.Arguments);
        var a = doc.RootElement;
        var code = S(a, "code");
        var app = await db.JobApplications.FirstOrDefaultAsync(x => x.Code == code, ct) ?? throw DomainException.NotFound("applicant_not_found");
        string result;
        switch (action.ActionName)
        {
            case "set_applicant_status":
                if (S(a, "status") is not ("Open" or "Replied" or "Hold")) throw DomainException.Invalid("invalid_value");
                await applicants.SetStatusAsync(app.Id, S(a, "status"), ct);
                result = $"{app.FullName} is now {S(a, "status")}.";
                break;
            case "schedule_interview":
            {
                var roundName = S(a, "round");
                var round = await db.InterviewRounds.FirstOrDefaultAsync(r => r.Name == roundName, ct) ?? throw DomainException.Invalid("round_required");
                var emails = a.TryGetProperty("panel", out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().Select(x => (x.GetString() ?? "").ToLowerInvariant()).ToList() : [];
                var panel = await db.StaffUsers.Where(u => emails.Contains(u.Email)).Select(u => u.Id).ToListAsync(ct);
                await interviews.ScheduleAsync(null, new InterviewInput(app.Id, round.Id, S(a, "date"), S(a, "from"), S(a, "to"), S(a, "location"), null, panel), ct);
                result = $"Interview scheduled with {app.FullName}.";
                break;
            }
            case "send_candidate_email":
            {
                if (!mailer.CanSend) throw DomainException.Conflict("email_not_configured");
                var body = TextUtil.NoEmDash(S(a, "body"));
                db.EmailMessages.Add(new EmailMessage
                {
                    ToAddress = app.Email, Subject = TextUtil.Clip(TextUtil.NoEmDash(S(a, "subject")), 300),
                    HtmlBody = PortalMailer.Html(body, S(a, "subject"), "", "Ministry of Economy & Tourism", TextUtil.IsRtl(app.LanguageCode)),
                    Status = EmailStatus.Queued, NextAttemptAt = clock.UtcNow, CreatedAt = clock.UtcNow,
                });
                db.ApplicationNotes.Add(new ApplicationNote { JobApplicationId = app.Id, Kind = NoteKind.System, Text = $"Email sent: {S(a, "subject")}", AuthorStaffId = staffId, CreatedAt = clock.UtcNow });
                await db.SaveChangesAsync(ct);
                result = $"Email to {app.Email} queued.";
                break;
            }
            default:
                throw DomainException.Invalid("invalid_value");
        }
        action.Result = TextUtil.Clip(result, 600);
        await db.SaveChangesAsync(ct);
        return new { result };
    }

    static string S(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText() : "";
}
