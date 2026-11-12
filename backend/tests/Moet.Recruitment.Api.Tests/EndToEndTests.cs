using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Moet.Recruitment.Api.Tests;

/// <summary>
/// Runs the real API against SQL Server: schema scripts, staff sign-in, a job created in the backoffice API, and an
/// applicant signing up and applying through the portal API. Set TEST_DB_CONNECTION to run (CI does).
/// </summary>
public class EndToEndTests
{
    static readonly string? Db = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION");
    const string Key = "test-portal-key";
    const string Secret = "test-portal-secret-0123456789";
    const string AgentKey = "test-agent-key-0123456789";

    sealed class App : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseSetting("DB_CONNECTION", Db);
            b.UseSetting("ADMIN_EMAIL", "admin@moet.test");
            b.UseSetting("ADMIN_PASSWORD", "Admin-Password-123");
            b.UseSetting("PORTAL_API_KEY", Key);
            b.UseSetting("PORTAL_API_SECRET", Secret);
            b.UseSetting("COOKIE_SECURE", "0");
            b.UseSetting("RUN_WORKERS", "0");
            b.UseSetting("PORTAL_URL", "https://careers.example");
            b.UseSetting("AGENT_KEY", AgentKey);
        }
    }

    static StringContent JsonBody(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    static async Task<JsonElement> Read(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {r.RequestMessage?.RequestUri}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public async Task Staff_create_a_job_and_an_applicant_applies()
    {
        if (string.IsNullOrEmpty(Db)) return;
        using var app = new App();
        var admin = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        admin.DefaultRequestHeaders.Add("X-Backoffice", "1");

        // Sign-in is required, and refuses a wrong password.
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/backoffice/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.PostAsync("/api/backoffice/sign-in", JsonBody(new { email = "admin@moet.test", password = "wrong" }))).StatusCode);
        var me = await Read(await admin.PostAsync("/api/backoffice/sign-in", JsonBody(new { email = "admin@moet.test", password = "Admin-Password-123" })));
        Assert.Equal("Administrator", me.GetProperty("role").GetString());

        // A screening question and a published job with an approved English version.
        var sq = await Read(await admin.PostAsync("/api/backoffice/screening-questions", JsonBody(new
        {
            text = "هل لديك رخصة قيادة إماراتية؟", text_en = "Do you have a UAE driving licence?", english_approved = true,
            answer_type = "YesNo", category = "", is_active = true, options = Array.Empty<object>(),
        })));
        var job = await Read(await admin.PostAsync("/api/backoffice/jobs", JsonBody(new
        {
            title = "محلل بيانات", description = "<p>تحليل البيانات الاقتصادية</p>", department_id = (int?)null, location = "دبي",
            employment_type = "FullTime", status = "Open", is_published = true, posted_on = (string?)null, closes_on = (string?)null,
            salary_min = (decimal?)null, salary_max = (decimal?)null, salary_currency = "AED", salary_period = "Month", show_salary = false,
            translations = new[] { new { lang = "en", title = "Data Analyst", description = "<p>Analyse economic data</p>", approved = true } },
            screening = new[] { new { question_id = sq.GetProperty("id").GetInt32(), required = true, rule = "MustBe", expected = "Yes" } },
            stages = Array.Empty<object>(), row_version = (string?)null,
        })));
        var code = job.GetProperty("code").GetString()!;
        Assert.StartsWith("JOB-", code);

        // The portal API needs the service key.
        var portal = app.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await portal.GetAsync("/api/portal/config")).StatusCode);
        portal.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", $"{Key}:{Secret}");

        var config = await Read(await portal.GetAsync("/api/portal/config?lang=ar"));
        Assert.Equal("ar", config.GetProperty("message").GetProperty("default_language").GetString());
        Assert.Equal("rtl", config.GetProperty("message").GetProperty("dir").GetString());

        var en = await Read(await portal.GetAsync($"/api/portal/job?lang=en&name={code}"));
        Assert.Equal("Data Analyst", en.GetProperty("message").GetProperty("title").GetString());
        Assert.Equal("Yes or No", en.GetProperty("message").GetProperty("screening")[0].GetProperty("type").GetString());
        var ar = await Read(await portal.GetAsync($"/api/portal/jobs?lang=ar"));
        Assert.Equal("محلل بيانات", ar.GetProperty("message").GetProperty("jobs")[0].GetProperty("title").GetString());

        // Sign up (no email server, so no verification step) and apply.
        var signup = await Read(await portal.PostAsync("/api/portal/signup", JsonBody(new { full_name = "Layla Test", email = "layla@example.test", password = "a-good-pass-123", lang = "ar" })));
        var epoch = signup.GetProperty("message").GetProperty("epoch").GetInt32().ToString();
        var bad = await portal.PostAsync("/api/portal/authenticate", JsonBody(new { email = "layla@example.test", password = "nope" }));
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.Contains("bad_credentials", await bad.Content.ReadAsStringAsync());

        using var form = new MultipartFormDataContent
        {
            { new StringContent("layla@example.test"), "email" }, { new StringContent(epoch), "epoch" }, { new StringContent(code), "job" },
            { new StringContent("+971501234567"), "phone" }, { new StringContent("United Arab Emirates"), "country" },
            { new StringContent("1"), "declaration" }, { new StringContent("ar"), "lang" }, { new StringContent("{\"sq1\":\"No\"}"), "screening" },
        };
        var pdf = new ByteArrayContent("%PDF-1.4\n%test\n"u8.ToArray());
        pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(pdf, "cv", "cv.pdf");
        var applied = await Read(await portal.PostAsync("/api/portal/apply", form));
        Assert.StartsWith("APP-", applied.GetProperty("message").GetProperty("application").GetString());

        var again = await portal.PostAsync("/api/portal/apply", form);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var mine = await Read(await portal.GetAsync($"/api/portal/applications?lang=ar&email=layla@example.test&epoch={epoch}"));
        Assert.Equal("under_review", mine.GetProperty("message").GetProperty("applications")[0].GetProperty("status").GetProperty("code").GetString());

        // HR sees the applicant, flagged by the screening requirement.
        var list = await Read(await admin.GetAsync("/api/backoffice/applicants"));
        Assert.Equal("DoesNotMeet", list.GetProperty("items")[0].GetProperty("screening").GetString());
        Assert.Equal(1, list.GetProperty("total").GetInt32());

        // Every list pages, sorts (only by allowed columns) and filters.
        var jobs = await Read(await admin.GetAsync("/api/backoffice/jobs?sort=title&dir=asc&size=10&status=Open&q=%D9%85%D8%AD%D9%84%D9%84"));
        Assert.Equal(10, jobs.GetProperty("size").GetInt32());
        Assert.Equal("title", jobs.GetProperty("sort").GetString());
        Assert.Equal("asc", jobs.GetProperty("dir").GetString());
        Assert.Equal(1, jobs.GetProperty("items")[0].GetProperty("applicants").GetInt32());
        var odd = await Read(await admin.GetAsync("/api/backoffice/applicants?sort=PasswordHash&size=7&status=Rejected"));
        Assert.Equal("created", odd.GetProperty("sort").GetString());
        Assert.Equal(25, odd.GetProperty("size").GetInt32());
        Assert.Equal(0, odd.GetProperty("total").GetInt32());

        // The agent can only propose; the backend describes the change and carries it out once, on approval.
        var applicantId = list.GetProperty("items")[0].GetProperty("id").GetInt32();
        var proposal = await Read(await admin.PostAsync("/api/backoffice/agent/proposals",
            JsonBody(new { action = "add_note", arguments = new { applicant_id = applicantId, text = "Strong analytics background" }, thread_id = "t1", lang = "en" })));
        Assert.StartsWith("Add a note to", proposal.GetProperty("label").GetString());
        var pid = proposal.GetProperty("id").GetString();
        // The staff member edits the note on the card; who it is about cannot change.
        var approved = await Read(await admin.PostAsync($"/api/backoffice/agent/proposals/{pid}/approve?lang=en",
            JsonBody(new { changes = new { text = "Edited: strong analytics background", applicant_id = 999 } })));
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/backoffice/agent/proposals/{pid}/approve", JsonBody(new { }))).StatusCode);
        var detail = await Read(await admin.GetAsync($"/api/backoffice/applicants/{applicantId}"));
        Assert.Contains(detail.GetProperty("notes").EnumerateArray(), n => n.GetProperty("text").GetString() == "Edited: strong analytics background");
        var unknown = await admin.PostAsync("/api/backoffice/agent/proposals", JsonBody(new { action = "delete_everything", arguments = new { } }));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync("/api/backoffice/agent/run", JsonBody(new { }))).StatusCode);

        // Admins design the agent's components; each approval card may only edit what its action allows.
        var card = new
        {
            key = "note_card", name = "بطاقة الملاحظة", name_en = "Note Card", purpose = "Notes", kind = "Approval", action_name = "add_note", is_active = true,
            definition = new { nodes = new object[] { new { type = "field", name = "text", input = "longtext", label = new { ar = "الملاحظة", en = "Note" }, required = true } } },
        };
        var created = await Read(await admin.PostAsync("/api/backoffice/components", JsonBody(card)));
        var second = await admin.PostAsync("/api/backoffice/components", JsonBody(card with { }));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var badCard = await admin.PostAsync("/api/backoffice/components", JsonBody(new
        {
            key = "bad_card", name = "x", kind = "Approval", action_name = "add_note", is_active = true,
            definition = new { nodes = new object[] { new { type = "field", name = "applicant_id", input = "number" } } },
        }));
        Assert.Equal(HttpStatusCode.BadRequest, badCard.StatusCode);
        var comps = await Read(await admin.GetAsync("/api/backoffice/components?kind=Approval&sort=name"));
        Assert.Equal(1, comps.GetProperty("total").GetInt32());
        var forAgent = await Read(await admin.GetAsync("/api/backoffice/agent/components"));
        Assert.Equal("note_card", forAgent.GetProperty("items")[0].GetProperty("key").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/backoffice/components/{created.GetProperty("id").GetInt32()}")).StatusCode);

        // Assistant settings: the model key is stored encrypted, never shown, and only the agent service can read it.
        var settings = await Read(await admin.GetAsync("/api/backoffice/agent-settings"));
        Assert.True(settings.GetProperty("is_enabled").GetBoolean());
        Assert.Equal(23, settings.GetProperty("tools").GetArrayLength());
        var put = new
        {
            is_enabled = true, model_base_url = "http://llm.internal/v1", model_name = "qwen3", api_key = "sk-secret-123", clear_api_key = false,
            temperature = 0.2, max_steps = 6, extra_instructions = "Prefer Arabic.", disabled_tools = new[] { "propose_email", "not_a_tool" },
            proposal_minutes = 60, daily_run_limit = 50, history_turns = 20, examples_ar = "من أفضل المتقدمين؟", examples_en = "Who are the best applicants?",
        };
        await Read(await admin.PutAsync("/api/backoffice/agent-settings", JsonBody(put)));
        var saved = await admin.GetAsync("/api/backoffice/agent-settings");
        var savedText = await saved.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sk-secret-123", savedText);
        var savedJson = JsonDocument.Parse(savedText).RootElement;
        Assert.True(savedJson.GetProperty("has_api_key").GetBoolean());
        Assert.Equal("propose_email", Assert.Single(savedJson.GetProperty("disabled_tools").EnumerateArray()).GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync("/api/backoffice/agent-settings", JsonBody(put with { max_steps = 99 }))).StatusCode);

        var anon = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/internal/agent/settings")).StatusCode);
        using (var internalReq = new HttpRequestMessage(HttpMethod.Get, "/api/internal/agent/settings"))
        {
            internalReq.Headers.Add("X-Agent-Key", AgentKey);
            var forAgent2 = await Read(await anon.SendAsync(internalReq));
            Assert.Equal("sk-secret-123", forAgent2.GetProperty("model_api_key").GetString());
            Assert.Equal(6, forAgent2.GetProperty("max_steps").GetInt32());
        }
        var info = await Read(await admin.GetAsync("/api/backoffice/agent/info"));
        Assert.Equal(20, info.GetProperty("history_turns").GetInt32());

        // Assessments load with their sections (computed orderings are not mapped as navigations).
        Assert.Equal(0, (await Read(await admin.GetAsync("/api/backoffice/assessments"))).GetProperty("total").GetInt32());

        // The public-domain IPIP Big Five questionnaire can be added in one step: 50 statements and a draft questionnaire.
        var templates = await Read(await admin.GetAsync("/api/backoffice/assessment-templates"));
        Assert.Equal("ipip-big-five-50", templates[0].GetProperty("key").GetString());
        var bigFive = await Read(await admin.PostAsync("/api/backoffice/assessment-templates/ipip-big-five-50", JsonBody(new { })));
        Assert.Equal(50, bigFive.GetProperty("questions").GetInt32());
        var questionnaire = await Read(await admin.GetAsync($"/api/backoffice/assessments/{bigFive.GetProperty("id").GetInt32()}"));
        Assert.Equal("BigFive", questionnaire.GetProperty("personality_model").GetString());
        Assert.Equal(50, questionnaire.GetProperty("question_count").GetInt32());
        Assert.Equal(5, questionnaire.GetProperty("traits").GetArrayLength());

        // Changes without the X-Admin header are refused.
        var plain = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostAsync("/api/backoffice/sign-in", JsonBody(new { email = "x", password = "y" }))).StatusCode);
    }

    [Fact]
    public async Task A_staff_session_survives_an_api_restart()
    {
        if (string.IsNullOrEmpty(Db)) return;
        string cookie;
        using (var first = new App())
        {
            var http = first.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            http.DefaultRequestHeaders.Add("X-Backoffice", "1");
            var res = await http.PostAsync("/api/backoffice/sign-in", JsonBody(new { email = "admin@moet.test", password = "Admin-Password-123" }));
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            cookie = res.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("moet_backoffice=")).Split(';')[0];
        }
        // A new instance (as after a redeploy) reads the key ring from SQL Server and accepts the same cookie.
        using var second = new App();
        var again = second.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/backoffice/me");
        req.Headers.Add("Cookie", cookie);
        var me = await again.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }
}
