using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Features;

namespace Moet.Careers.Portal;

/// <summary>A refusal with an HTTP status, an error code the candidate app translates, and its parameters.</summary>
public sealed class PortalError(int status, string code, object? parameters = null) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public object? Parameters { get; } = parameters;
}

/// <summary>
/// The careers portal server: the candidate app's small JSON API under /api (sign-in, accounts, applications, tests,
/// CVs, the candidate's own data), each request checked here (session, same-origin, rate limits, sizes) and passed on
/// to the recruitment API with the portal's key; and the built candidate app.
/// </summary>
public sealed partial class PortalApp(PortalSettings cfg, HrmsApi api, RateLimiter limiter, ILogger<PortalApp> log)
{
    static readonly string Csp = string.Join("; ",
        "default-src 'self'", "script-src 'self'", "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
        "font-src 'self' data: https://fonts.gstatic.com", "img-src 'self' data:", "connect-src 'self'",
        "frame-ancestors 'none'", "base-uri 'self'", "form-action 'self'", "object-src 'none'");

    static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".js"] = "text/javascript; charset=utf-8", [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8", [".svg"] = "image/svg+xml", [".png"] = "image/png", [".jpg"] = "image/jpeg",
        [".ico"] = "image/x-icon", [".woff2"] = "font/woff2", [".webp"] = "image/webp", [".txt"] = "text/plain; charset=utf-8",
        [".webmanifest"] = "application/manifest+json",
    };

    [GeneratedRegex(@"\.(pdf|docx|jpe?g|png)$", RegexOptions.IgnoreCase)] private static partial Regex CvExt();
    [GeneratedRegex(@"^[a-z]{2,3}(-[A-Za-z]{2,4})?$")] private static partial Regex Lang();
    [GeneratedRegex(@"^/[\w.-]+\.(ico|png|svg|txt|webmanifest)$")] private static partial Regex RootFile();
    [GeneratedRegex(@"[\\/\r\n""]")] private static partial Regex BadNameChars();
    [GeneratedRegex(@"^/api/jobs/([A-Za-z0-9._-]{1,140})$")] private static partial Regex JobPath();
    [GeneratedRegex(@"^/api/applications/([A-Za-z0-9._-]{1,140})$")] private static partial Regex ApplicationPath();
    [GeneratedRegex(@"^/api/assessments/([A-Za-z0-9._-]{1,140})(?:/(state|start|answer|file|next|event))?$")] private static partial Regex AssessmentPath();
    [GeneratedRegex(@"^/api/account/alerts/([0-9]{1,9})$")] private static partial Regex AlertPath();
    [GeneratedRegex(@"^/api/cvs/(\d{1,9})(?:/(default|prefill))?$")] private static partial Regex CvPath();
    [GeneratedRegex(@"^/api/cv-draft/([0-9a-fA-F-]{36})$")] private static partial Regex DraftPath();
    [GeneratedRegex(@"^/api/applications/([A-Za-z0-9._-]{1,140})/(offer|withdraw|reschedule|book|corrections)$")] private static partial Regex ApplicationActionPath();

    readonly ConcurrentDictionary<string, (JsonNode? Value, DateTime Until)> configCache = new();

    // ---------------------------------------------------------------- entry

    public async Task HandleAsync(HttpContext ctx)
    {
        SecurityHeaders(ctx.Response);
        var path = ctx.Request.Path.Value ?? "/";
        try
        {
            if (path == "/healthz") { await Send(ctx, 200, new { ok = true }); return; }
            if (path.StartsWith("/api/", StringComparison.Ordinal)) { await ApiAsync(ctx, path); return; }
            await WebAsync(ctx, path);
        }
        catch (Exception e) when (e is PortalError or ApiError)
        {
            var (status, code, prms) = e is PortalError p ? (p.Status, p.Code, p.Parameters) : (((ApiError)e).Code == "server" ? 502 : ((ApiError)e).Status, ((ApiError)e).Code, (object?)((ApiError)e).Parameters);
            if (code == "signed_out" && path != "/api/login") ctx.Response.Headers.Append("Set-Cookie", ClearCookie());
            if (status == 413) ctx.Response.Headers.Connection = "close";   // the rest of the oversized body is not read
            if (!ctx.Response.HasStarted) await Send(ctx, status, new { error = new { code, @params = prms ?? new { } } });
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "portal error");
            if (!ctx.Response.HasStarted) await Send(ctx, 500, new { error = new { code = "server", @params = new { } } });
        }
    }

    void SecurityHeaders(HttpResponse res)
    {
        var h = res.Headers;
        h["Content-Security-Policy"] = Csp;
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["X-Frame-Options"] = "DENY";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        if (cfg.CookieSecure) h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }

    static async Task Send(HttpContext ctx, int status, object? body, string? disposition = null)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-store";
        if (disposition != null) ctx.Response.Headers.ContentDisposition = disposition;
        await ctx.Response.WriteAsync(body is JsonNode n ? n.ToJsonString() : JsonSerializer.Serialize(body), Encoding.UTF8);
    }

    // ---------------------------------------------------------------- request helpers

    string ClientIp(HttpContext ctx)
    {
        if (cfg.TrustProxy)
        {
            var all = ctx.Request.Headers[cfg.ClientIpHeader].ToString().Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();
            var fwd = all.Length == 0 ? "" : cfg.ClientIpFromLast ? all[^1] : all[0];
            if (fwd.Length > 0) return fwd;
        }
        return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    void Limit(string key, (int, int) limit)
    {
        if (!limiter.Hit(key, limit)) throw new PortalError(429, "rate_limited");
    }

    /// <summary>Changes need a custom header (cannot be sent cross-site without CORS) and, when present, a same-origin Origin.</summary>
    void CheckCsrf(HttpContext ctx)
    {
        if (ctx.Request.Headers["X-Portal"].ToString() != "1") throw new PortalError(403, "forbidden");
        var origin = ctx.Request.Headers.Origin.ToString();
        if (origin.Length == 0) return;
        var host = cfg.TrustProxy && ctx.Request.Headers["X-Forwarded-Host"].ToString() is { Length: > 0 } fh ? fh : ctx.Request.Host.Value;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var o) || !string.Equals(o.Authority, host, StringComparison.OrdinalIgnoreCase))
            throw new PortalError(403, "forbidden");
    }

    async Task<JsonObject> ReadJson(HttpContext ctx, long maxBytes)
    {
        if (!ctx.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? true) throw new PortalError(415, "bad_request");
        if (ctx.Request.ContentLength > maxBytes) throw new PortalError(413, "payload_too_large");
        var limitFeature = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limitFeature is { IsReadOnly: false }) limitFeature.MaxRequestBodySize = maxBytes + 1;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int n;
        try
        {
            while ((n = await ctx.Request.Body.ReadAsync(chunk, ctx.RequestAborted)) > 0)
            {
                buffer.Write(chunk, 0, n);
                if (buffer.Length > maxBytes) throw new PortalError(413, "payload_too_large");
            }
        }
        catch (BadHttpRequestException) { throw new PortalError(413, "payload_too_large"); }
        try
        {
            var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            return JsonNode.Parse(text.Length == 0 ? "{}" : text) as JsonObject ?? throw new PortalError(400, "bad_request");
        }
        catch (JsonException) { throw new PortalError(400, "bad_request"); }
    }

    static string Str(JsonNode? node, int max) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? (s.Length > max ? s[..max] : s) : "";

    static string Str(JsonObject b, string key, int max) => Str(b[key], max);

    static bool IsTrue(JsonNode? node) => node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    static bool Truthy(JsonNode? node) => node switch
    {
        null => false,
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<string>(out var s) => s.Length > 0,
        JsonValue v when v.TryGetValue<double>(out var d) => d != 0 && !double.IsNaN(d),
        _ => true,
    };

    static long? Int(JsonNode? node) => node is JsonValue v && v.TryGetValue<long>(out var n) ? n : null;

    static long Number(JsonNode? node)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d) && double.IsFinite(d)) return (long)d;
            if (v.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) && double.IsFinite(p)) return (long)p;
        }
        return 0;
    }

    static string LangParam(HttpContext ctx)
    {
        var l = ctx.Request.Query["lang"].ToString();
        if (!Lang().IsMatch(l)) throw new PortalError(400, "unknown_language");
        return l;
    }

    static string Q(HttpContext ctx, string key, int max)
    {
        var v = ctx.Request.Query[key].ToString();
        return v.Length > max ? v[..max] : v;
    }

    PortalUser? CurrentUser(HttpContext ctx) => Session.ReadToken(Session.FromRequest(ctx.Request), cfg.SessionSecret);

    PortalUser RequireUser(HttpContext ctx) => CurrentUser(ctx) ?? throw new PortalError(401, "signed_out");

    string SessionCookie(PortalUser u) => Session.CookieHeader(Session.CreateToken(u, cfg.SessionSecret, cfg.SessionHours), cfg.CookieSecure, (long)Math.Round(cfg.SessionHours * 3600));

    string ClearCookie() => Session.CookieHeader("", cfg.CookieSecure, 0);

    static PortalUser UserFrom(JsonNode? r) =>
        new(Str(r?["email"], 400), Str(r?["full_name"], 400), r?["epoch"] is JsonValue e && e.TryGetValue<double>(out var d) ? (long)d : 0);

    static object PublicUser(PortalUser u) => new { email = u.Email, full_name = u.FullName };

    /// <summary>The fields every signed-in call carries: the account, and the session version from sign-in.</summary>
    static JsonObject Who(PortalUser u) => new() { ["email"] = u.Email, ["epoch"] = u.Epoch };

    static Dictionary<string, object?> WhoQuery(PortalUser u, params (string, object?)[] more)
    {
        var d = new Dictionary<string, object?> { ["email"] = u.Email, ["epoch"] = u.Epoch };
        foreach (var (k, v) in more) d[k] = v;
        return d;
    }

    static JsonObject With(JsonObject o, params (string, JsonNode?)[] more)
    {
        foreach (var (k, v) in more) o[k] = v;
        return o;
    }

    async Task<JsonNode?> SiteConfig(string lang, CancellationToken ct)
    {
        if (configCache.TryGetValue(lang, out var hit) && hit.Until > DateTime.UtcNow) return hit.Value;
        var value = await api.GetAsync("config", new() { ["lang"] = lang.Length == 0 ? null : lang }, ct);
        configCache[lang] = (value, DateTime.UtcNow.AddSeconds(60));
        return value;
    }

    /// <summary>A file sent as JSON {name, type, data (base64)}, checked like the Node server did.</summary>
    static UploadFile? FileFrom(JsonNode? node, out string name)
    {
        var f = node as JsonObject;
        name = BadNameChars().Replace(Str(f?["name"], 200), "_");
        if (f == null || name.Length == 0 || f["data"] is not JsonValue d || !d.TryGetValue<string>(out var b64)) return null;
        byte[] data;
        try { data = Convert.FromBase64String(b64); } catch (FormatException) { data = []; }
        return new UploadFile(name, data, Str(f["type"], 100));
    }

    // ---------------------------------------------------------------- API routes

    async Task ApiAsync(HttpContext ctx, string path)
    {
        var m = ctx.Request.Method;
        var ct = ctx.RequestAborted;
        var ip = ClientIp(ctx);
        Limit($"all:{ip}", RateLimiter.All);
        bool Get() => m == "GET";
        bool Post() => m == "POST";

        if (Get() && path == "/api/config")
        {
            var lang = ctx.Request.Query["lang"].ToString().Length > 0 ? LangParam(ctx) : "";
            await Send(ctx, 200, await SiteConfig(lang, ct));
            return;
        }
        if (Get() && path == "/api/jobs")
        {
            var page = int.TryParse(ctx.Request.Query["page"], out var p) ? Math.Clamp(p, 1, 1000) : 1;
            await Send(ctx, 200, await api.GetAsync("jobs", new()
            {
                ["lang"] = LangParam(ctx), ["q"] = Q(ctx, "q", 100), ["department"] = Q(ctx, "department", 140), ["location"] = Q(ctx, "location", 140), ["page"] = page,
            }, ct));
            return;
        }
        var mm = JobPath().Match(path);
        if (Get() && mm.Success) { await Send(ctx, 200, await api.GetAsync("job", new() { ["lang"] = LangParam(ctx), ["name"] = mm.Groups[1].Value }, ct)); return; }

        if (Get() && path == "/api/logo")
        {
            var (data, type) = await api.LogoAsync(ctx.Request.Query["dark"] == "1", ct);
            if (!Regex.IsMatch(type, @"^image/(png|jpeg|svg\+xml|webp|gif)")) throw new PortalError(404, "not_found");
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = type;
            ctx.Response.Headers.CacheControl = "public, max-age=3600";
            ctx.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'";
            await ctx.Response.Body.WriteAsync(data, ct);
            return;
        }

        if (Get() && path == "/api/me")
        {
            var cu = CurrentUser(ctx);
            await Send(ctx, 200, new { user = cu == null ? null : PublicUser(cu) });
            return;
        }

        if (Post() && path == "/api/login")
        {
            CheckCsrf(ctx);
            var b = await ReadJson(ctx, 16_384);
            var email = Str(b, "email", 200).Trim().ToLowerInvariant();
            Limit($"login_ip:{ip}", RateLimiter.LoginIp);
            Limit($"login_email:{email}", RateLimiter.LoginEmail);
            var user = UserFrom(await api.PostAsync("authenticate", new() { ["email"] = email, ["password"] = Str(b, "password", 256) }, ct));
            ctx.Response.Headers.Append("Set-Cookie", SessionCookie(user));
            await Send(ctx, 200, new { user = PublicUser(user) });
            return;
        }
        if (Post() && path == "/api/signup")
        {
            CheckCsrf(ctx);
            Limit($"signup:{ip}", RateLimiter.Signup);
            var b = await ReadJson(ctx, 16_384);
            var r = await api.PostAsync("signup", new()
            {
                ["full_name"] = Str(b, "full_name", 140), ["email"] = Str(b, "email", 200), ["password"] = Str(b, "password", 256),
                ["lang"] = Str(b, "lang", 10), ["next_path"] = Str(b, "next", 300), ["cv_draft"] = Str(b, "cv_draft", 64),
            }, ct);
            if (Truthy(r?["verification_sent"])) { await Send(ctx, 200, new { verification_sent = true, email = r?["email"]?.DeepClone() }); return; }
            var u = UserFrom(r);
            ctx.Response.Headers.Append("Set-Cookie", SessionCookie(u));
            await Send(ctx, 200, new { user = PublicUser(u) });
            return;
        }
        // A CV given on the sign-up page, before the account exists: read so the form can be filled in from it, and saved to
        // the account when the person signs up. Not signed in, so limited per address.
        if (Post() && path == "/api/cv-draft")
        {
            CheckCsrf(ctx);
            Limit($"cvdraft:{ip}", RateLimiter.CvDraft);
            var b = await ReadJson(ctx, cfg.MaxBodyMb * 1024L * 1024);
            var file = FileFrom(b["cv"], out var name) ?? throw new PortalError(400, "cv_required");
            if (!CvExt().IsMatch(name)) throw new PortalError(400, "cv_type");
            if (file.Data.Length == 0) throw new PortalError(400, "cv_empty");
            await Send(ctx, 200, await api.PostFormAsync("cv_draft", [], "file", file, ct));
            return;
        }
        mm = DraftPath().Match(path);
        if (Get() && mm.Success) { await Send(ctx, 200, await api.GetAsync("cv_draft_status", new() { ["token"] = mm.Groups[1].Value }, ct)); return; }
        // Suggested screening answers from the candidate's chosen CV, for them to check on the application form.
        if (Get() && path == "/api/screening-suggestions")
        {
            var u = RequireUser(ctx);
            var cvId = long.TryParse(ctx.Request.Query["cv_id"], out var c) && c > 0 ? c : 0;
            await Send(ctx, 200, await api.GetAsync("screening_suggestions", WhoQuery(u, ("job", Q(ctx, "job", 140)), ("cv_id", cvId)), ct));
            return;
        }

        // Account links. Resend and forgot always answer the same way (no account discovery).
        if (Post() && (path == "/api/resend-verification" || path == "/api/forgot-password"))
        {
            CheckCsrf(ctx);
            var b = await ReadJson(ctx, 16_384);
            var email = Str(b, "email", 200).Trim().ToLowerInvariant();
            Limit($"account_ip:{ip}", RateLimiter.AccountIp);
            Limit($"account_email:{email}", RateLimiter.AccountEmail);
            try
            {
                if (path == "/api/resend-verification")
                    await api.PostAsync("resend_verification", new() { ["email"] = email, ["lang"] = Str(b, "lang", 10), ["next_path"] = Str(b, "next", 300) }, ct);
                else
                    await api.PostAsync("request_password_reset", new() { ["email"] = email, ["lang"] = Str(b, "lang", 10) }, ct);
            }
            catch (ApiError e) when (e.Code != "server") { /* the same answer either way */ }
            await Send(ctx, 200, new { sent = true });
            return;
        }
        if (Post() && path == "/api/verify-email")
        {
            CheckCsrf(ctx);
            Limit($"verify:{ip}", RateLimiter.Verify);
            var b = await ReadJson(ctx, 4096);
            var r = await api.PostAsync("verify_email", new() { ["token"] = Str(b, "token", 200) }, ct);
            var u = UserFrom(r);
            ctx.Response.Headers.Append("Set-Cookie", SessionCookie(u));
            await Send(ctx, 200, new { user = PublicUser(u), next = Str(r?["next"], 300), lang = Str(r?["lang"], 10) });
            return;
        }
        if (Post() && path == "/api/reset-password")
        {
            CheckCsrf(ctx);
            Limit($"reset:{ip}", RateLimiter.Reset);
            var b = await ReadJson(ctx, 4096);
            await api.PostAsync("reset_password", new() { ["token"] = Str(b, "token", 200), ["password"] = Str(b, "password", 256) }, ct);
            ctx.Response.Headers.Append("Set-Cookie", ClearCookie());
            await Send(ctx, 200, new { ok = true });
            return;
        }
        if (Post() && path == "/api/logout")
        {
            CheckCsrf(ctx);
            ctx.Response.Headers.Append("Set-Cookie", ClearCookie());
            await Send(ctx, 200, new { ok = true });
            return;
        }

        if (Get() && path == "/api/applications")
        {
            var u = RequireUser(ctx);
            await Send(ctx, 200, await api.GetAsync("applications", WhoQuery(u, ("lang", LangParam(ctx))), ct));
            return;
        }
        mm = ApplicationPath().Match(path);
        if (Get() && mm.Success)
        {
            var u = RequireUser(ctx);
            await Send(ctx, 200, await api.GetAsync("application", WhoQuery(u, ("name", mm.Groups[1].Value), ("lang", LangParam(ctx))), ct));
            return;
        }
        if (Post() && path == "/api/applications") { await ApplyAsync(ctx, ip, ct); return; }

        // Tests (candidate side). The API times every section; answers are saved one by one.
        if (Get() && path == "/api/assessments")
        {
            var u = RequireUser(ctx);
            await Send(ctx, 200, await api.GetAsync("assessments", WhoQuery(u, ("lang", LangParam(ctx))), ct));
            return;
        }
        mm = AssessmentPath().Match(path);
        if (mm.Success && await AssessmentAsync(ctx, mm.Groups[1].Value, mm.Groups[2].Success ? mm.Groups[2].Value : "", ct)) return;

        // The candidate's own data: a copy to download, and a request to delete the account.
        if (Get() && path == "/api/account")
        {
            var u = RequireUser(ctx);
            await Send(ctx, 200, await api.GetAsync("data_requests", WhoQuery(u), ct));
            return;
        }
        if (Get() && path == "/api/account/data")
        {
            var u = RequireUser(ctx);
            Limit($"mydata:{u.Email}", RateLimiter.Correction);
            await Send(ctx, 200, await api.GetAsync("my_data", WhoQuery(u), ct), "attachment; filename=\"my-data.json\"");
            return;
        }
        // Job alerts: saved searches emailed when new matching jobs are published.
        if (Get() && path == "/api/account/alerts")
        {
            var u = RequireUser(ctx);
            await Send(ctx, 200, await api.GetAsync("job_alerts", WhoQuery(u, ("lang", LangParam(ctx))), ct));
            return;
        }
        if (Post() && path == "/api/account/alerts")
        {
            CheckCsrf(ctx);
            var u = RequireUser(ctx);
            var b = await ReadJson(ctx, 4096);
            await Send(ctx, 200, await api.PostAsync("save_job_alert", With(Who(u), ("keywords", Str(b, "keywords", 300)), ("department", Number(b["department"])),
                ("location", Number(b["location"])), ("lang", Str(b, "lang", 10))), ct));
            return;
        }
        mm = AlertPath().Match(path);
        if (m == "DELETE" && mm.Success)
        {
            CheckCsrf(ctx);
            var u = RequireUser(ctx);
            await Send(ctx, 200, await api.PostAsync("delete_job_alert", With(Who(u), ("id", long.Parse(mm.Groups[1].Value))), ct));
            return;
        }
        // The candidate's CV library: list, add (JSON with the file as base64, like applying), make default, delete, and
        // what a CV can fill in on the application form.
        if (Get() && path == "/api/cvs") { await Send(ctx, 200, await api.GetAsync("my_cvs", WhoQuery(RequireUser(ctx)), ct)); return; }
        if (Post() && path == "/api/cvs")
        {
            CheckCsrf(ctx);
            var u = RequireUser(ctx);
            Limit($"cvs:{ip}", RateLimiter.Apply);
            var b = await ReadJson(ctx, cfg.MaxBodyMb * 1024L * 1024);
            var file = FileFrom(b["cv"], out var name) ?? throw new PortalError(400, "cv_required");
            if (!CvExt().IsMatch(name)) throw new PortalError(400, "cv_type");
            if (file.Data.Length == 0) throw new PortalError(400, "cv_empty");
            await Send(ctx, 200, await api.PostFormAsync("upload_cv",
                [new("email", u.Email), new("epoch", u.Epoch.ToString()), new("default", IsTrue(b["default"]) ? "1" : "0")], "file", file, ct));
            return;
        }
        mm = CvPath().Match(path);
        if (mm.Success)
        {
            var u = RequireUser(ctx);
            var id = long.Parse(mm.Groups[1].Value);
            var action = mm.Groups[2].Success ? mm.Groups[2].Value : "";
            if (Get() && action == "prefill") { await Send(ctx, 200, await api.GetAsync("cv_prefill", WhoQuery(u, ("id", id)), ct)); return; }
            CheckCsrf(ctx);
            if (Post() && action == "default") { await Send(ctx, 200, await api.PostAsync("default_cv", With(Who(u), ("id", id)), ct)); return; }
            if (m == "DELETE" && action == "") { await Send(ctx, 200, await api.PostAsync("delete_cv", With(Who(u), ("id", id)), ct)); return; }
        }
        if (Post() && path == "/api/account/talent-pool")
        {
            CheckCsrf(ctx);
            var u = RequireUser(ctx);
            var b = await ReadJson(ctx, 4096);
            await Send(ctx, 200, await api.PostAsync("talent_pool", With(Who(u), ("join", IsTrue(b["join"]) ? "1" : "0")), ct));
            return;
        }
        mm = ApplicationActionPath().Match(path);
        if (Post() && mm.Success) { await ApplicationActionAsync(ctx, ip, mm.Groups[1].Value, mm.Groups[2].Value, ct); return; }
        if (Post() && path == "/api/account/delete")
        {
            CheckCsrf(ctx);
            var u = RequireUser(ctx);
            Limit($"delete:{ip}", RateLimiter.Correction);
            var b = await ReadJson(ctx, 16_384);
            await Send(ctx, 200, await api.PostAsync("request_deletion", With(Who(u), ("reason", Str(b, "reason", 2000))), ct));
            return;
        }
        throw new PortalError(404, "not_found");
    }

    async Task ApplyAsync(HttpContext ctx, string ip, CancellationToken ct)
    {
        CheckCsrf(ctx);
        var u = RequireUser(ctx);
        Limit($"apply:{ip}", RateLimiter.Apply);
        var b = await ReadJson(ctx, cfg.MaxBodyMb * 1024L * 1024);
        var cvNode = b["cv"] as JsonObject;
        var cvId = cvNode == null ? Int(b["cv_id"]) : null;
        var reuse = cvNode == null && (IsTrue(b["reuse_cv"]) || cvId != null);
        UploadFile? file = null;
        if (!reuse)
        {
            file = FileFrom(cvNode, out var name) ?? throw new PortalError(400, "cv_required");
            if (!CvExt().IsMatch(name)) throw new PortalError(400, "cv_type");
            if (file.Data.Length == 0) throw new PortalError(400, "cv_empty");
        }
        var screening = b["screening"] is JsonObject so ? so.ToJsonString() : "{}";
        if (screening.Length > 20000) screening = screening[..20000];
        var fields = new List<KeyValuePair<string, string>>
        {
            new("email", u.Email), new("epoch", u.Epoch.ToString()), new("job", Str(b, "job", 140)), new("phone", Str(b, "phone", 30)),
            new("country", Str(b, "country", 140)), new("nationality", Str(b, "nationality", 140)), new("cover_note", Str(b, "cover_note", 4000)),
            new("declaration", Truthy(b["declaration"]) ? "1" : "0"), new("talent_pool", IsTrue(b["talent_pool"]) ? "1" : "0"), new("lang", Str(b, "lang", 10)),
            new("screening", screening), new("reuse_cv", reuse && cvId == null ? "1" : "0"), new("cv_id", cvId?.ToString() ?? ""),
        };
        // No file: the API uses a saved CV (cv_id) or the one from the candidate's latest application (reuse_cv).
        await Send(ctx, 200, await api.PostFormAsync("apply", fields, "cv", file, ct));
    }

    async Task<bool> AssessmentAsync(HttpContext ctx, string id, string action, CancellationToken ct)
    {
        var m = ctx.Request.Method;
        var u = RequireUser(ctx);
        if (m == "GET" && action == "") { await Send(ctx, 200, await api.GetAsync("assessment", WhoQuery(u, ("name", id), ("lang", LangParam(ctx))), ct)); return true; }
        if (m == "GET" && action == "state") { await Send(ctx, 200, await api.GetAsync("assessment_state", WhoQuery(u, ("name", id), ("lang", LangParam(ctx))), ct)); return true; }
        if (m != "POST" || action == "") return false;
        CheckCsrf(ctx);
        Limit($"asm:{u.Email}", RateLimiter.AssessmentWrite);
        var b = await ReadJson(ctx, action == "file" ? 15 * 1024 * 1024 : 256 * 1024);
        var lang = Str(b, "lang", 10);
        var section = Int(b["section"]) ?? -1;
        switch (action)
        {
            case "start":
                await Send(ctx, 200, await api.PostAsync("start_assessment", With(Who(u), ("name", id), ("lang", lang)), ct));
                return true;
            case "answer":
                var answer = b["answer"]?.ToJsonString() ?? "null";
                await Send(ctx, 200, await api.PostAsync("save_assessment_answer", With(Who(u), ("name", id), ("key", Str(b, "key", 20)), ("section", section), ("answer", answer)), ct));
                return true;
            case "next":
                await Send(ctx, 200, await api.PostAsync("next_assessment_section", With(Who(u), ("name", id), ("section", section), ("lang", lang)), ct));
                return true;
            case "event":
                var kind = Str(b, "kind", 20) is "hidden" or "paste" ? Str(b, "kind", 20) : "";
                await Send(ctx, 200, await api.PostAsync("assessment_event", With(Who(u), ("name", id), ("kind", kind)), ct));
                return true;
            case "file":
                var file = FileFrom(b["file"], out _) ?? throw new PortalError(400, "file_required");
                if (file.Data.Length == 0) throw new PortalError(400, "file_required");
                if (file.Data.Length > 10 * 1024 * 1024) throw new PortalError(413, "file_too_large", new { max_mb = 10 });
                await Send(ctx, 200, await api.PostFormAsync("upload_assessment_file",
                    [new("email", u.Email), new("epoch", u.Epoch.ToString()), new("name", id), new("key", Str(b, "key", 20)), new("section", section.ToString())], "file", file, ct));
                return true;
        }
        return false;
    }

    async Task ApplicationActionAsync(HttpContext ctx, string ip, string name, string action, CancellationToken ct)
    {
        CheckCsrf(ctx);
        var u = RequireUser(ctx);
        switch (action)
        {
            case "offer":
            {
                Limit($"offer:{ip}", RateLimiter.Correction);
                var b = await ReadJson(ctx, 8192);
                var body = With(Who(u), ("name", name), ("accept", IsTrue(b["accept"]) ? "1" : "0"), ("reason", Str(b, "reason", 1000)));
                if (Str(b, "signature", 200) is { Length: > 0 } signature) body["signature"] = signature;
                await Send(ctx, 200, await api.PostAsync("respond_offer", body, ct));
                return;
            }
            case "withdraw" or "reschedule":
            {
                Limit($"selfservice:{ip}", RateLimiter.Correction);
                var b = await ReadJson(ctx, 8192);
                var body = With(Who(u), ("name", name), ("reason", Str(b, "reason", 1000)));
                await Send(ctx, 200, await api.PostAsync(action == "withdraw" ? "withdraw_application" : "reschedule_interview", body, ct));
                return;
            }
            case "book":
            {
                Limit($"book:{ip}", RateLimiter.Correction);
                var b = await ReadJson(ctx, 4096);
                await Send(ctx, 200, await api.PostAsync("book_interview", With(Who(u), ("name", name), ("slot", Int(b["slot"]) ?? -1)), ct));
                return;
            }
            default:
            {
                Limit($"correction:{ip}", RateLimiter.Correction);
                var b = await ReadJson(ctx, 16_384);
                await Send(ctx, 200, await api.PostAsync("add_correction", With(Who(u), ("name", name), ("note", Str(b, "note", 2000))), ct));
                return;
            }
        }
    }

    // ---------------------------------------------------------------- the candidate app

    async Task WebAsync(HttpContext ctx, string rawPath)
    {
        if (ctx.Request.Method is not ("GET" or "HEAD")) { await Send(ctx, 405, new { error = new { code = "bad_request", @params = new { } } }); return; }
        var path = rawPath;   // already decoded by the server
        if (path is "/" or "")
        {
            var lang = "ar";
            try { lang = Str((await SiteConfig("", ctx.RequestAborted))?["default_language"], 10) is { Length: > 0 } l ? l : "ar"; }
            catch (ApiError) { /* API unreachable: Arabic */ }
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Redirect($"/{lang}");
            return;
        }
        if (path.Contains('\0')) { await Send(ctx, 400, new { error = new { code = "bad_request", @params = new { } } }); return; }
        // URL paths use forward slashes on every system. A backslash or a colon (Windows separators, drive letters and
        // alternate streams) never appears in the app's own files, so such a path is never given to the file system.
        var safe = path.Contains('\\') || path.Contains(':') ? "/" : Normalize(path);
        if (safe.StartsWith("/assets/", StringComparison.Ordinal) && await ServeFile(ctx, safe, "public, max-age=31536000, immutable")) return;
        if (RootFile().IsMatch(safe) && await ServeFile(ctx, safe, "public, max-age=3600")) return;
        if (safe.StartsWith("/assets/", StringComparison.Ordinal)) { await Send(ctx, 404, new { error = new { code = "not_found", @params = new { } } }); return; }
        // Single page app: every other path is handled by the React router (/ar/..., /en/...).
        if (await ServeFile(ctx, "/index.html", "no-cache")) return;
        ctx.Response.StatusCode = 503;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.Response.WriteAsync("Portal web build not found. Build portal/web into wwwroot.");
    }

    /// <summary>A URL path with "." and ".." resolved and never above the root.</summary>
    static string Normalize(string path)
    {
        var stack = new List<string>();
        foreach (var seg in path.Split('/'))
        {
            if (seg is "" or ".") continue;
            if (seg == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }
            stack.Add(seg);
        }
        return "/" + string.Join('/', stack) + (path.EndsWith('/') && stack.Count > 0 ? "/" : "");
    }

    async Task<bool> ServeFile(HttpContext ctx, string urlPath, string cache)
    {
        var root = Path.GetFullPath(cfg.WebDist);
        var file = Path.GetFullPath(Path.Combine(root, urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        if (!file.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) return false;
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = Types.TryGetValue(Path.GetExtension(file), out var t) ? t : "application/octet-stream";
        ctx.Response.Headers.CacheControl = cache;
        if (ctx.Request.Method == "HEAD") return true;
        await ctx.Response.SendFileAsync(file, ctx.RequestAborted);
        return true;
    }
}
