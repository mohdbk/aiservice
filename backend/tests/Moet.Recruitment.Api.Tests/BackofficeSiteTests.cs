using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Moet.Recruitment.Api.Tests;

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
