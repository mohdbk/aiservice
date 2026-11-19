using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Moet.Careers.Portal.Tests;

public class PortalTests
{
    const string Secret = "portal-test-session-secret-0123456789";

    /// <summary>A stand-in recruitment API: answers each portal method from a table and records the calls.</summary>
    sealed class FakeApi : HttpMessageHandler
    {
        public List<(string Method, string Query, string Body, string Auth, string ContentType)> Calls { get; } = [];
        public Dictionary<string, (HttpStatusCode Status, string Json)> Answers { get; } = new()
        {
            ["config"] = (HttpStatusCode.OK, """{"message":{"default_language":"ar","languages":["ar","en"]}}"""),
            ["authenticate"] = (HttpStatusCode.OK, """{"message":{"email":"layla@example.com","full_name":"Layla Hassan","epoch":2}}"""),
            ["applications"] = (HttpStatusCode.OK, """{"message":{"items":[{"name":"APP-1"}]}}"""),
            ["apply"] = (HttpStatusCode.OK, """{"message":{"name":"APP-2"}}"""),
            ["jobs"] = (HttpStatusCode.OK, """{"message":{"items":[],"total":0}}"""),
        };
        public bool Down { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Down) throw new HttpRequestException("connection refused");
            var method = request.RequestUri!.AbsolutePath.Split('/').Last();
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((method, request.RequestUri.Query, body, request.Headers.TryGetValues("Authorization", out var a) ? a.First() : "",
                request.Content?.Headers.ContentType?.MediaType ?? ""));
            var (status, json) = Answers.TryGetValue(method, out var known) ? known : (HttpStatusCode.OK, """{"message":{}}""");
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    sealed class CountingLimiter : RateLimiter
    {
        public List<string> Keys { get; } = [];
        public override bool Hit(string key, (int Count, int Seconds) limit) { lock (Keys) Keys.Add(key); return true; }
    }

