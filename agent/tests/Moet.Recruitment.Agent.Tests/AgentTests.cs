using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moet.Recruitment.Agent.A2ui;
using Moet.Recruitment.Agent.Backoffice;

namespace Moet.Recruitment.Agent.Tests;

public class A2uiTests
{
    [Fact]
    public void Component_fields_become_bound_inputs_with_options_and_starting_values()
    {
        using var doc = JsonDocument.Parse("""
            {"nodes":[{"type":"row","children":[
              {"type":"field","name":"date","input":"date","label":{"ar":"التاريخ","en":"Date"},"required":true},
              {"type":"field","name":"round_id","input":"choice","source":"rounds","label":{"ar":"الجولة"}}]},
              {"type":"field","name":"panel","input":"multichoice","options":[{"value":"1","label":{"en":"Sara"}},{"value":"2","label":{"en":"Omar"}}]}]}
            """);
        var s = new Surface("f");
        var form = new JsonObject();
        var required = new List<string>();
        var ids = Forms.Component(s, doc.RootElement, "en", name => name == "rounds" ? new List<(string, string)> { ("5", "Technical") } : new List<(string, string)>(),
            new Dictionary<string, string?> { ["date"] = "2026-11-20", ["panel"] = "1,2" }, form, required);

        Assert.Equal(2, ids.Count);
        Assert.Equal("2026-11-20", form["date"]!.GetValue<string>());
        Assert.Equal(2, form["panel"]!.AsArray().Count);
        Assert.Equal(new List<string> { "/form/date" }, required);
        var components = s.Messages()[1]!["updateComponents"]!["components"]!.AsArray();
        var round = components.First(c => c!["component"]!.GetValue<string>() == "ChoicePicker" && c["label"]!.GetValue<string>() == "الجولة")!;
        Assert.Equal("Technical", round["options"]![0]!["label"]!.GetValue<string>());
        Assert.Equal("/form/round_id", round["value"]!["@path"]!.GetValue<string>());
    }

    [Fact]
    public void Surface_is_created_with_its_data_then_drawn_from_root()
    {
        var s = new Surface("proposal-1").Set("proposal_id", "abc");
        var title = s.Text("Needs Your Approval", "h5");
        var approve = s.Button("Approve", "approve_proposal", new JsonObject { ["proposal_id"] = new JsonObject { ["@path"] = "/proposal_id" } });
        s.Card(s.Column([title, approve]), id: "root");

        var m = s.Messages();
        Assert.Equal("1.0", m[0]!["version"]!.GetValue<string>());
        Assert.Equal("abc", m[0]!["createSurface"]!["dataModel"]!["proposal_id"]!.GetValue<string>());
        var components = m[1]!["updateComponents"]!["components"]!.AsArray();
        Assert.Contains(components, c => c!["id"]!.GetValue<string>() == "root" && c["component"]!.GetValue<string>() == "Card");
        var button = components.First(c => c!["component"]!.GetValue<string>() == "Button")!;
        Assert.Equal("approve_proposal", button["action"]!["event"]!["name"]!.GetValue<string>());
        // Every child reference points at a component that exists.
        var ids = components.Select(c => c!["id"]!.GetValue<string>()).ToHashSet();
        foreach (var c in components)
        {
            if (c!["child"] is { } child) Assert.Contains(child.GetValue<string>(), ids);
            if (c["children"] is JsonArray kids) Assert.All(kids, k => Assert.Contains(k!.GetValue<string>(), ids));
        }
    }
}

/// <summary>Runs the real AG-UI endpoint with a scripted model and a fake backoffice API.</summary>
public class AgUiEndToEndTests
{
    const string Key = "test-agent-key-0123456789";

