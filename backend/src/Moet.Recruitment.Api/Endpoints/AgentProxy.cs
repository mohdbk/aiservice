using Microsoft.AspNetCore.Http.Features;
using Moet.Recruitment.Application.Common;
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

    public static async Task RunAsync(HttpContext ctx, IHttpClientFactory factory, IConfiguration cfg, ContentService content, CancellationToken ct)
    {
        var baseUrl = cfg["AGENT_URL"];
        if (string.IsNullOrWhiteSpace(baseUrl) || !(await content.SettingsAsync(ct)).AssistantEnabled)
            throw DomainException.Forbidden("assistant_off");

        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/agent");
        req.Content = new StreamContent(ctx.Request.Body);
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        req.Headers.Accept.ParseAdd("text/event-stream");
        req.Headers.TryAddWithoutValidation("Cookie", ctx.Request.Headers.Cookie.ToString());
        req.Headers.TryAddWithoutValidation("X-Agent-Key", cfg["AGENT_KEY"] ?? "");
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
