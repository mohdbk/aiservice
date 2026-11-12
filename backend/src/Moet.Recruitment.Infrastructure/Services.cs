using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ganss.Xss;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Infrastructure.Persistence;

namespace Moet.Recruitment.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public TimeZoneInfo Zone { get; } = FindZone(Environment.GetEnvironmentVariable("TIME_ZONE") ?? "Asia/Dubai");

    static TimeZoneInfo FindZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception) { return TimeZoneInfo.CreateCustomTimeZone("UAE", TimeSpan.FromHours(4), "UAE", "UAE"); }
    }
}

/// <summary>The staff member signed in to the backoffice, the applicant (via the portal key), or the system.</summary>
public sealed class HttpRequestLanguage(IHttpContextAccessor http) : IRequestLanguage
{
    public bool English => http.HttpContext?.Request.Headers["X-Lang"].ToString() == "en";
}

public sealed class HttpCurrentActor(IHttpContextAccessor http) : ICurrentActor
{
    public const string ApplicantHeader = "X-Applicant";

    public ActorType Type
    {
        get
        {
            var ctx = http.HttpContext;
            if (ctx?.User.Identity?.IsAuthenticated == true) return ActorType.Staff;
            return ctx?.Items.ContainsKey(ApplicantHeader) == true ? ActorType.Applicant : ActorType.System;
        }
    }

    public int? Id => Type == ActorType.Staff && int.TryParse(http.HttpContext!.User.FindFirst("sid")?.Value, out var id) ? id : null;

    public string Name => Type switch
    {
        ActorType.Staff => http.HttpContext!.User.Identity!.Name ?? "",
        ActorType.Applicant => http.HttpContext!.Items[ApplicantHeader] as string ?? "",
        _ => "System",
    };
}

/// <summary>PBKDF2-SHA256 with 210,000 iterations and a random salt (OWASP 2023 guidance).</summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    const int Iterations = 210_000;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool Verify(string hash, string password)
    {
        var parts = (hash ?? "").Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var it))
        {
            // Spend the same time as a real check, so timing does not reveal unknown accounts.
            Rfc2898DeriveBytes.Pbkdf2(password, new byte[16], Iterations, HashAlgorithmName.SHA256, 32);
            return false;
        }
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, it, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(key, expected);
    }
}

public sealed class SqlCodeGenerator(AppDbContext db, IClock clock) : ICodeGenerator
{
    public async Task<string> NextAsync(CodeKind kind, CancellationToken ct = default)
    {
        var (seq, prefix, width, yearly) = kind switch
        {
            CodeKind.JobOpening => ("hiring.JobOpeningCode", "JOB", 4, true),
            CodeKind.JobApplication => ("hiring.JobApplicationCode", "APP", 5, true),
            CodeKind.Interview => ("hiring.InterviewCode", "INT", 5, true),
            CodeKind.JobOffer => ("hiring.JobOfferCode", "OFR", 4, true),
            _ => ("assessment.InvitationCode", "ASM", 5, false),
        };
        // Not composed (SingleAsync would wrap it in a subquery, where NEXT VALUE FOR is not allowed).
        var n = (await db.Database.SqlQueryRaw<int>($"SELECT NEXT VALUE FOR {seq} AS [Value]").ToListAsync(ct))[0];
        var num = n.ToString().PadLeft(width, '0');
        return yearly ? $"{prefix}-{clock.Local(clock.UtcNow).Year}-{num}" : $"{prefix}-{num}";
    }
}

/// <summary>Wording that ships with the product (Content/Text/*.json).</summary>
public sealed class EmbeddedTextCatalog : ITextCatalog
{
    readonly Dictionary<string, IReadOnlyDictionary<string, string>> _byLang = Load();

    static Dictionary<string, IReadOnlyDictionary<string, string>> Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        var o = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var n in asm.GetManifestResourceNames().Where(n => n.Contains(".Content.Text.") && n.EndsWith(".json")))
        {
            using var s = asm.GetManifestResourceStream(n)!;
            var lang = n.Split('.')[^2];
            o[lang] = JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? [];
        }
        return o;
    }

    public IReadOnlyDictionary<string, string> Shipped(string lang) =>
        _byLang.TryGetValue(lang, out var d) ? d : new Dictionary<string, string>();
}