    sealed class App(FakeApi fake, string dist, Dictionary<string, string>? extra = null, RateLimiter? limiter = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseSetting("RECRUITMENT_API_URL", "http://api.test");
            b.UseSetting("CAREERS_PORTAL_API_KEY", "portal-key");
            b.UseSetting("CAREERS_PORTAL_API_SECRET", "portal-secret");
            b.UseSetting("SESSION_SECRET", Secret);
            b.UseSetting("WEB_DIST", dist);
            b.UseSetting("MAX_BODY_MB", "2");
            foreach (var (k, v) in extra ?? []) b.UseSetting(k, v);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton(sp => new RecruitmentApi(new HttpClient(fake), sp.GetRequiredService<PortalSettings>(), NullLogger<RecruitmentApi>.Instance));
                if (limiter != null) s.AddSingleton(limiter);
            });
        }
    }

    static string Dist()
    {
        var root = Directory.CreateTempSubdirectory("moet-portal").FullName;
        var dist = Path.Combine(root, "web");
        Directory.CreateDirectory(Path.Combine(dist, "assets"));
        File.WriteAllText(Path.Combine(dist, "index.html"), "<!doctype html><div id=\"root\"></div>");
        File.WriteAllText(Path.Combine(dist, "assets", "app-1a2b.js"), "console.log(1)");
        File.WriteAllText(Path.Combine(dist, "favicon.svg"), "<svg/>");
        File.WriteAllText(Path.Combine(root, "secret.txt"), "SESSION_SECRET=do-not-serve");
        return dist;
    }

    static StringContent Json(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    static HttpRequestMessage Post(string path, object body, string? origin = null, string? cookie = null, bool portalHeader = true)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = Json(body) };
        if (portalHeader) req.Headers.Add("X-Portal", "1");
        if (origin != null) req.Headers.Add("Origin", origin);
        if (cookie != null) req.Headers.Add("Cookie", cookie);
        return req;
    }

    [Fact]
    public async Task Pages_assets_and_headers()
    {
        var fake = new FakeApi();
        using var app = new App(fake, Dist());
        var http = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var root = await http.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, root.StatusCode);
        Assert.Equal("/ar", root.Headers.Location?.ToString());

        var page = await http.GetAsync("/en/jobs/HR-OPN-2026-0001");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("id=\"root\"", await page.Content.ReadAsStringAsync());
        Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").First());
        Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").First());
        Assert.Contains("max-age=31536000", page.Headers.GetValues("Strict-Transport-Security").First());

        var asset = await http.GetAsync("/assets/app-1a2b.js");
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains("immutable", asset.Headers.CacheControl?.ToString());
        Assert.Equal("text/javascript", asset.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/favicon.svg")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/assets/missing.js")).StatusCode);

        foreach (var raw in new[] { "/assets/../../secret.txt", "/assets/%2e%2e/%2e%2e/secret.txt", "/..%2f..%2fsecret.txt", "/assets/..%5C..%5Csecret.txt",
                     "/assets/C:%5CWindows%5Cwin.ini", "/favicon.svg::$DATA", "/../secret.txt" })
        {
            var r = await http.GetAsync(raw);
            Assert.DoesNotContain("do-not-serve", await r.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await http.PostAsync("/en/jobs", Json(new { }))).StatusCode);
    }

    [Fact]
    public async Task Sign_in_checks_the_request_and_sets_a_secure_session()
    {
        var fake = new FakeApi();
        using var app = new App(fake, Dist());
        var http = app.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Post("/api/login", new { email = "a@b.c", password = "x" }, portalHeader: false))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Post("/api/login", new { email = "a@b.c", password = "x" }, origin: "https://evil.example"))).StatusCode);
        var bad = await http.PostAsync("/api/login", new StringContent("email=a", Encoding.UTF8, "application/x-www-form-urlencoded"));
        Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);

        var ok = await http.SendAsync(Post("/api/login", new { email = " Layla@Example.com ", password = "secret" }, origin: "http://localhost"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var cookie = ok.Headers.GetValues("Set-Cookie").First();
        Assert.StartsWith("mc_session=", cookie);
        Assert.Contains("HttpOnly", cookie);
        Assert.Contains("SameSite=Lax", cookie);
        Assert.Contains("Secure", cookie);
        var call = fake.Calls.Single(c => c.Method == "authenticate");
        Assert.Equal("token portal-key:portal-secret", call.Auth);
        Assert.Contains("\"email\":\"layla@example.com\"", call.Body);
        var user = JsonNode.Parse(await ok.Content.ReadAsStringAsync())!["user"]!;
        Assert.Equal("Layla Hassan", (string?)user["full_name"]);

        var me = JsonNode.Parse(await (await http.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/me") { Headers = { { "Cookie", cookie.Split(';')[0] } } })).Content.ReadAsStringAsync());
        Assert.Equal("layla@example.com", (string?)me!["user"]!["email"]);
    }

    [Fact]
    public async Task Signed_in_calls_carry_the_account_and_refuse_without_a_session()
    {
        var fake = new FakeApi();
        using var app = new App(fake, Dist());
        var http = app.CreateClient();

        var anon = await http.GetAsync("/api/applications?lang=en");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        Assert.Contains("signed_out", await anon.Content.ReadAsStringAsync());
        Assert.Contains(anon.Headers.GetValues("Set-Cookie"), c => c.Contains("Max-Age=0"));

        var cookie = "mc_session=" + Session.CreateToken(new PortalUser("layla@example.com", "Layla", 2), Secret, 8);
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/applications?lang=en");
        req.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(req)).StatusCode);
        var call = fake.Calls.Single(c => c.Method == "applications");
        Assert.Contains("email=layla%40example.com", call.Query);
        Assert.Contains("epoch=2", call.Query);
        Assert.Contains("lang=en", call.Query);

        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/api/jobs?lang=../../x")).StatusCode);
        var forged = new HttpRequestMessage(HttpMethod.Get, "/api/applications?lang=en");
        forged.Headers.Add("Cookie", "mc_session=" + Session.CreateToken(new PortalUser("other@example.com", "", 0), "another-secret-0123456789-0123456789", 8));
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(forged)).StatusCode);
    }

    [Fact]
    public async Task Applying_checks_the_cv_and_sends_it_on_as_a_file()
    {
        var fake = new FakeApi();
        using var app = new App(fake, Dist());
        var http = app.CreateClient();
        var cookie = "mc_session=" + Session.CreateToken(new PortalUser("layla@example.com", "Layla", 2), Secret, 8);
        var cv = Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF-1.4 test"));

        async Task<(HttpStatusCode, string)> Apply(object body)
        {
            var r = await http.SendAsync(Post("/api/applications", body, cookie: cookie));
            return (r.StatusCode, await r.Content.ReadAsStringAsync());
        }
        Assert.Contains("cv_required", (await Apply(new { job = "JOB-1" })).Item2);
        Assert.Contains("cv_type", (await Apply(new { job = "JOB-1", cv = new { name = "cv.exe", type = "application/octet-stream", data = cv } })).Item2);
        Assert.Contains("cv_empty", (await Apply(new { job = "JOB-1", cv = new { name = "cv.pdf", type = "application/pdf", data = "" } })).Item2);

        var (status, text) = await Apply(new { job = "JOB-1", phone = "+971501234567", declaration = true, screening = new { q1 = "yes" },
            cv = new { name = "my\"cv.pdf", type = "application/pdf", data = cv } });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("APP-2", text);
        var call = fake.Calls.Single(c => c.Method == "apply");
        Assert.Equal("multipart/form-data", call.ContentType);
        Assert.Contains("layla@example.com", call.Body);
        Assert.Contains("%PDF-1.4 test", call.Body);
        Assert.Contains("my_cv.pdf", call.Body);
        Assert.Contains("{\"q1\":\"yes\"}", call.Body);

        // A saved CV from the library: no file, its id instead.
        Assert.Equal(HttpStatusCode.OK, (await Apply(new { job = "JOB-1", cv_id = 7 })).Item1);
        Assert.Contains("cv_id", fake.Calls.Last(c => c.Method == "apply").Body);

        var huge = new string('A', 3 * 1024 * 1024);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Apply(new { job = "JOB-1", cv = new { name = "cv.pdf", data = huge } })).Item1);
    }

    [Fact]
    public async Task Api_errors_reach_the_candidate_app_as_codes()
    {
        var fake = new FakeApi();
        fake.Answers["authenticate"] = (HttpStatusCode.Unauthorized, """{"message":{"error":{"code":"wrong_password","params":{"left":2}}}}""");
        using var app = new App(fake, Dist());
        var http = app.CreateClient();

        var r = await http.SendAsync(Post("/api/login", new { email = "a@b.c", password = "x" }));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        var err = JsonNode.Parse(await r.Content.ReadAsStringAsync())!["error"]!;
        Assert.Equal("wrong_password", (string?)err["code"]);
        Assert.Equal(2, (int?)err["params"]!["left"]);

        fake.Down = true;
        var down = await http.GetAsync("/api/jobs?lang=ar");
        Assert.Equal(HttpStatusCode.BadGateway, down.StatusCode);
        Assert.Contains("\"server\"", await down.Content.ReadAsStringAsync());
        // Forgot password never reveals whether an account exists, but still says when the API is down.
        fake.Down = false;
        fake.Answers["request_password_reset"] = (HttpStatusCode.NotFound, """{"message":{"error":{"code":"no_account"}}}""");
        var forgot = await http.SendAsync(Post("/api/forgot-password", new { email = "nobody@example.com" }));
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        Assert.Contains("\"sent\":true", await forgot.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Behind_a_proxy_the_client_address_is_the_entry_the_proxy_added()
    {
        var limiter = new CountingLimiter();
        using var app = new App(new FakeApi(), Dist(), new() { ["TRUST_PROXY"] = "1", ["CLIENT_IP_FROM"] = "last" }, limiter);
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/config?lang=en");
        req.Headers.Add("X-Forwarded-For", "6.6.6.6, 10.1.2.3");
        await app.CreateClient().SendAsync(req);
        Assert.Contains("all:10.1.2.3", limiter.Keys);
        Assert.DoesNotContain(limiter.Keys, k => k.Contains("6.6.6.6"));
    }

    [Fact]
    public void Sessions_from_the_earlier_node_server_still_work()
    {
        // Made by portal/server/src/session.js createToken({ email, full_name: "ليلى حسن", epoch: 3 }, "x" * 40, 8, 2026-11-19T10:00Z).
        const string token = "eyJlIjoibGF5bGFAZXhhbXBsZS5jb20iLCJuIjoi2YTZitmE2Ykg2K3Ys9mGIiwidiI6MywieCI6MTc5NTExMTIwMDAwMH0.Q-0mceF2SOlpyFVkvDu4A3wQCBSTzKDw4O6opPKt-rw";
        var at = DateTimeOffset.FromUnixTimeMilliseconds(1795082400000);
        var u = Session.ReadToken(token, new string('x', 40), at.AddHours(1));
        Assert.Equal(new PortalUser("layla@example.com", "ليلى حسن", 3), u);
        Assert.Null(Session.ReadToken(token, new string('x', 40), at.AddHours(9)));
        Assert.Null(Session.ReadToken(token, new string('y', 40), at.AddHours(1)));
        Assert.Null(Session.ReadToken(token[..^2] + "AA", new string('x', 40), at.AddHours(1)));
        var mine = Session.CreateToken(new PortalUser("a@b.c", "A", 1), new string('x', 40), 1);
        Assert.Equal("a@b.c", Session.ReadToken(mine, new string('x', 40))?.Email);
    }
}
