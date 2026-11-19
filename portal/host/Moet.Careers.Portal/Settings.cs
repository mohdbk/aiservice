namespace Moet.Careers.Portal;

/// <summary>
/// The portal's settings, all from the environment (or web.config on IIS). Listed in docs/ON_PREM.md, "Careers portal
/// settings".
/// </summary>
public sealed record PortalSettings
{
    /// <summary>The HRMS API as this server reaches it (HRMS_API_URL; RECRUITMENT_API_URL is still read).</summary>
    public string ApiUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string ApiSecret { get; init; } = "";
    public string SessionSecret { get; init; } = "";
    public double SessionHours { get; init; } = 8;
    public bool CookieSecure { get; init; } = true;
    /// <summary>Take the client address from a proxy header (when a reverse proxy sits in front).</summary>
    public bool TrustProxy { get; init; }
    public string ClientIpHeader { get; init; } = "x-forwarded-for";
    /// <summary>"first" or "last" entry of that header ("last" when the proxy in front appends the address it saw).</summary>
    public bool ClientIpFromLast { get; init; }
    /// <summary>The built candidate app (index.html and assets).</summary>
    public string WebDist { get; init; } = "";
    public int MaxBodyMb { get; init; } = 30;
    public int ApiTimeoutMs { get; init; } = 30000;

    public static PortalSettings FromConfiguration(IConfiguration c, string contentRoot)
    {
        static bool Bool(string? v, bool d) => string.IsNullOrEmpty(v) ? d : !new[] { "0", "false", "no", "off" }.Contains(v.Trim().ToLowerInvariant());
        static int Int(string? v, int d) => int.TryParse(v, out var n) && n > 0 ? n : d;
        var s = new PortalSettings
        {
            ApiUrl = (c["HRMS_API_URL"] is { Length: > 0 } u ? u : c["RECRUITMENT_API_URL"] ?? "").TrimEnd('/'),
            ApiKey = c["CAREERS_PORTAL_API_KEY"] ?? "",
            ApiSecret = c["CAREERS_PORTAL_API_SECRET"] ?? "",
            SessionSecret = c["SESSION_SECRET"] ?? "",
            SessionHours = double.TryParse(c["SESSION_HOURS"], System.Globalization.CultureInfo.InvariantCulture, out var h) && h > 0 ? h : 8,
            CookieSecure = Bool(c["COOKIE_SECURE"], true),
            TrustProxy = Bool(c["TRUST_PROXY"], false),
            ClientIpHeader = (c["CLIENT_IP_HEADER"] is { Length: > 0 } hdr ? hdr : "x-forwarded-for").ToLowerInvariant(),
            ClientIpFromLast = string.Equals(c["CLIENT_IP_FROM"], "last", StringComparison.OrdinalIgnoreCase),
            WebDist = c["WEB_DIST"] is { Length: > 0 } dist ? dist : Path.Combine(contentRoot, "wwwroot"),
            MaxBodyMb = Int(c["MAX_BODY_MB"], 30),
            ApiTimeoutMs = Int(c["API_TIMEOUT_MS"], Int(c["FRAPPE_TIMEOUT_MS"], 30000)),
        };
        var missing = new[] { (s.ApiUrl, "HRMS_API_URL"), (s.ApiKey, "CAREERS_PORTAL_API_KEY"), (s.ApiSecret, "CAREERS_PORTAL_API_SECRET") }
            .Where(x => x.Item1.Length == 0).Select(x => x.Item2).ToList();
        if (missing.Count > 0) throw new InvalidOperationException("Missing settings: " + string.Join(", ", missing));
        if (s.SessionSecret.Length < 32) throw new InvalidOperationException("SESSION_SECRET must be at least 32 characters");
        return s;
    }
}
