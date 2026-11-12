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
        AIFunctionFactory.Create(RankApplicants, "rank_applicants"),
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
        AIFunctionFactory.Create(AskUser, "ask_user"),
        AIFunctionFactory.Create(ListComponents, "list_components"),
        AIFunctionFactory.Create(ShowComponent, "show_component"),
        AIFunctionFactory.Create(ListEmailTemplates, "list_email_templates"),
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

    [Description("A job's applicants ranked by combined selection score (out of 100): technical assessments, interview panel ratings and personality fit with the job's role profile, weighted as set on the job (by default 40, 40 and 20). Parts without a result are left out (complete false). Returns items with rank, total and each part, plus the weights.")]
    public Task<JsonElement> RankApplicants(
        [Description("Job id")] int jobId,
        [Description("Include rejected applicants")] bool includeRejected = false,
        [Description("Page number, from 1")] int page = 1,
        CancellationToken ct = default) =>
        Read($"jobs/{jobId}/ranking?{Q(("all", includeRejected ? "true" : null), ("page", page.ToString(CultureInfo.InvariantCulture)), ("size", "25"))}", ct);

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

    [Description("Shows a side-by-side comparison of 2 to 5 applicants: score, status, screening, years of experience, each criterion and, when completed, their personality questionnaire profile.")]
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
            RowOf(L("درجة الاختيار", "Selection Score"), SelectionTotal),
            RowOf(L("تقييم السيرة الذاتية", "CV Score"), Score),
            RowOf(L("الحالة", "Status"), p => Statuses().Where(x => x.Item1 == Str(p, "status")).Select(x => x.Item2).FirstOrDefault() ?? Str(p, "status")),
            RowOf(L("أسئلة الفرز", "Screening"), p => (p.TryGetProperty("screening", out var sc) ? Str(sc, "outcome") : "") switch
            {
                "Meets" => L("يستوفي المتطلبات", "Meets Requirements"), "DoesNotMeet" => L("لا يستوفي المتطلبات", "Does Not Meet"),
                "NotApplicable" or "" => "-", var o => o,
            }),
            RowOf(L("سنوات الخبرة", "Years of Experience"), p => Analysis(p) is { } an ? Str(an, "years") : ""),
        };
        var criteria = people.SelectMany(p => Analysis(p) is { } an && an.TryGetProperty("criteria", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray().Select(x => Str(x, "criterion")) : []).Where(x => x.Length > 0).Distinct().Take(10).ToList();
        if (criteria.Count > 0) rows.Add(s.Divider());
        foreach (var criterion in criteria)
            rows.Add(RowOf(criterion, p => Analysis(p) is { } an && an.TryGetProperty("criteria", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().Where(x => Str(x, "criterion") == criterion).Select(x => Str(x, "score")).FirstOrDefault() ?? "" : ""));
        // Personality questionnaire results (sum out of 25 or sten out of 10, with the band), when any of them completed one.
        var profiles = people.Select(TraitProfile).ToList();
        var traitRows = profiles.SelectMany(x => x.Keys).Distinct().ToList();
        if (traitRows.Count > 0)
        {
            rows.Add(s.Divider());
            rows.Add(s.Text(L("ملف الشخصية", "Personality Profile"), "h5"));
            foreach (var code in traitRows)
            {
                var name = profiles.Select(x => x.TryGetValue(code, out var v) ? v.Name : null).First(n => n != null)!;
                rows.Add(s.Row([s.Text(name, "caption"), .. profiles.Select(x => s.Text(x.TryGetValue(code, out var v) ? v.Value : "-"))]));
            }
            // The 16-type letters and the fit with HR's role profiles, when worked out.
            var summaries = people.Select(Personality).ToList();
            if (summaries.Any(x => x.Type.Length > 0))
                rows.Add(s.Row([s.Text(L("نمط الشخصية", "Personality Type"), "caption"), .. summaries.Select(x => s.Text(x.Type.Length > 0 ? x.Type : "-"))]));
            foreach (var role in summaries.SelectMany(x => x.Fits.Keys).Distinct().Take(8))
                rows.Add(s.Row([s.Text(L($"الملاءمة: {role}", $"Fit: {role}"), "caption"), .. summaries.Select(x => s.Text(x.Fits.TryGetValue(role, out var f) ? f : "-"))]));
        }
        rows.Add(s.Row([.. people.Select(p => s.Button(L("فتح", "Open"), "open_applicant",
            new JsonObject { ["applicant_id"] = p.GetProperty("id").GetInt32() }, "borderless"))]));
        s.Card(s.Column(rows), id: "root");

        var summary = string.Join("; ", people.Select(p => $"{Str(p, "full_name")} (id {p.GetProperty("id").GetInt32()}): selection score {SelectionTotal(p)}, CV score {Score(p)}, status {Str(p, "status")}"));
        return Ui(s, $"Comparison shown. {summary}");
    }

    // ---------------- ask (A2UI forms) ----------------

    public sealed class AskOption
    {
        [Description("Value returned when chosen, for example an id")] public string Value { get; set; } = "";
        [Description("Text shown to the user")] public string Label { get; set; } = "";
    }

    public sealed class AskField
    {
        [Description("Key for the answer, for example start_date")] public string Name { get; set; } = "";
        [Description("Label shown to the user, in the user's language")] public string Label { get; set; } = "";
        [Description("text, longtext, number, date, time, choice, multichoice or checkbox")] public string Type { get; set; } = "text";
        [Description("Options for choice and multichoice")] public List<AskOption>? Options { get; set; }
        [Description("Pre-filled value, only when the user said it or the data makes it certain. Never pre-select a record (job, applicant, test) the user has not named: leave the choice empty so they pick it.")] public string? Value { get; set; }
        [Description("Whether an answer is needed")] public bool Required { get; set; }
    }

    [Description("Shows the user a short form when you need details from them (choices, dates, numbers, text). Pre-fill what you know and use real options from other tools. After calling it, stop and wait: the answers arrive as an A2UI action named submit_form.")]
    public JsonElement AskUser(
        [Description("Short question or title, in the user's language")] string title,
        [Description("The fields, at most 8")] List<AskField> fields)
    {
        if (fields.Count is 0 or > 8) return Error("give_1_to_8_fields");
        // Different forms may show together; the same form twice in one reply (same question and fields) is refused.
        var signature = $"ask:{title.Trim().ToLowerInvariant()}|{string.Join(",", fields.Select(f => f.Name.Trim().ToLowerInvariant()).Order())}";
        if (!api.FirstInRun(signature))
            return JsonSerializer.SerializeToElement(new { error = "form_already_shown", hint = "This form is already showing. Do not repeat it; wait for the user's answer." });
        // A choice with nothing to choose from is a dead end for the user: send the model back to fetch real options first.
        var empty = fields.Where(f => f.Type is "choice" or "multichoice" && (f.Options == null || !f.Options.Any(o => o.Label.Trim().Length > 0))).Select(f => f.Name).ToList();
        if (empty.Count > 0)
            return JsonSerializer.SerializeToElement(new
            {
                error = "choice_without_options", fields = empty,
                hint = "Each choice or multichoice field needs its options. Get them first (list_jobs for jobs, get_lookups for rounds, staff, tests and departments), or use a text field.",
            });
        var s = new Surface($"form-{Guid.NewGuid():N}"[..17]);
        var form = new JsonObject();
        var parts = new List<string> { s.Text(title, "h5") };
        var required = new List<string>();
        foreach (var f in fields)
        {
            var key = new string(f.Name.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
            if (key.Length == 0 || form.ContainsKey(key)) continue;
            parts.Add(Forms.Add(s, form, new Forms.Field(key, f.Label, f.Type, (f.Options ?? []).Select(o => (o.Value, o.Label)).ToList(), f.Required, f.Value), required));
        }
        var submit = s.Button(L("إرسال", "Submit"), "submit_form", new JsonObject { ["answers"] = new JsonObject { ["@path"] = "/form" } }, "primary");
        s.RequireFilled(submit, [.. required]);
        parts.Add(s.Row([submit], id: "actions"));
        s.Set("form", form).Set("state", "open");
        s.Card(s.Column(parts), id: "root");
        return Ui(s, "Form shown. Stop here and wait: the user's answers arrive as an A2UI action named submit_form.");
    }

    // ---------------- components designed by admins ----------------

    [Description("Forms that HR admins designed for you to show (key, name, when to use it, fields). Prefer one of these over ask_user when it fits.")]
    public async Task<JsonElement> ListComponents(CancellationToken ct = default)
    {
        JsonElement all;
        try { all = await api.GetAsync("agent/components", ct); }
        catch (BackofficeException e) { return Error(e.Code); }
        var list = all.GetProperty("items").EnumerateArray().Where(c => Str(c, "kind") == "Ask").Select(c => new
        {
            key = Str(c, "key"), name = Ar || Str(c, "name_en").Length == 0 ? Str(c, "name") : Str(c, "name_en"), purpose = Str(c, "purpose"),
            fields = Fields(c.GetProperty("definition")).Select(f => new { name = Str(f, "name"), input = Str(f, "input"), required = f.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True }),
        });
        return JsonSerializer.SerializeToElement(list);
    }

    [Description("HR's approved email wording (subject and body in Arabic and English, with {placeholders}). Check it before drafting an email to a candidate and follow the matching template, filling the placeholders yourself.")]
    public async Task<JsonElement> ListEmailTemplates(CancellationToken ct = default)
    {
        try { return (await api.GetAsync("email-wording", ct)).GetProperty("items"); }
        catch (BackofficeException e) { return Error(e.Code); }
    }

    [Description("Shows a form an admin designed (key from list_components), pre-filled with values you know. After calling it, stop and wait: the answers arrive as an A2UI action named submit_form.")]
    public async Task<JsonElement> ShowComponent(
        [Description("Component key")] string key,
        [Description("Pre-filled values by field name; multiple choices comma separated")] Dictionary<string, string>? values = null,
        CancellationToken ct = default)
    {
        JsonElement component;
        try { component = (await FindComponentAsync(c => Str(c, "key") == key && Str(c, "kind") == "Ask", ct)) ?? default; }
        catch (BackofficeException e) { return Error(e.Code); }
        if (component.ValueKind != JsonValueKind.Object) return Error("component_not_found");
        if (!api.FirstInRun($"component:{key}"))
            return JsonSerializer.SerializeToElement(new { error = "form_already_shown", hint = "This form is already showing. Do not repeat it; wait for the user's answer." });
        var definition = component.GetProperty("definition");
        var lookups = await LookupsIfNeededAsync(definition, ct);
        var s = new Surface($"form-{key}-{Guid.NewGuid():N}"[..Math.Min(60, 14 + key.Length)]);
        var form = new JsonObject();
        var required = new List<string>();
        var title = Forms.Localized(definition, "title", api.Lang);
        var parts = new List<string>();
        if (title.Length > 0) parts.Add(s.Text(title, "h5"));
        parts.AddRange(Forms.Component(s, definition, api.Lang, name => Source(name, lookups), (values ?? new Dictionary<string, string>()).ToDictionary(x => x.Key, x => (string?)x.Value), form, required));
        var submitLabel = Forms.Localized(definition, "submit", api.Lang);
        var submit = s.Button(submitLabel.Length > 0 ? submitLabel : L("إرسال", "Submit"), "submit_form",
            new JsonObject { ["component"] = key, ["answers"] = new JsonObject { ["@path"] = "/form" } }, "primary");
        s.RequireFilled(submit, [.. required]);
        parts.Add(s.Row([submit], id: "actions"));
        s.Set("form", form).Set("state", "open");
        s.Card(s.Column(parts), id: "root");
        return Ui(s, $"Form {key} shown. Stop here and wait: the user's answers arrive as an A2UI action named submit_form.");
    }

    async Task<JsonElement?> FindComponentAsync(Func<JsonElement, bool> match, CancellationToken ct)
    {
        var all = await api.GetAsync("agent/components", ct);
        foreach (var c in all.GetProperty("items").EnumerateArray()) if (match(c)) return c;
        return null;
    }

    static IEnumerable<JsonElement> Fields(JsonElement definition) =>
        definition.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array
            ? nodes.EnumerateArray().SelectMany<JsonElement, JsonElement>(n => Str(n, "type") == "row" && n.TryGetProperty("children", out var k) ? k.EnumerateArray() : new[] { n }).Where(n => Str(n, "type") == "field")
            : [];

    async Task<JsonElement> LookupsIfNeededAsync(JsonElement definition, CancellationToken ct) =>
        Fields(definition).Any(f => Str(f, "source") is "rounds" or "staff" or "assessments" or "departments") ? await api.GetAsync("lookups", ct) : default;

    IReadOnlyList<(string, string)> Source(string name, JsonElement lookups) => name switch
    {
        "rounds" => Options(lookups, "rounds", "name").ToList(),
        "staff" => Options(lookups, "staff", "name").ToList(),
        "assessments" => Options(lookups, "assessments", "title").ToList(),
        "departments" => Options(lookups, "departments", Ar ? "name" : "name_en").Select(x => (x.Item1, x.Item2.Length > 0 ? x.Item2 : x.Item1)).ToList(),
        "statuses" => Statuses().ToList(),
        "employment_types" => EmploymentTypes().ToList(),
        _ => [],
    };

    // ---------------- propose (editable approval cards) ----------------

    [Description("Proposes moving an applicant to another status. Shows an approval card; nothing changes until the user approves.")]
    public Task<JsonElement> ProposeStatusChange(int applicantId, [Description("Open, Replied, Hold, Accepted or Rejected")] string status, CancellationToken ct = default) =>
        Propose("set_applicant_status", new JsonObject { ["applicant_id"] = applicantId, ["status"] = status }, ct, (s, _) =>
            [s.ChoicePicker(L("الحالة", "Status"), "/form/status", Statuses(), required: true)], "status");

    [Description("Proposes adding an internal note to an applicant. Shows an approval card the user can edit.")]
    public Task<JsonElement> ProposeNote(int applicantId, string text, CancellationToken ct = default) =>
        Propose("add_note", new JsonObject { ["applicant_id"] = applicantId, ["text"] = text }, ct, (s, _) =>
            [s.TextField(L("الملاحظة", "Note"), "/form/text", "longText", true)], "text");

    [Description("Proposes an interview. Get round and staff ids from get_lookups. date YYYY-MM-DD, times HH:MM in UAE time. Shows an approval card the user can edit. If the date, time, round or panel is not clear, ask the user first (ask_user) instead of guessing.")]
    public Task<JsonElement> ProposeInterview(int applicantId, int roundId, string date, string from, string to,
        [Description("Room or meeting link")] string? location = null, [Description("Staff ids on the panel")] int[]? panelIds = null,
        string? notes = null, CancellationToken ct = default) =>
        Propose("schedule_interview", new JsonObject
        {
            ["applicant_id"] = applicantId, ["round_id"] = roundId.ToString(CultureInfo.InvariantCulture), ["date"] = date, ["from"] = from, ["to"] = to,
            ["location"] = location ?? "", ["notes"] = notes ?? "",
            ["panel_ids"] = new JsonArray([.. (panelIds ?? []).Select(x => (JsonNode)x.ToString(CultureInfo.InvariantCulture))]),
        }, ct, (s, lookups) =>
        [
            s.ChoicePicker(L("مرحلة المقابلة", "Interview Round"), "/form/round_id", Options(lookups, "rounds", "name"), required: true),
            s.DateTimeInput(L("التاريخ", "Date"), "/form/date", required: true),
            s.Row([s.DateTimeInput(L("من", "From"), "/form/from", false, true, true), s.DateTimeInput(L("إلى", "To"), "/form/to", false, true, true)]),
            s.TextField(L("المكان أو رابط الاجتماع", "Location or Meeting Link"), "/form/location"),
            s.ChoicePicker(L("لجنة المقابلة", "Interview Panel"), "/form/panel_ids", Options(lookups, "staff", "name"), multiple: true),
            s.TextField(L("ملاحظات", "Notes"), "/form/notes", "longText"),
        ], "round_id", "date", "from", "to");

    [Description("Proposes inviting an applicant to a published test (ids from get_lookups). Shows an approval card.")]
    public Task<JsonElement> ProposeTestInvitation(int applicantId, int testId, [Description("Days to complete it")] int? deadlineDays = null, CancellationToken ct = default) =>
        Propose("invite_to_test", new JsonObject
        {
            ["applicant_id"] = applicantId, ["assessment_id"] = testId.ToString(CultureInfo.InvariantCulture),
            ["deadline_days"] = deadlineDays?.ToString(CultureInfo.InvariantCulture) ?? "",
        }, ct, (s, lookups) =>
        [
            s.ChoicePicker(L("الاختبار", "Test"), "/form/assessment_id", Options(lookups, "assessments", "title"), required: true),
            s.TextField(L("عدد أيام الإنجاز", "Days to Complete"), "/form/deadline_days", "number"),
        ], "assessment_id");

    [Description("Proposes a draft job offer (not sent to the candidate). Dates YYYY-MM-DD. Shows an approval card the user can edit.")]
    public Task<JsonElement> ProposeOffer(int applicantId, string positionTitle, string offerDate, string? startDate = null,
        decimal? monthlySalary = null, [Description("Currency code, default AED")] string? currency = null, string? terms = null, CancellationToken ct = default) =>
        Propose("make_offer", new JsonObject
        {
            ["applicant_id"] = applicantId, ["position_title"] = positionTitle, ["offer_date"] = offerDate, ["start_date"] = startDate ?? "",
            ["monthly_salary"] = monthlySalary?.ToString(CultureInfo.InvariantCulture) ?? "", ["currency"] = currency ?? "AED", ["terms"] = terms ?? "",
        }, ct, (s, _) =>
        [
            s.TextField(L("المسمى الوظيفي", "Position Title"), "/form/position_title", required: true),
            s.Row([s.DateTimeInput(L("تاريخ العرض", "Offer Date"), "/form/offer_date", required: true), s.DateTimeInput(L("تاريخ المباشرة", "Start Date"), "/form/start_date")]),
            s.Row([s.TextField(L("الراتب الشهري", "Monthly Salary"), "/form/monthly_salary", "number"), s.TextField(L("العملة", "Currency"), "/form/currency")]),
            s.TextField(L("الشروط", "Terms"), "/form/terms", "longText"),
        ], "position_title", "offer_date");

    [Description("Proposes an email to an applicant, in the applicant's language, written as the hiring team. Plain text body. Shows an approval card where the user can edit the subject and text.")]
    public Task<JsonElement> ProposeEmail(int applicantId, string subject, string body, CancellationToken ct = default) =>
        Propose("email_candidate", new JsonObject { ["applicant_id"] = applicantId, ["subject"] = subject, ["body"] = body }, ct, (s, _) =>
        [
            s.TextField(L("الموضوع", "Subject"), "/form/subject", required: true),
            s.TextField(L("نص الرسالة", "Message"), "/form/body", "longText", true),
        ], "subject", "body");

    [Description("Proposes a new unpublished draft job. description is simple HTML (p, ul, li, strong). Shows an approval card the user can edit.")]
    public Task<JsonElement> ProposeDraftJob(string title, string description, string location,
        [Description("FullTime, PartTime, Contract, Temporary or Internship")] string employmentType = "FullTime",
        [Description("Department id from get_lookups")] int? departmentId = null, CancellationToken ct = default) =>
        Propose("create_draft_job", new JsonObject
        {
            ["title"] = title, ["description"] = description, ["location"] = location, ["employment_type"] = employmentType,
            ["department_id"] = departmentId?.ToString(CultureInfo.InvariantCulture) ?? "",
        }, ct, (s, lookups) =>
        [
            s.TextField(L("المسمى الوظيفي", "Job Title"), "/form/title", required: true),
            s.Row([s.TextField(L("الموقع", "Location"), "/form/location"),
                s.ChoicePicker(L("نوع التوظيف", "Employment Type"), "/form/employment_type", EmploymentTypes())]),
            s.ChoicePicker(L("الإدارة", "Department"), "/form/department_id", Options(lookups, "departments", "name")),
        ], "title");

    [Description("Proposes asking for an English version of Arabic content for HR to review. kind: job, screening, question or assessment.")]
    public Task<JsonElement> ProposeEnglishSuggestion(string kind, int id, CancellationToken ct = default) =>
        Propose("suggest_english", new JsonObject { ["kind"] = kind, ["id"] = id }, ct, null);

    /// <summary>
    /// Records the proposal in the backend and returns its approval card. When <paramref name="form"/> is given, the card
    /// is an editable form pre-filled with the proposal; Approve sends the edits, which the backend checks again.
    /// </summary>
    async Task<JsonElement> Propose(string action, JsonObject arguments, CancellationToken ct,
        Func<Surface, JsonElement, IEnumerable<string>>? form, params string[] required)
    {
        JsonElement p, lookups = default;
        JsonElement? designed;
        try
        {
            p = await api.PostAsync("agent/proposals", new { action, arguments, lang = api.Lang }, ct);
            designed = await FindComponentAsync(c => Str(c, "kind") == "Approval" && Str(c, "action_name") == action, ct);
            if (designed is { } d) lookups = await LookupsIfNeededAsync(d.GetProperty("definition"), ct);
            else if (form != null && action is "schedule_interview" or "invite_to_test" or "create_draft_job") lookups = await api.GetAsync("lookups", ct);
        }
        catch (BackofficeException e) { return Error(e.Code); }
        var id = Str(p, "id");
        var s = new Surface($"proposal-{id}");
        var values = (JsonObject)arguments.DeepClone();
        values.Remove("applicant_id");
        if (action == "create_draft_job") values.Remove("description");
        s.Set("proposal_id", id).Set("state", "pending").Set("form", values);
        var parts = new List<string> { s.Text(L("بانتظار موافقتك", "Needs Your Approval"), "h5"), s.Text(Str(p, "label")) };
        if (designed is { } component)
        {
            // An admin designed this card in the backoffice: draw their fields, pre-filled with the proposal.
            var definition = component.GetProperty("definition");
            var start = values.ToDictionary(x => x.Key, x => x.Value switch
            {
                JsonArray arr => (string?)string.Join(",", arr.Select(v => v?.ToString())),
                null => null,
                var v => v.ToString(),
            });
            var editable = new JsonObject();
            var needed = new List<string>();
            parts.AddRange(Forms.Component(s, definition, api.Lang, name => Source(name, lookups), start, editable, needed));
            s.Set("form", editable);
            required = [.. needed.Select(r => r["/form/".Length..])];
        }
        else if (form != null) parts.AddRange(form(s, lookups));
        var approve = s.Button(L("موافقة", "Approve"), "approve_proposal", new JsonObject
        {
            ["proposal_id"] = new JsonObject { ["@path"] = "/proposal_id" },
            ["changes"] = new JsonObject { ["@path"] = "/form" },
        }, "primary");
        s.RequireFilled(approve, [.. required.Select(r => $"/form/{r}")]);
        var cancel = s.Button(L("إلغاء", "Cancel"), "cancel_proposal", new JsonObject { ["proposal_id"] = new JsonObject { ["@path"] = "/proposal_id" } }, "borderless");
        parts.Add(s.Row([approve, cancel], id: "actions"));
        s.Card(s.Column(parts), id: "root");
        return Ui(s, $"Proposal {id} shown to the user as an editable card with Approve and Cancel. NOT applied yet: {Str(p, "label")}");
    }

    IEnumerable<(string, string)> Statuses() =>
    [
        ("Open", L("جديد", "New")), ("Replied", L("تم التواصل", "Contacted")), ("Hold", L("معلّق", "On Hold")),
        ("Accepted", L("مقبول", "Accepted")), ("Rejected", L("لم يتم الاختيار", "Not Selected")),
    ];

    IEnumerable<(string, string)> EmploymentTypes() =>
    [
        ("FullTime", L("دوام كامل", "Full Time")), ("PartTime", L("دوام جزئي", "Part Time")), ("Contract", L("عقد", "Contract")),
        ("Temporary", L("مؤقت", "Temporary")), ("Internship", L("تدريب", "Internship")),
    ];

    static IEnumerable<(string, string)> Options(JsonElement lookups, string list, string label) =>
        lookups.ValueKind == JsonValueKind.Object && lookups.TryGetProperty(list, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(x => (Str(x, "id"), Str(x, label))).ToList()
            : [];

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

    /// <summary>
    /// The latest personality questionnaire profile of an applicant: trait code to (name, value shown). The value is the sum
    /// out of 25 (workplace questionnaires) or the sten out of 10, with the band.
    /// </summary>
    Dictionary<string, (string Name, string Value)> TraitProfile(JsonElement p)
    {
        var result = new Dictionary<string, (string, string)>();
        if (!p.TryGetProperty("invitations", out var invs) || invs.ValueKind != JsonValueKind.Array) return result;
        foreach (var inv in invs.EnumerateArray())
        {
            if (Str(inv, "personality_model") is "" or "None" || !inv.TryGetProperty("attempts", out var atts)) continue;
            var json = atts.EnumerateArray().Select(a => Str(a, "trait_profile")).LastOrDefault(x => x.Length > 0);
            if (json == null) continue;
            using var doc = JsonDocument.Parse(json);
            foreach (var t in doc.RootElement.EnumerateArray())
            {
                var band = BandText(Str(t, "Band"));
                var value = t.TryGetProperty("Sum", out var sum) && sum.ValueKind == JsonValueKind.Number
                    ? $"{sum.GetInt32()}/{t.GetProperty("MaxSum").GetInt32()}" : $"{t.GetProperty("Sten").GetInt32()}/10";
                result[Str(t, "Code")] = (Ar ? Str(t, "NameAr") : Str(t, "NameEn"), band.Length > 0 ? $"{value} · {band}" : value);
            }
            return result;
        }
        return result;
    }

    string BandText(string band) => band switch
    {
        "VeryHigh" => L("مرتفع جدًا", "Very High"), "High" => L("مرتفع", "High"), "Moderate" => L("متوسط", "Moderate"),
        "Average" => L("متوسط", "Average"), "Low" => L("منخفض", "Low"), "VeryLow" => L("منخفض جدًا", "Very Low"), _ => "",
    };

    /// <summary>The 16-type letters and role fits (name to "84% · Strong Fit") of the applicant's latest personality questionnaire.</summary>
    (string Type, Dictionary<string, string> Fits) Personality(JsonElement p)
    {
        var fits = new Dictionary<string, string>();
        if (!p.TryGetProperty("personality", out var all) || all.ValueKind != JsonValueKind.Object) return ("", fits);
        var type = "";
        foreach (var inv in all.EnumerateObject())
        {
            if (inv.Value.ValueKind != JsonValueKind.Object) continue;
            if (type.Length == 0) type = Str(inv.Value, "type");
            if (!inv.Value.TryGetProperty("fits", out var list)) continue;
            foreach (var f in list.EnumerateArray())
            {
                var level = Str(f, "level") switch { "Strong" => L("ملاءمة عالية", "Strong Fit"), "Moderate" => L("ملاءمة متوسطة", "Moderate Fit"), _ => L("ملاءمة ضعيفة", "Weak Fit") };
                fits.TryAdd(Ar ? Str(f, "name_ar") : Str(f, "name_en"), $"{f.GetProperty("percent").GetDouble():0.#}% · {level}");
            }
        }
        return (type, fits);
    }

    static JsonElement? Analysis(JsonElement p) => p.TryGetProperty("analysis", out var a) && a.ValueKind == JsonValueKind.Object ? a : null;

    /// <summary>The combined selection score, marked partial while parts are still missing.</summary>
    string SelectionTotal(JsonElement p) =>
        p.TryGetProperty("selection", out var s) && s.ValueKind == JsonValueKind.Object && s.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number
            ? $"{t.GetDecimal():0.#}" + (s.TryGetProperty("complete", out var c) && c.ValueKind == JsonValueKind.False ? L(" (جزئية)", " (partial)") : "")
            : "-";

    static string Score(JsonElement p) => Analysis(p) is { } a && a.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? $"{s.GetDecimal():0.#}" : "-";

    static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString() ?? "", JsonValueKind.Null or JsonValueKind.Undefined => "", _ => v.GetRawText() }
            : "";

    static string Clip(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