    /// <summary>First asks for propose_note, then answers in text once the tool result is back.</summary>
    sealed class ScriptedModel : IChatClient
    {
        public List<string> SeenTools { get; } = [];
        public List<string> SeenSystem { get; } = [];
        void See(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            lock (SeenTools)
            {
                SeenTools.AddRange((options?.Tools ?? []).Select(t => t.Name));
                SeenSystem.AddRange(messages.Where(m => m.Role == ChatRole.System).Select(m => m.Text));
            }
        }

        static bool HasToolResult(IEnumerable<ChatMessage> m) => m.Any(x => x.Contents.OfType<FunctionResultContent>().Any());

        static ChatResponseUpdate Next(IEnumerable<ChatMessage> messages) => HasToolResult(messages)
            ? new ChatResponseUpdate(ChatRole.Assistant, "The note is waiting for your approval.") { MessageId = "m2" }
            : new ChatResponseUpdate(ChatRole.Assistant,
                [new FunctionCallContent("call_1", "propose_note", new Dictionary<string, object?> { ["applicantId"] = 7, ["text"] = "Strong candidate" })]) { MessageId = "m1" };

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            See(messages, options);
            return Task.FromResult(new[] { Next(messages) }.ToChatResponse());
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            See(messages, options);
            await Task.Yield();
            yield return Next(messages);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    sealed class FakeBackoffice(string components = """{"items":[]}""", string? settings = null) : HttpMessageHandler
    {
        public List<(string Path, string? Cookie, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.RequestUri!.AbsolutePath, request.Headers.TryGetValues("Cookie", out var c) ? c.First() : null, body));
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/internal/agent/settings")
            {
                Assert.Equal(Key, request.Headers.GetValues("X-Agent-Key").First());
                return settings == null ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(settings, Encoding.UTF8, "application/json") };
            }
            Assert.Equal("1", request.Headers.GetValues("X-Backoffice").First());
            var json = path.EndsWith("/agent/proposals")
                ? """{"id":"9a6c1d1e-0000-4000-8000-000000000001","action":"add_note","label":"Add a note to Layla (APP-2026-00001): Strong candidate","expires_at":"2026-11-12T10:30:00Z"}"""
                : path.EndsWith("/agent/components") ? components
                : """{"error":{"code":"not_found"}}""";
            return new HttpResponseMessage(json.Contains("error") ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    sealed class App(FakeBackoffice backoffice, ScriptedModel? model = null) : WebApplicationFactory<Program>
    {
        public ScriptedModel Model { get; } = model ?? new ScriptedModel();
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseSetting("AGENT_KEY", Key);
            b.UseSetting("BACKEND_URL", "http://backend.test");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IChatClient>(Model);
                s.AddHttpClient(BackofficeApi.Client).ConfigurePrimaryHttpMessageHandler(() => backoffice);
            });
        }
    }

    static StringContent Run(string text) => new(JsonSerializer.Serialize(new
    {
        threadId = "thread-1", runId = "run-1", state = new { }, tools = Array.Empty<object>(), context = Array.Empty<object>(), forwardedProps = new { },
        messages = new[] { new { id = "u1", role = "user", content = text } },
    }), Encoding.UTF8, "application/json");

    [Fact]
    public async Task A_run_streams_tool_calls_an_approval_surface_and_text()
    {
        var backoffice = new FakeBackoffice();
        using var app = new App(backoffice);
        var http = app.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/agent") { Content = Run("Add a note to applicant 7: strong candidate") };
        req.Headers.Add("X-Agent-Key", Key);
        req.Headers.Add("Cookie", "moet_backoffice=session-cookie");
        req.Headers.Add("X-Lang", "en");
        req.Headers.Accept.ParseAdd("text/event-stream");
        var res = await http.SendAsync(req);
        var sse = await res.Content.ReadAsStringAsync();

        Assert.True(res.IsSuccessStatusCode, sse);
        Assert.Contains("RUN_STARTED", sse);
        Assert.Contains("TOOL_CALL_START", sse);
        Assert.Contains("propose_note", sse);
        Assert.Contains("TOOL_CALL_RESULT", sse);
        Assert.Contains("createSurface", sse);
        Assert.Contains("approve_proposal", sse);
        Assert.Contains("Needs Your Approval", sse);
        Assert.Contains("TEXT_MESSAGE_CONTENT", sse);
        Assert.Contains("waiting for your approval", sse);
        Assert.Contains("RUN_FINISHED", sse);

        // The built-in card is an editable form pre-filled with the note.
        Assert.Contains("TextField", sse);
        Assert.Contains("/form/text", sse);

        // The tool ran as the staff member: their cookie went to the backoffice API with the proposal.
        var call = backoffice.Calls.First(c => c.Path.EndsWith("/agent/proposals"));
        Assert.Equal("/api/backoffice/agent/proposals", call.Path);
        Assert.Equal("moet_backoffice=session-cookie", call.Cookie);
        Assert.Contains("\"applicant_id\":7", call.Body);
        Assert.Contains("\"action\":\"add_note\"", call.Body);
    }

    [Fact]
    public async Task An_admin_designed_card_replaces_the_built_in_one()
    {
        const string designed = """
            {"items":[{"key":"note_card","kind":"Approval","action_name":"add_note","definition":{"nodes":[
              {"type":"heading","text":{"ar":"ملاحظة للفريق","en":"Note for the Team"}},
              {"type":"field","name":"text","input":"longtext","label":{"ar":"النص","en":"Team Note"},"required":true}]}}]}
            """;
        var backoffice = new FakeBackoffice(designed);
        using var app = new App(backoffice);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/agent") { Content = Run("note please") };
        req.Headers.Add("X-Agent-Key", Key);
        req.Headers.Add("Cookie", "moet_backoffice=session-cookie");
        req.Headers.Add("X-Lang", "en");
        var sse = await (await app.CreateClient().SendAsync(req)).Content.ReadAsStringAsync();
        Assert.Contains("Note for the Team", sse);
        Assert.Contains("Team Note", sse);
        Assert.Contains("Strong candidate", sse);
    }

    [Fact]
    public async Task Admin_settings_and_the_open_screen_reach_the_model()
    {
        const string settings = """
            {"is_enabled":true,"model_base_url":"","model_name":"","model_api_key":"","temperature":0.1,"max_steps":5,
             "extra_instructions":"Always mention the job code.","disabled_tools":["propose_email","propose_offer"]}
            """;
        using var app = new App(new FakeBackoffice(settings: settings));
        using var req = new HttpRequestMessage(HttpMethod.Post, "/agent") { Content = Run("summarise this applicant") };
        req.Headers.Add("X-Agent-Key", Key);
        req.Headers.Add("Cookie", "moet_backoffice=session-cookie");
        req.Headers.Add("X-Agent-Context", Uri.EscapeDataString("""{"page":"Applicant","applicant_id":7,"title":"Layla Hassan"}"""));
        var res = await app.CreateClient().SendAsync(req);
        await res.Content.ReadAsStringAsync();

        Assert.Contains("propose_note", app.Model.SeenTools);
        Assert.DoesNotContain("propose_email", app.Model.SeenTools);
        Assert.DoesNotContain("propose_offer", app.Model.SeenTools);
        Assert.Contains(app.Model.SeenSystem, m => m.Contains("Always mention the job code."));
        Assert.Contains(app.Model.SeenSystem, m => m.Contains("applicant_id: 7") && m.Contains("Layla Hassan"));
        // The open record is named, and the reply follows the language of the message (English here, with no X-Lang).
        Assert.Contains(app.Model.SeenSystem, m => m.Contains("Records open on the screen: applicant_id: 7"));
        Assert.Contains(app.Model.SeenSystem, m => m.Contains("last message is in English"));
    }

    [Fact]
    public async Task Only_the_backoffice_api_can_call_the_agent()
    {
        using var app = new App(new FakeBackoffice());
        var http = app.CreateClient();
        var res = await http.PostAsync("/agent", Run("hello"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/healthz")).StatusCode);
    }
}
