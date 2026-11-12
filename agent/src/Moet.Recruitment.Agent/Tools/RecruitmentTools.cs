using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Moet.Recruitment.Agent.A2ui;
using Moet.Recruitment.Agent.Backoffice;

namespace Moet.Recruitment.Agent.Tools;

/// <summary>
/// The agent's tools. Read tools return backoffice data; show tools return A2UI surfaces for the backoffice to draw;
/// propose tools ask the backend to record a change and return an approval card. Nothing changes until the staff
/// member presses Approve, and the backend then carries the change out with that person's permissions.
/// </summary>
public sealed class RecruitmentTools(BackofficeApi api)
{
    const int MaxResultChars = 12000;

    public IList<AITool> All() =>
    [
        AIFunctionFactory.Create(ListJobs, "list_jobs"),
        AIFunctionFactory.Create(GetJob, "get_job"),
        AIFunctionFactory.Create(SearchApplicants, "search_applicants"),
        AIFunctionFactory.Create(GetApplicant, "get_applicant"),
        AIFunctionFactory.Create(ListInterviews, "list_interviews"),
        AIFunctionFactory.Create(ListOffers, "list_offers"),
        AIFunctionFactory.Create(ListTests, "list_tests"),
        AIFunctionFactory.Create(ListTestInvitations, "list_test_invitations"),
        AIFunctionFactory.Create(GetLookups, "get_lookups"),
        AIFunctionFactory.Create(GetDashboard, "get_dashboard"),
        AIFunctionFactory.Create(ShowApplicants, "show_applicants"),
        AIFunctionFactory.Create(CompareApplicants, "compare_applicants"),
        AIFunctionFactory.Create(ProposeStatusChange, "propose_status_change"),
        AIFunctionFactory.Create(ProposeNote, "propose_note"),
        AIFunctionFactory.Create(ProposeInterview, "propose_interview"),
        AIFunctionFactory.Create(ProposeTestInvitation, "propose_test_invitation"),
        AIFunctionFactory.Create(ProposeOffer, "propose_offer"),
        AIFunctionFactory.Create(ProposeEmail, "propose_email"),
        AIFunctionFactory.Create(ProposeDraftJob, "propose_draft_job"),
        AIFunctionFactory.Create(ProposeEnglishSuggestion, "propose_english_suggestion"),
    ];

    bool Ar => api.Lang == "ar";
    string L(string ar, string en) => Ar ? ar : en;

    // ---------------- read ----------------

    [Description("Job openings, newest first, with applicant counts. Returns items and paging.")]
    public Task<JsonElement> ListJobs(
        [Description("Draft, Open or Closed; empty for all")] string? status = null,
        [Description("Words in the title or code")] string? search = null,
        [Description("Page number, from 1")] int page = 1,
        CancellationToken ct = default) =>
        Read($"jobs?{Q(("status", status), ("q", search), ("page", page.ToString(CultureInfo.InvariantCulture)), ("size", "20"))}", ct);

    [Description("One job opening: description, criteria, screening questions, tests and translations.")]
    public Task<JsonElement> GetJob([Description("Job id")] int jobId, CancellationToken ct = default) => Read($"jobs/{jobId}", ct);

    [Description("Find applicants. Combine filters freely. Use cv_text for skills, employers or qualifications found in CVs.")]
    public Task<JsonElement> SearchApplicants(
        [Description("Name, email, phone or applicant code")] string? search = null,
        [Description("Words to find in CV summaries and profiles, for example a skill")] string? cvText = null,
        [Description("Only this job id")] int? jobId = null,
        [Description("Open, Replied, Hold, Accepted or Rejected")] string? status = null,
        [Description("Meets, DoesNotMeet or NotApplicable")] string? screening = null,
        [Description("Lowest CV score, 0 to 100")] decimal? minScore = null,
        [Description("Sort by: score, created, name, status")] string sort = "score",
        [Description("asc or desc")] string dir = "desc",
        [Description("Page number, from 1")] int page = 1,
        CancellationToken ct = default) =>
        Read("applicants?" + Q(("q", search), ("cv", cvText), ("job", jobId?.ToString(CultureInfo.InvariantCulture)), ("status", status),
            ("screening", screening), ("minScore", minScore?.ToString(CultureInfo.InvariantCulture)), ("sort", sort), ("dir", dir),
            ("page", page.ToString(CultureInfo.InvariantCulture)), ("size", "20")), ct);

