using Microsoft.Extensions.Configuration;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Moet.Hrms.Api.Tests;

/// <summary>The API serving the built backoffice from wwwroot (one IIS site for the staff app and its API). No database.</summary>
public class BackofficeSiteTests
{
    sealed class App(string root) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseContentRoot(root);
            b.UseSetting("DB_CONNECTION", "Server=127.0.0.1,1;Database=None;User Id=x;Password=x;Connect Timeout=1;TrustServerCertificate=True");
            b.UseSetting("MIGRATE_ON_START", "0");
            b.UseSetting("RUN_WORKERS", "0");
            b.UseSetting("AGENT_KEY", "test-agent-key-0123456789");
        }
    }

    [Fact]
    public async Task Pages_assets_and_api_share_one_site()
    {
        var root = Directory.CreateTempSubdirectory("moet-site").FullName;
        Directory.CreateDirectory(Path.Combine(root, "wwwroot", "assets"));
        await File.WriteAllTextAsync(Path.Combine(root, "wwwroot", "index.html"), "<!doctype html><div id=\"root\"></div>");
        await File.WriteAllTextAsync(Path.Combine(root, "wwwroot", "assets", "app-1a2b.js"), "console.log(1)");
        using var app = new App(root);
        var http = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        foreach (var page in new[] { "/", "/ar/jobs/12", "/en/applicants?status=Open" })
        {
            var r = await http.GetAsync(page);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Contains("id=\"root\"", await r.Content.ReadAsStringAsync());
            Assert.Equal("no-cache", r.Headers.CacheControl?.ToString());
            Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").First());
            Assert.Contains("script-src 'self'", r.Headers.GetValues("Content-Security-Policy").First());
        }
        var asset = await http.GetAsync("/assets/app-1a2b.js");
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains("immutable", asset.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/assets/missing.js")).StatusCode);

        var api = await http.GetAsync("/api/nothing-here");
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
        Assert.Contains("not_found", await api.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/backoffice/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/healthz")).StatusCode);
    }
}

/// <summary>Portal-only ports and the portal API's address list (a backoffice the portal reaches from the DMZ).</summary>
public class PortalAccessTests
{
    static async Task<(int Status, string Body)> Run(string path, int localPort, string remote, Dictionary<string, string?> settings)
    {
        var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var reached = false;
        var mw = new Moet.Hrms.Api.Hosting.PortalAccess(_ => { reached = true; return Task.CompletedTask; }, cfg,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Moet.Hrms.Api.Hosting.PortalAccess>.Instance);
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Connection.LocalPort = localPort;
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remote);
        ctx.Response.Body = new MemoryStream();
        await mw.InvokeAsync(ctx);
        ctx.Response.Body.Position = 0;
        return (reached ? 0 : ctx.Response.StatusCode, await new StreamReader(ctx.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task Portal_ports_serve_only_the_portal_api_and_the_address_list_applies()
    {
        var s = new Dictionary<string, string?> { ["PORTAL_ONLY_PORTS"] = "5443, 5080", ["PORTAL_API_ALLOWED_IPS"] = "10.20.0.0/24, 127.0.0.1" };
        Assert.Equal(0, (await Run("/api/portal/jobs", 5443, "10.20.0.9", s)).Status);           // reaches the API
        Assert.Equal(0, (await Run("/healthz", 5443, "10.20.0.9", s)).Status);
        Assert.Equal(404, (await Run("/", 5443, "10.20.0.9", s)).Status);                       // no staff app there
        Assert.Equal(404, (await Run("/api/backoffice/me", 5080, "127.0.0.1", s)).Status);
        Assert.Equal(404, (await Run("/assets/app.js", 5443, "10.20.0.9", s)).Status);
        Assert.Equal(0, (await Run("/api/backoffice/me", 443, "10.1.1.1", s)).Status);          // staff port unaffected
        Assert.Equal(0, (await Run("/api/portal/jobs", 5080, "::ffff:127.0.0.1", s)).Status);
        var refused = await Run("/api/portal/jobs", 443, "10.30.0.1", s);
        Assert.Equal(403, refused.Status);
        Assert.Contains("forbidden", refused.Body);
        Assert.Equal(0, (await Run("/api/portal/jobs", 443, "203.0.113.5", [])).Status);        // off unless set
    }
}

/// <summary>Screening questions the CV never answers for the candidate.</summary>
public class ScreeningSuggestionTests
{
    static Moet.Hrms.Domain.Hiring.ScreeningQuestion Q(string text, string category = "") => new() { Text = text, Category = category };

    [Theory]
    [InlineData("What is your nationality?")]
    [InlineData("Are you a UAE citizen?")]
    [InlineData("What is your expected salary?")]
    [InlineData("What is your date of birth?")]
    [InlineData("I declare that the information is correct")]
    [InlineData("ما جنسيتك؟")]
    [InlineData("هل أنت مواطن؟")]
    [InlineData("ما الراتب المتوقع؟")]
    [InlineData("الحالة الاجتماعية")]
    public void Personal_and_eligibility_questions_are_never_suggested(string text) =>
        Assert.False(Moet.Hrms.Application.Portal.ScreeningSuggestionService.Allowed(Q(text)));

    [Theory]
    [InlineData("How many years of experience do you have in economic analysis?")]
    [InlineData("Do you hold a bachelor's degree?")]
    [InlineData("Which languages do you speak?")]
    [InlineData("كم سنة خبرة لديك في التحليل الاقتصادي؟")]
    [InlineData("هل تقيم الإجراءات المالية؟")]
    [InlineData("ما المدينة التي عملت فيها؟")]
    public void Experience_and_qualification_questions_can_be_suggested(string text) =>
        Assert.True(Moet.Hrms.Application.Portal.ScreeningSuggestionService.Allowed(Q(text)));
}

/// <summary>A saved CV's profile reused for scoring carries no personal details.</summary>
public class SavedProfileTests
{
    [Fact]
    public void Contact_details_and_the_name_are_removed_before_scoring()
    {
        const string json = """
            {"basics":{"name":"Mariam Al Hammadi","email":"mariam@example.com","phone":"+971 50 123 4567","location":{"country":"UAE"},
              "label":"Economic Analyst","summary":"Mariam has six years in trade analysis. Contact mariam@example.com."},
             "work":[{"name":"Ministry of Economy","position":"Analyst","startDate":"2019-03","endDate":"","summary":"Mariam prepared price indices","highlights":["Led by Mariam Al Hammadi"]}],
             "skills":[{"name":"Excel","keywords":["Power BI"]}],"stated_years_of_experience":6,"cv_language":"en"}
            """;
        var p = Moet.Hrms.Application.Ai.CvScoringHandler.ForScoring(json, "Mariam Al Hammadi");
        var text = p.GetRawText();
        Assert.DoesNotContain("Mariam", text);
        Assert.DoesNotContain("Hammadi", text);
        Assert.DoesNotContain("mariam@example.com", text);
        Assert.DoesNotContain("123 4567", text);
        Assert.False(p.GetProperty("basics").TryGetProperty("location", out _));
        Assert.Equal("Economic Analyst", p.GetProperty("basics").GetProperty("label").GetString());
        Assert.Equal("2019-03", p.GetProperty("work")[0].GetProperty("startDate").GetString());
        Assert.Equal("Ministry of Economy", p.GetProperty("work")[0].GetProperty("name").GetString());
        Assert.Equal(6, p.GetProperty("stated_years_of_experience").GetInt32());
    }
}
