using Microsoft.AspNetCore.Server.IIS;
using Moet.Careers.Portal;

// MoET careers portal server (Ministry of Economy & Tourism). On IIS it runs in-process in the portal site; in a
// container it listens on PORT (8080).
var builder = WebApplication.CreateBuilder(args);
var settings = PortalSettings.FromConfiguration(builder.Configuration, builder.Environment.ContentRootPath);

if (builder.Configuration["PORT"] is { Length: > 0 } port && string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_URLS"]))
    builder.WebHost.UseUrls($"http://*:{port}");
// CVs arrive as JSON (base64), up to MAX_BODY_MB; each route sets its own lower limit.
var maxBody = (settings.MaxBodyMb + 2) * 1024L * 1024;
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxBody);
builder.Services.Configure<IISServerOptions>(o => o.MaxRequestBodySize = maxBody);

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<RateLimiter>();
builder.Services.AddSingleton(sp => new RecruitmentApi(
    new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = Timeout.InfiniteTimeSpan },
    settings, sp.GetRequiredService<ILogger<RecruitmentApi>>()));
builder.Services.AddSingleton<PortalApp>();

var app = builder.Build();
var portal = app.Services.GetRequiredService<PortalApp>();
app.Run(portal.HandleAsync);
app.Logger.LogInformation("Careers portal ready (API at {Api})", settings.ApiUrl);
app.Run();

public partial class Program;