    [Description("An applicant's full record: CV analysis and criteria scores, screening answers, notes, tests, interviews and offers.")]
    public Task<JsonElement> GetApplicant([Description("Applicant id")] int applicantId, CancellationToken ct = default) => Read($"applicants/{applicantId}", ct);

    [Description("Interviews. scope: upcoming, past or all.")]
    public Task<JsonElement> ListInterviews(
        [Description("upcoming, past or all")] string scope = "upcoming",
        [Description("Scheduled, Completed, Cancelled or NoShow")] string? status = null,
        [Description("Only this job id")] int? jobId = null,
        CancellationToken ct = default) =>
        Read("interviews?" + Q(("scope", scope), ("status", status), ("job", jobId?.ToString(CultureInfo.InvariantCulture)), ("size", "25")), ct);

    [Description("Job offers.")]
    public Task<JsonElement> ListOffers([Description("Draft, Sent, Accepted, Declined or Withdrawn")] string? status = null, CancellationToken ct = default) =>
        Read("offers?" + Q(("status", status), ("size", "25")), ct);

    [Description("Assessment tests.")]
    public Task<JsonElement> ListTests([Description("Draft, Published or Archived")] string? status = null, CancellationToken ct = default) =>
        Read("assessments?" + Q(("status", status), ("size", "25")), ct);

    [Description("Test invitations and results.")]
    public Task<JsonElement> ListTestInvitations(
        [Description("Only this job id")] int? jobId = null,
        [Description("Only this test id")] int? testId = null,
        [Description("Pass or Fail")] string? result = null,
        CancellationToken ct = default) =>
        Read("invitations?" + Q(("job", jobId?.ToString(CultureInfo.InvariantCulture)), ("assessment", testId?.ToString(CultureInfo.InvariantCulture)), ("result", result), ("size", "25")), ct);

    [Description("Ids and names of departments, interview rounds, staff (for interview panels), published tests and screening questions.")]
    public Task<JsonElement> GetLookups(CancellationToken ct = default) => Read("lookups", ct);

    [Description("Recruitment totals and recent activity.")]
    public Task<JsonElement> GetDashboard(CancellationToken ct = default) => Read("dashboard", ct);

    // ---------------- show (A2UI) ----------------

    [Description("Shows applicants as cards in the chat (name, job, status, score, summary, open button). Use after finding people.")]
    public async Task<JsonElement> ShowApplicants([Description("Applicant ids, up to 6")] int[] applicantIds, CancellationToken ct = default)
    {
        if (applicantIds.Length is 0 or > 6) return Error("give_1_to_6_ids");
        var s = new Surface($"applicants-{Guid.NewGuid():N}"[..20]);
        var cards = new List<string>();
        foreach (var id in applicantIds.Distinct())
        {
            JsonElement a;
            try { a = await api.GetAsync($"applicants/{id}", ct); }
            catch (BackofficeException e) { return Error(e.Code); }
            var score = Score(a);
            var header = s.Row([s.Text(Str(a, "full_name"), "h4"), s.Text(score, "h4")], justify: "spaceBetween");
            var job = s.Text($"{Str(a, "code")} · {Str(a.GetProperty("job"), "title")} · {Str(a, "status")}", "caption");
            var parts = new List<string> { header, job };
            if (a.TryGetProperty("analysis", out var an) && an.ValueKind == JsonValueKind.Object)
            {
                var summary = Ar && Str(an, "summary_ar").Length > 0 ? Str(an, "summary_ar") : Str(an, "summary");
                if (summary.Length > 0) parts.Add(s.Text(Clip(summary, 400)));
            }
            parts.Add(s.Button(L("فتح الملف", "Open Profile"), "open_applicant", new JsonObject { ["applicant_id"] = id }, "borderless"));
            cards.Add(s.Card(s.Column(parts)));
        }
        s.Column(cards, id: "root");
        return Ui(s, $"Shown {cards.Count} applicant cards.");
    }

