using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Moet.Recruitment.Api.Endpoints;
using Moet.Recruitment.Application.Admin;
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

/// <summary>Writes request rule failures as {"error": {"code", "params"}} for the admin API.</summary>
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

/// <summary>Changes to the admin API need the X-Admin header (a cross-site form cannot send it) and a same-origin request.</summary>
public sealed class AdminCsrf(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        if (req.Path.StartsWithSegments("/api/admin") && !HttpMethods.IsGet(req.Method) && !HttpMethods.IsHead(req.Method))
        {
            var origin = req.Headers.Origin.ToString();
            var sameOrigin = origin.Length == 0 || Uri.TryCreate(origin, UriKind.Absolute, out var o) && o.Authority == req.Host.Value;
            if (req.Headers["X-Admin"] != "1" || !sameOrigin)
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
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "same-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        h["Content-Security-Policy"] =
            "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
            "font-src 'self' https://fonts.gstatic.com; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        if (ctx.Request.IsHttps) h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        if (ctx.Request.Path.StartsWithSegments("/api")) h["Cache-Control"] = "no-store";
        return next(ctx);
    }
}

/// <summary>Serves the admin panel's index.html for its client-side routes; unknown API paths get 404.</summary>
public static class SpaFallback
{
    public static async Task Handle(HttpContext ctx, IWebHostEnvironment env)
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsJsonAsync(new { error = new { code = "not_found", @params = new { } } }, Json.Options);
            return;
        }
        var index = env.WebRootFileProvider.GetFileInfo("index.html");
        if (!index.Exists)
        {
            ctx.Response.StatusCode = 404;
            return;
        }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.SendFileAsync(index);
    }
}
