using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Moet.Recruitment.Api.Endpoints;
using Moet.Recruitment.Application.Backoffice;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Api.Hosting;

public static class StaffPolicies
{
    public const string Staff = "staff";
    public const string Admin = "recruitment-admin";
    public const string SystemAdmin = "administrator";

    public static void Add(AuthorizationOptions o)
    {
        o.AddPolicy(Staff, p => p.RequireAuthenticatedUser().RequireClaim("sid"));
        o.AddPolicy(Admin, p => p.RequireAuthenticatedUser().RequireRole(nameof(StaffRole.RecruitmentAdmin), nameof(StaffRole.Administrator)));
        o.AddPolicy(SystemAdmin, p => p.RequireAuthenticatedUser().RequireRole(nameof(StaffRole.Administrator)));
    }
}

public static class StaffCookie
{
    public static ClaimsPrincipal Principal(StaffSession s) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.Name, s.FullName), new Claim(ClaimTypes.Email, s.Email), new Claim("sid", s.Id.ToString()),
        new Claim(ClaimTypes.Role, s.Role.ToString()), new Claim("ver", s.SessionVersion.ToString()),
    ], CookieAuthenticationDefaults.AuthenticationScheme));

    /// <summary>API answers 401/403 instead of redirecting; every request re-checks the account is still active.</summary>
    public static CookieAuthenticationEvents Events() => new()
    {
        OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; },
        OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; },
        OnValidatePrincipal = async ctx =>
        {
            var p = ctx.Principal;
            if (!int.TryParse(p?.FindFirst("sid")?.Value, out var id) || !int.TryParse(p?.FindFirst("ver")?.Value, out var ver))
            {
                ctx.RejectPrincipal();
                return;
            }
            var auth = ctx.HttpContext.RequestServices.GetRequiredService<StaffAuthService>();
            var s = await auth.ValidateAsync(id, ver, ctx.HttpContext.RequestAborted);
            if (s == null) ctx.RejectPrincipal();
            else if (s.Role.ToString() != p!.FindFirst(ClaimTypes.Role)?.Value || s.FullName != p.Identity?.Name)
            {
                ctx.ReplacePrincipal(Principal(s));
                ctx.ShouldRenew = true;
            }
        },
    };
}

/// <summary>Writes request rule failures as {"error": {"code", "params"}} for the backoffice API.</summary>
public sealed class ErrorEnvelope(RequestDelegate next, ILogger<ErrorEnvelope> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (DomainException e) when (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = e.Status;
            await ctx.Response.WriteAsJsonAsync(new { error = new { code = e.Code, @params = e.Params } }, Json.Options);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException) when (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = 409;
            await ctx.Response.WriteAsJsonAsync(new { error = new { code = "changed_elsewhere", @params = new { } } }, Json.Options);
        }
        catch (Exception e) when (!ctx.Response.HasStarted && e is not OperationCanceledException)
        {
            log.LogError(e, "Unhandled error on {Path}", ctx.Request.Path);
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsJsonAsync(new { error = new { code = "server", @params = new { } } }, Json.Options);
        }
    }
}

/// <summary>Changes to the backoffice API need the X-Backoffice header (a cross-site form cannot send it) and a same-origin request.</summary>
public sealed class BackofficeCsrf(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        if (req.Path.StartsWithSegments("/api/backoffice") && !HttpMethods.IsGet(req.Method) && !HttpMethods.IsHead(req.Method))
        {
            var origin = req.Headers.Origin.ToString();
            var sameOrigin = origin.Length == 0 || Uri.TryCreate(origin, UriKind.Absolute, out var o) && o.Authority == req.Host.Value;
            if (req.Headers["X-Backoffice"] != "1" || !sameOrigin)
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsJsonAsync(new { error = new { code = "forbidden", @params = new { } } }, Json.Options);
                return;
            }
        }
        await next(ctx);
    }
}

public sealed class SecurityHeaders(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        // CV files are shown inside the backoffice (same origin, through its proxy); everything else is never framed.
        var framable = ctx.Request.Path.Value?.EndsWith("/cv", StringComparison.Ordinal) == true;
        h["X-Frame-Options"] = framable ? "SAMEORIGIN" : "DENY";
        h["Referrer-Policy"] = "same-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        h["Content-Security-Policy"] = framable ? "frame-ancestors 'self'" : "default-src 'none'; frame-ancestors 'none'";
        if (ctx.Request.IsHttps) h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        h["Cache-Control"] = "no-store";
        return next(ctx);
    }
}

/// <summary>The API serves no web pages (the backoffice and portal are their own services): anything unknown is a JSON 404.</summary>
public static class NotFoundEnvelope
{
    public static Task Handle(HttpContext ctx)
    {
        ctx.Response.StatusCode = 404;
        return ctx.Response.WriteAsJsonAsync(new { error = new { code = "not_found", @params = new { } } }, Json.Options);
    }
}
