using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Moet.Recruitment.Api.Endpoints;
using Moet.Recruitment.Api.Hosting;
using Moet.Recruitment.Application;
using Moet.Recruitment.Infrastructure;

// "--pdf-check <file>": renders a sample Arabic letter and lists the fonts found (CI runs it inside the container image).
if (args.Length >= 2 && args[0] == "--pdf-check")
{
    var renderer = new QuestPdfRenderer(new ConfigurationBuilder().AddEnvironmentVariables().Build());
    File.WriteAllBytes(args[1], renderer.Letter("MoET | Offer Letter", "# عرض عمل\n\nالسيدة سارة أحمد المحترمة،\n\nيسرنا أن نقدم لك عرض العمل لوظيفة محلل بيانات اقتصادية في وزارة الاقتصاد والسياحة، براتب شهري 25,000 درهم (Monthly salary AED 25,000).\n\nمع خالص التحية،\nفريق التوظيف", true, null));
    Console.WriteLine("Fonts: " + string.Join(", ", QuestPdfRenderer.Registered));
    return;
}

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddTelemetry("moet-recruitment-api", Moet.Recruitment.Infrastructure.AppMetrics.MeterName);

builder.Services.AddApplication();
builder.Services.AddBackofficeApplication();
builder.Services.AddInfrastructure(cfg, runWorkers: cfg["RUN_WORKERS"] != "0");
builder.Services.AddHttpClient(AgentProxy.Client, c => c.Timeout = TimeSpan.FromMinutes(10));
builder.Services.ConfigureHttpJsonOptions(o => Json.Configure(o.SerializerOptions));
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "moet_backoffice";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = cfg["COOKIE_SECURE"] == "0" ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(double.TryParse(cfg["SESSION_HOURS"], out var h) ? h : 8);
    o.SlidingExpiration = true;
    o.Events = StaffCookie.Events();
});
builder.Services.AddAuthorization(o => StaffPolicies.Add(o));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("sign-in", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("assistant", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.User.Identity?.Name ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (cfg["TRUST_PROXY"] == "1") app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeaders>();
// Outside the error envelope, so it records the final result of every action, refusals included.
app.UseMiddleware<ActionAudit>();
app.UseMiddleware<ErrorEnvelope>();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<BackofficeCsrf>();

app.MapGet("/healthz", () => Results.Ok(new { ok = true }));
PortalEndpoints.Map(app);
BackofficeEndpoints.Map(app);
AgentProxy.MapInternal(app);
app.MapFallback(NotFoundEnvelope.Handle);

if (cfg["MIGRATE_ON_START"] != "0")
    await Moet.Recruitment.Infrastructure.DependencyInjection.PrepareDatabaseAsync(app.Services, cfg, app.Logger);

app.Run();

public partial class Program;