    [Description("Shows a side-by-side comparison of 2 to 5 applicants: score, status, screening, years of experience and each criterion.")]
    public async Task<JsonElement> CompareApplicants([Description("Applicant ids, 2 to 5")] int[] applicantIds, CancellationToken ct = default)
    {
        var ids = applicantIds.Distinct().ToArray();
        if (ids.Length is < 2 or > 5) return Error("give_2_to_5_ids");
        var people = new List<JsonElement>();
        foreach (var id in ids)
        {
            try { people.Add(await api.GetAsync($"applicants/{id}", ct)); }
            catch (BackofficeException e) { return Error(e.Code); }
        }
        var s = new Surface($"compare-{Guid.NewGuid():N}"[..20]);
        string RowOf(string label, Func<JsonElement, string> value, string variant = "body") =>
            s.Row([s.Text(label, "caption"), .. people.Select(p => s.Text(value(p), variant))]);

        var rows = new List<string>
        {
            s.Text(L("مقارنة المتقدمين", "Applicant Comparison"), "h3"),
            RowOf("", p => Str(p, "full_name"), "h5"),
            s.Divider(),
            RowOf(L("التقييم", "Score"), Score),
            RowOf(L("الحالة", "Status"), p => Str(p, "status")),
            RowOf(L("أسئلة الفرز", "Screening"), p => p.TryGetProperty("screening", out var sc) ? Str(sc, "outcome") : ""),
            RowOf(L("سنوات الخبرة", "Years of Experience"), p => Analysis(p) is { } an ? Str(an, "years") : ""),
        };
        var criteria = people.SelectMany(p => Analysis(p) is { } an && an.TryGetProperty("criteria", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray().Select(x => Str(x, "criterion")) : []).Where(x => x.Length > 0).Distinct().Take(10).ToList();
        if (criteria.Count > 0) rows.Add(s.Divider());
        foreach (var criterion in criteria)
            rows.Add(RowOf(criterion, p => Analysis(p) is { } an && an.TryGetProperty("criteria", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().Where(x => Str(x, "criterion") == criterion).Select(x => Str(x, "score")).FirstOrDefault() ?? "" : ""));
        rows.Add(s.Row([.. people.Select(p => s.Button(L("فتح", "Open"), "open_applicant",
            new JsonObject { ["applicant_id"] = p.GetProperty("id").GetInt32() }, "borderless"))]));
        s.Card(s.Column(rows), id: "root");

        var summary = string.Join("; ", people.Select(p => $"{Str(p, "full_name")} (id {p.GetProperty("id").GetInt32()}): score {Score(p)}, status {Str(p, "status")}"));
        return Ui(s, $"Comparison shown. {summary}");
    }

    // ---------------- propose (approval cards) ----------------

    [Description("Proposes moving an applicant to another status. Shows an approval card; nothing changes until the user approves.")]
    public Task<JsonElement> ProposeStatusChange(int applicantId, [Description("Open, Replied, Hold, Accepted or Rejected")] string status, CancellationToken ct = default) =>
        Propose("set_applicant_status", new JsonObject { ["applicant_id"] = applicantId, ["status"] = status }, ct);

    [Description("Proposes adding an internal note to an applicant. Shows an approval card.")]
    public Task<JsonElement> ProposeNote(int applicantId, string text, CancellationToken ct = default) =>
        Propose("add_note", new JsonObject { ["applicant_id"] = applicantId, ["text"] = text }, ct);

    [Description("Proposes an interview. Get round and staff ids from get_lookups. date YYYY-MM-DD, times HH:MM in UAE time. Shows an approval card.")]
    public Task<JsonElement> ProposeInterview(int applicantId, int roundId, string date, string from, string to,
        [Description("Room or meeting link")] string? location = null, [Description("Staff ids on the panel")] int[]? panelIds = null,
        string? notes = null, CancellationToken ct = default) =>
        Propose("schedule_interview", new JsonObject
        {
            ["applicant_id"] = applicantId, ["round_id"] = roundId, ["date"] = date, ["from"] = from, ["to"] = to, ["location"] = location,
            ["notes"] = notes, ["panel_ids"] = new JsonArray([.. (panelIds ?? []).Select(x => (JsonNode)x)]),
        }, ct);

    [Description("Proposes inviting an applicant to a published test (ids from get_lookups). Shows an approval card.")]
    public Task<JsonElement> ProposeTestInvitation(int applicantId, int testId, [Description("Days to complete it")] int? deadlineDays = null, CancellationToken ct = default) =>
        Propose("invite_to_test", new JsonObject { ["applicant_id"] = applicantId, ["assessment_id"] = testId, ["deadline_days"] = deadlineDays }, ct);

    [Description("Proposes a draft job offer (not sent to the candidate). Dates YYYY-MM-DD. Shows an approval card.")]
    public Task<JsonElement> ProposeOffer(int applicantId, string positionTitle, string offerDate, string? startDate = null,
        decimal? monthlySalary = null, [Description("Currency code, default AED")] string? currency = null, string? terms = null, CancellationToken ct = default) =>
        Propose("make_offer", new JsonObject
        {
            ["applicant_id"] = applicantId, ["position_title"] = positionTitle, ["offer_date"] = offerDate, ["start_date"] = startDate,
            ["monthly_salary"] = monthlySalary, ["currency"] = currency ?? "AED", ["terms"] = terms,
        }, ct);

    [Description("Proposes an email to an applicant, in the applicant's language, written as the hiring team. Plain text body. Shows an approval card with the full text.")]
    public Task<JsonElement> ProposeEmail(int applicantId, string subject, string body, CancellationToken ct = default) =>
        Propose("email_candidate", new JsonObject { ["applicant_id"] = applicantId, ["subject"] = subject, ["body"] = body }, ct, preview: body);

    [Description("Proposes a new unpublished draft job. description is simple HTML (p, ul, li, strong). Shows an approval card.")]
    public Task<JsonElement> ProposeDraftJob(string title, string description, string location,
        [Description("FullTime, PartTime, Contract, Temporary or Internship")] string employmentType = "FullTime",
        [Description("Department id from get_lookups")] int? departmentId = null, CancellationToken ct = default) =>
        Propose("create_draft_job", new JsonObject
        {
            ["title"] = title, ["description"] = description, ["location"] = location, ["employment_type"] = employmentType, ["department_id"] = departmentId,
        }, ct);

    [Description("Proposes asking for an English version of Arabic content for HR to review. kind: job, screening, question or assessment.")]
    public Task<JsonElement> ProposeEnglishSuggestion(string kind, int id, CancellationToken ct = default) =>
        Propose("suggest_english", new JsonObject { ["kind"] = kind, ["id"] = id }, ct);

    async Task<JsonElement> Propose(string action, JsonObject arguments, CancellationToken ct, string? preview = null)
    {
        JsonElement p;
        try { p = await api.PostAsync("agent/proposals", new { action, arguments, lang = api.Lang }, ct); }
        catch (BackofficeException e) { return Error(e.Code); }
        var id = Str(p, "id");
        var s = new Surface($"proposal-{id}");
        s.Set("proposal_id", id).Set("state", "pending");
        var parts = new List<string>
        {
            s.Text(L("بانتظار موافقتك", "Needs Your Approval"), "h5"),
            s.Text(Str(p, "label")),
        };
        if (preview is { Length: > 0 }) parts.Add(s.Text(Clip(preview, 3000), "caption"));
        parts.Add(s.Row([
            s.Button(L("موافقة", "Approve"), "approve_proposal", new JsonObject { ["proposal_id"] = new JsonObject { ["@path"] = "/proposal_id" } }, "primary"),
            s.Button(L("إلغاء", "Cancel"), "cancel_proposal", new JsonObject { ["proposal_id"] = new JsonObject { ["@path"] = "/proposal_id" } }, "borderless"),
        ], id: "actions"));
        s.Card(s.Column(parts), id: "root");
        return Ui(s, $"Proposal {id} shown to the user with Approve and Cancel buttons. NOT applied yet: {Str(p, "label")}");
    }

    // ---------------- helpers ----------------

    async Task<JsonElement> Read(string path, CancellationToken ct)
    {
        try
        {
            var r = await api.GetAsync(path, ct);
            if (r.ValueKind == JsonValueKind.Undefined) return Error("empty");
            var raw = r.GetRawText();
            return raw.Length <= MaxResultChars ? r : JsonSerializer.SerializeToElement(new { truncated = true, json = raw[..MaxResultChars] });
        }
        catch (BackofficeException e) { return Error(e.Code); }
    }

    static JsonElement Ui(Surface s, string summary) => JsonSerializer.SerializeToElement(new JsonObject { ["summary"] = summary, ["a2ui"] = s.Messages() });

    static JsonElement Error(string code) => JsonSerializer.SerializeToElement(new { error = code });

    static string Q(params (string Key, string? Value)[] pairs) =>
        string.Join("&", pairs.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

    static JsonElement? Analysis(JsonElement p) => p.TryGetProperty("analysis", out var a) && a.ValueKind == JsonValueKind.Object ? a : null;

    static string Score(JsonElement p) => Analysis(p) is { } a && a.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? $"{s.GetDecimal():0.#}" : "-";

    static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString() ?? "", JsonValueKind.Null or JsonValueKind.Undefined => "", _ => v.GetRawText() }
            : "";

    static string Clip(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