public sealed class HtmlCleaner : IHtmlCleaner
{
    readonly HtmlSanitizer _s = Build();

    static HtmlSanitizer Build()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var t in new[] { "p", "br", "strong", "b", "em", "i", "u", "ul", "ol", "li", "h2", "h3", "h4", "a", "blockquote", "span", "div", "table", "thead", "tbody", "tr", "th", "td" })
            s.AllowedTags.Add(t);
        s.AllowedAttributes.Clear();
        foreach (var a in new[] { "href", "dir", "lang", "title" }) s.AllowedAttributes.Add(a);
        s.AllowedSchemes.Clear();
        foreach (var sc in new[] { "https", "mailto" }) s.AllowedSchemes.Add(sc);
        s.AllowedCssProperties.Clear();
        return s;
    }

    public string Clean(string? html) => _s.Sanitize(html ?? "");
}

public sealed class EnvironmentMail(IConfiguration cfg) : IMailEnvironment
{
    public SmtpConfig? FromEnvironment => string.IsNullOrWhiteSpace(cfg["SMTP_HOST"]) ? null : new SmtpConfig(
        cfg["SMTP_HOST"]!, int.TryParse(cfg["SMTP_PORT"], out var p) ? p : 587, !string.Equals(cfg["SMTP_TLS"], "0", StringComparison.Ordinal),
        cfg["SMTP_USER"] ?? "", cfg["SMTP_PASSWORD"] ?? "", cfg["SMTP_FROM"] ?? "careers@moet.gov.ae", cfg["SMTP_FROM_NAME"] ?? "Ministry of Economy & Tourism",
        "Environment");
}

public sealed class SmtpMailSender : IMailSender
{
    /// <summary>multipart/alternative: plain text first, then HTML (mail clients show the richest part they support).</summary>
    public async Task SendAsync(SmtpConfig c, string to, string subject, string html, string text, CancellationToken ct = default)
    {
        using var smtp = new System.Net.Mail.SmtpClient(c.Host, c.Port)
        {
            EnableSsl = c.UseTls,
            Credentials = c.UserName.Length == 0 ? null : new System.Net.NetworkCredential(c.UserName, c.Password),
            Timeout = 30_000,
        };
        using var msg = new System.Net.Mail.MailMessage { From = new System.Net.Mail.MailAddress(c.FromAddress, c.FromName), Subject = subject };
        if (text.Length > 0)
        {
            msg.Body = text;
            msg.BodyEncoding = System.Text.Encoding.UTF8;
            msg.AlternateViews.Add(System.Net.Mail.AlternateView.CreateAlternateViewFromString(html, System.Text.Encoding.UTF8, "text/html"));
        }
        else
        {
            msg.Body = html;
            msg.IsBodyHtml = true;
        }
        msg.SubjectEncoding = System.Text.Encoding.UTF8;
        msg.To.Add(to);
        await smtp.SendMailAsync(msg, ct);
    }
}

/// <summary>AES-GCM with a key derived from AGENT_KEY (the secret the API and the agent service already share).</summary>
public sealed class AesSecretProtector(IConfiguration cfg) : ISecretProtector
{
    byte[]? Key => (cfg["AGENT_KEY"] ?? "") is { Length: >= 16 } k ? SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("moet-recruitment-secrets|" + k)) : null;

    public bool IsConfigured => Key != null;

    public string Protect(string plain)
    {
        var key = Key ?? throw new Domain.Common.DomainException("agent_key_missing", 409);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = System.Text.Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, data, cipher, tag);
        return "v1." + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string? Unprotect(string protectedValue)
    {
        var key = Key;
        if (key == null || !protectedValue.StartsWith("v1.")) return null;
        try
        {
            var all = Convert.FromBase64String(protectedValue[3..]);
            var plain = new byte[all.Length - 28];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(all.AsSpan(0, 12), all.AsSpan(28), all.AsSpan(12, 16), plain);
            return System.Text.Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException) { return null; }
    }
}
