using Microsoft.Extensions.FileProviders;

namespace Moet.Hrms.Api.Hosting;

/// <summary>
/// Serves the built backoffice (the staff app) from wwwroot when it is there, so one site holds both the staff app
/// and its API (on IIS). Built assets are cached for a year (their names carry a hash), every other path is the app's
/// index.html (the React router), and /api keeps its JSON "not found". Without wwwroot (the containers, where the
/// backoffice has a web server of its own) nothing changes.
/// </summary>
public static class BackofficeSite
{
    const string Csp = "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; "
        + "font-src 'self' https://fonts.gstatic.com; script-src 'self'; connect-src 'self'; frame-src 'self'; frame-ancestors 'none'; "
        + "base-uri 'self'; form-action 'self'";

    /// <summary>The folder with the built app (wwwroot next to the API), or null when it is not there.</summary>
    public static string? Folder(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "wwwroot");
        return File.Exists(Path.Combine(dir, "index.html")) ? dir : null;
    }

    /// <summary>Static files, before routing (the fallback endpoint would otherwise claim every path).</summary>
    public static void UseFiles(WebApplication app, string folder)
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(folder),
            OnPrepareResponse = f =>
            {
                var h = f.Context.Response.Headers;
                var asset = f.Context.Request.Path.StartsWithSegments("/assets");
                h.CacheControl = asset ? "public, max-age=31536000, immutable" : "no-cache";
                Common(f.Context);
            },
        });
    }

    /// <summary>Every path that is not the API, a file or a missing asset: the app's index.html.</summary>
    public static Task Fallback(HttpContext ctx, string? folder)
    {
        var path = ctx.Request.Path;
        if (folder == null || path.StartsWithSegments("/api") || path.StartsWithSegments("/assets")
            || !(HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method)))
            return NotFoundEnvelope.Handle(ctx);
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        Common(ctx);
        return HttpMethods.IsHead(ctx.Request.Method) ? Task.CompletedTask : ctx.Response.SendFileAsync(Path.Combine(folder, "index.html"));
    }

    static void Common(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "same-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        h["Content-Security-Policy"] = Csp;
        if (ctx.Request.IsHttps) h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }
}
