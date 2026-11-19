using System.Net;
using Moet.Hrms.Api.Endpoints;

namespace Moet.Hrms.Api.Hosting;

/// <summary>
/// Narrows who can use what, for a backoffice on the internal network that the careers portal (in the DMZ) reaches:
/// <list type="bullet">
/// <item>PORTAL_ONLY_PORTS: ports (bindings) on which only the portal API (/api/portal) and /healthz answer, for
/// example the HTTPS port the firewall opens to the portal server, and the local port the portal uses on the same
/// server. Everything else on those ports is "not found".</item>
/// <item>PORTAL_API_ALLOWED_IPS: addresses or ranges (10.20.0.5, 10.20.0.0/24) that may call the portal API at all,
/// even with the key. Empty: any address with the key.</item>
/// </list>
/// Both are off unless set. The staff app and its API are unaffected on the other ports.
/// </summary>
public sealed class PortalAccess(RequestDelegate next, IConfiguration cfg, ILogger<PortalAccess> log)
{
    readonly HashSet<int> portalPorts = (cfg["PORTAL_ONLY_PORTS"] ?? "").Split(',', ';', ' ')
        .Select(p => int.TryParse(p.Trim(), out var n) ? n : 0).Where(n => n > 0).ToHashSet();
    readonly List<IPNetwork> allowed = Parse(cfg["PORTAL_API_ALLOWED_IPS"], log);

    internal static List<IPNetwork> Parse(string? value, ILogger? log = null)
    {
        var list = new List<IPNetwork>();
        foreach (var raw in (value ?? "").Split(',', ';', ' ').Select(v => v.Trim()).Where(v => v.Length > 0))
        {
            if (IPNetwork.TryParse(raw, out var net)) list.Add(net);
            else if (IPAddress.TryParse(raw, out var ip)) list.Add(new IPNetwork(ip, ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128));
            else log?.LogWarning("PORTAL_API_ALLOWED_IPS: '{Value}' is not an address or range; ignored", raw);
        }
        return list;
    }

    internal static bool Allowed(IReadOnlyList<IPNetwork> allowed, IPAddress? remote)
    {
        if (allowed.Count == 0) return true;
        if (remote == null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        return allowed.Any(n => n.Contains(remote));
    }

    public Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path;
        var portalApi = path.StartsWithSegments("/api/portal");
        if (portalPorts.Count > 0 && portalPorts.Contains(ctx.Connection.LocalPort) && !portalApi && !path.StartsWithSegments("/healthz"))
            return NotFoundEnvelope.Handle(ctx);
        if (portalApi && !Allowed(allowed, ctx.Connection.RemoteIpAddress))
        {
            log.LogWarning("Portal API call refused from {Address} (not in PORTAL_API_ALLOWED_IPS)", ctx.Connection.RemoteIpAddress);
            ctx.Response.StatusCode = 403;
            return ctx.Response.WriteAsJsonAsync(Envelope.Error("forbidden", null), Json.Options);
        }
        return next(ctx);
    }
}
