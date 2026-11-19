using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Moet.Careers.Portal;

/// <summary>A signed-in candidate, as the session cookie carries them.</summary>
public sealed record PortalUser(string Email, string FullName, long Epoch);

/// <summary>
/// Stateless signed session cookie: base64url(JSON) + "." + HMAC-SHA256. Holds only the candidate's email, name and
/// session version (a password reset changes the version and signs out older sessions). The same format as the
/// earlier Node server, so sessions carry over when it is replaced.
/// </summary>
public static class Session
{
    public const string Cookie = "mc_session";

    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static byte[] FromB64(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(t + new string('=', (4 - t.Length % 4) % 4));
    }

    static string Sign(string data, string secret) => B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data)));

    public static string CreateToken(PortalUser user, string secret, double hours, DateTimeOffset? now = null)
    {
        var expires = (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds() + (long)(hours * 3_600_000);
        var json = JsonSerializer.Serialize(new { e = user.Email, n = user.FullName, v = user.Epoch, x = expires });
        var data = B64(Encoding.UTF8.GetBytes(json));
        return $"{data}.{Sign(data, secret)}";
    }

    public static PortalUser? ReadToken(string? token, string secret, DateTimeOffset? now = null)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 2048) return null;
        var parts = token.Split('.');
        if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) return null;
        var expected = Encoding.ASCII.GetBytes(Sign(parts[0], secret));
        var given = Encoding.ASCII.GetBytes(parts[1]);
        if (expected.Length != given.Length || !CryptographicOperations.FixedTimeEquals(expected, given)) return null;
        try
        {
            using var doc = JsonDocument.Parse(FromB64(parts[0]));
            var r = doc.RootElement;
            var email = r.TryGetProperty("e", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
            if (email.Length == 0 || !r.TryGetProperty("x", out var x) || x.ValueKind != JsonValueKind.Number) return null;
            if (x.GetDouble() < (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds()) return null;
            var name = r.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
            var epoch = r.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : 0;
            return new PortalUser(email, name, epoch);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException) { return null; }
    }

    public static string? FromRequest(HttpRequest req)
    {
        foreach (var part in req.Headers.Cookie.ToString().Split(';'))
        {
            var i = part.IndexOf('=');
            if (i > 0 && part[..i].Trim() == Cookie) return Uri.UnescapeDataString(part[(i + 1)..].Trim());
        }
        return null;
    }

    public static string CookieHeader(string value, bool secure, long maxAgeSec) =>
        $"{Cookie}={Uri.EscapeDataString(value)}; Path=/; HttpOnly; SameSite=Lax; Max-Age={maxAgeSec}" + (secure ? "; Secure" : "");
}
