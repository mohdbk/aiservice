using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Moet.Recruitment.Domain.Common;

/// <summary>Small text helpers shared by every module (pure, unit-tested).</summary>
public static partial class TextUtil
{
    static readonly HashSet<string> RtlLangs = ["ar", "he", "fa", "ur", "ps", "ckb", "ku", "dv", "yi", "sd", "ug"];

    [GeneratedRegex(@"^[a-z]{2,3}(-[A-Za-z]{2,4})?$")] private static partial Regex LangRe();
    [GeneratedRegex(@"^\+?[0-9][0-9 ()-]{6,19}$")] private static partial Regex PhoneRe();
    [GeneratedRegex(@"^[^@\s]{1,64}@[^@\s]{1,255}\.[^@\s]{2,63}$")] private static partial Regex EmailRe();
    [GeneratedRegex(@"<(br|/p|/li|/h[1-6]|/div|/tr)[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex BlockEndRe();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex TagRe();
    [GeneratedRegex(@"\s+")] private static partial Regex SpaceRe();
    [GeneratedRegex(@"\p{IsArabic}")] private static partial Regex ArabicLetterRe();
    [GeneratedRegex(@"\p{L}")] private static partial Regex LetterRe();

    public const string EmDash = "\u2014";

    public static bool IsRtl(string? lang) => RtlLangs.Contains((lang ?? "").Split('-')[0].ToLowerInvariant());

    public static string Dir(string? lang) => IsRtl(lang) ? "rtl" : "ltr";

    public static bool ValidLang(string? lang) => LangRe().IsMatch(lang ?? "");

    public static bool ValidPhone(string? phone) => PhoneRe().IsMatch((phone ?? "").Trim());

    public static bool ValidEmail(string? email) => EmailRe().IsMatch(email ?? "") && (email ?? "").Length <= 254;

    public static string NormEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>Stable, human-friendly application reference for support calls.</summary>
    public static string Reference(string code, DateTime created)
    {
        var h = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(code ?? ""))).ToUpperInvariant();
        return $"MOET-{created.Year:D4}-{h[..6]}";
    }

    /// <summary>Short hash of several parts, used to notice that a suggestion was edited.</summary>
    public static string ContentHash(params object?[] parts)
    {
        var joined = string.Join("\u001f", parts.Select(p => p?.ToString() ?? ""));
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(joined))).ToLowerInvariant()[..16];
    }

    public static string Sha256Hex(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    public static string StripHtml(string? html)
    {
        var text = BlockEndRe().Replace(html ?? "", " ");
        text = TagRe().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        return SpaceRe().Replace(text, " ").Trim();
    }

    /// <summary>Plain-text teaser from HTML, cut on a word boundary.</summary>
    public static string Summary(string? html, int limit = 240)
    {
        var text = StripHtml(html);
        if (text.Length <= limit) return text;
        var cut = text[..limit];
        var sp = cut.LastIndexOf(' ');
        if (sp > 0) cut = cut[..sp];
        return cut.TrimEnd(',', ';', ':', '.') + "…";
    }

    /// <summary>Share of letters that are Arabic script (0 to 1).</summary>
    public static double ArabicRatio(string? text)
    {
        var s = text ?? "";
        var letters = LetterRe().Matches(s).Count;
        if (letters == 0) return 0;
        return ArabicLetterRe().Matches(s).Count / (double)letters;
    }

    /// <summary>"ar" when more than 30% of the letters are Arabic script, otherwise "en".</summary>
    public static string DetectLanguage(params string?[] texts) =>
        ArabicRatio(string.Join(" ", texts.Select(StripHtml))) > 0.3 ? "ar" : "en";

    /// <summary>A same-site path to return to after signing in, or "".</summary>
    public static string SafeNext(string? path)
    {
        var p = (path ?? "").Trim();
        return p.StartsWith('/') && !p.StartsWith("//") && !p.Contains('\\') && p.Length < 300 ? p : "";
    }

    /// <summary>House style: no em dashes in anything we write.</summary>
    public static string NoEmDash(string? s) => (s ?? "").Replace(" " + EmDash + " ", ", ").Replace(EmDash, ", ");

    public static string Clip(string? s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);

    public static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public static List<string> Lines(string? s) =>
        (s ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    public static string Esc(string? s) => WebUtility.HtmlEncode(s ?? "");
}
