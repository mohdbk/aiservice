using Microsoft.AspNetCore.Http.Features;
using Moet.Recruitment.Application.Ai;
using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Api.Endpoints;

/// <summary>
/// Forwards an AG-UI run from the backoffice to the recruitment agent service and streams its events back.
/// The browser keeps one origin; the agent receives the staff member's own session so every tool call it makes
/// against this API runs with that person's permissions. The shared AGENT_KEY stops anyone else calling the agent.
/// </summary>
public static class AgentProxy
{
    public const string Client = "agent";

    /// <summary>
    /// Endpoints only the agent service may call (X-Agent-Key): its settings, including the model key, read on each run.
    /// </summary>
    public static void MapInternal(WebApplication app)
    {
        var cfg = app.Configuration;
        app.MapGet("/api/internal/agent/settings", async Task<IResult> (HttpContext ctx, AgentSettingsService s, CancellationToken ct) =>
        {
            var expected = System.Text.Encoding.UTF8.GetBytes(cfg["AGENT_KEY"] ?? "");
            var given = System.Text.Encoding.UTF8.GetBytes(ctx.Request.Headers["X-Agent-Key"].ToString());
            if (expected.Length < 16 || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, given))
                return Results.Json(new { error = new { code = "unauthorised" } }, statusCode: 401);
            return Results.Ok(await s.ForAgentAsync(ct));
        });
    }

    public static async Task RunAsync(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, AgentSettingsService settings, CancellationToken ct)
    {
        var baseUrl = cfg["AGENT_URL"];
        if (string.IsNullOrWhiteSpace(baseUrl)) throw DomainException.Forbidden("assistant_off");
        await settings.StartRunAsync(ct);

        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/agent");
        req.Content = new StreamContent(ctx.Request.Body);
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        req.Headers.Accept.ParseAdd("text/event-stream");
        req.Headers.TryAddWithoutValidation("Cookie", ctx.Request.Headers.Cookie.ToString());
        req.Headers.TryAddWithoutValidation("X-Agent-Key", cfg["AGENT_KEY"] ?? "");
        // The screen the staff member has open (page, record), so "this applicant" can be resolved by the agent.
        var screen = ctx.Request.Headers["X-Agent-Context"].ToString();
        if (screen.Length is > 0 and <= 4000) req.Headers.TryAddWithoutValidation("X-Agent-Context", screen);
        // The person's role, so the assistant only offers the tools they may use (the API still checks every call).
        req.Headers.TryAddWithoutValidation("X-Agent-Role", ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "");
        req.Headers.TryAddWithoutValidation("X-Lang", ctx.Request.Headers["X-Lang"].ToString() == "en" ? "en" : "ar");

        HttpResponseMessage res;
        try
        {
            res = await factory.CreateClient(Client).SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException)
        {
            throw new DomainException("agent_unavailable", 503);
        }
        using (res)
        {
            ctx.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            ctx.Response.StatusCode = (int)res.StatusCode;
            ctx.Response.ContentType = res.Content.Headers.ContentType?.ToString() ?? "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
            await using var body = await res.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[8192];
            int n;
            while ((n = await body.ReadAsync(buffer, ct)) > 0)
            {
                await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, n), ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
        }
    }
}
