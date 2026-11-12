using System.Text;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Common;

/// <summary>The email server in use: the one set in the backoffice, otherwise the SMTP_* environment settings.</summary>
public sealed class MailConfig(IAppDbContext db, ISecretProtector secrets, IMailEnvironment env)
{
    SmtpConfig? cached;
    bool loaded;

    public async Task<SmtpConfig?> CurrentAsync(CancellationToken ct = default)
    {
        if (loaded) return cached;
        var s = await db.EmailSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        cached = s is { Host.Length: > 0 }
            ? new SmtpConfig(s.Host, s.Port, s.UseTls, s.UserName, s.PasswordProtected.Length > 0 ? secrets.Unprotect(s.PasswordProtected) ?? "" : "",
                s.FromAddress, s.FromName.Length > 0 ? s.FromName : "Ministry of Economy & Tourism", "Settings")
            : env.FromEnvironment;
        loaded = true;
        return cached;
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) => await CurrentAsync(ct) != null;
}

/// <summary>
/// Queues candidate emails from the email templates (edited by HR in the backoffice), in the candidate's language:
/// Arabic uses the Arabic wording and every other language the English wording.
/// </summary>
public sealed class PortalMailer(IAppDbContext db, ContentService content, IClock clock, MailConfig mail, TextService texts)
{
    public Task<bool> CanSendAsync(CancellationToken ct = default) => mail.IsConfiguredAsync(ct);

    public async Task<string> LinkAsync(string lang, string path, CancellationToken ct = default)
    {
        var s = await content.SettingsAsync(ct);
        var b = s.PortalUrl.TrimEnd('/');
        return b.Length == 0 ? "" : $"{b}/{lang}{path}";
    }

    public async Task<string> SignatureAsync(string lang, CancellationToken ct = default)
    {
        var s = await db.EmailSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var sig = s == null ? "" : lang == "ar" ? s.SignatureAr : s.SignatureEn;
        return sig.Length > 0 ? sig : (await content.PortalTextAsync(lang, ct))["email.signature"];
    }

    /// <summary>
    /// Adds the event's email to the outbox (sent by the background worker). False when email is not set up or the
    /// template is switched off. The values fill the template's placeholders; {name} and {link} are always there.
    /// </summary>
    public async Task<bool> QueueAsync(string to, string name, string lang, string eventKey, string link,
        IDictionary<string, string>? values = null, CancellationToken ct = default)
    {
        var template = await db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Event == eventKey, ct);
        if (template is { IsEnabled: false }) return false;
        // A text message (SMS or WhatsApp) goes too when it is set up for this event, with or without an email server.
        if (template != null)
        {
            var (sub, _) = template.In(lang == "ar" ? "ar" : "en");
            var filled = new Dictionary<string, string> { ["name"] = name, ["link"] = link };
            foreach (var (k, v) in values ?? new Dictionary<string, string>()) filled[k] = v;
            await texts.QueueAsync(to, eventKey, TextUtil.NoEmDash(EmailTemplates.Fill(sub, filled)), link, ct);
        }
        if (!await mail.IsConfiguredAsync(ct)) return false;
        string subject, body;
        if (template != null) (subject, body) = template.In(lang == "ar" ? "ar" : "en");
        else
        {
            // Before the templates existed the wording lived in the portal text; it remains the fallback.
            var text = await content.PortalTextAsync(lang, ct);
            if (!text.TryGetValue($"email.{eventKey}_subject", out var s) || !text.TryGetValue($"email.{eventKey}_body", out var b)) return false;
            (subject, body) = (s, b);
        }
        var all = new Dictionary<string, string> { ["name"] = name, ["link"] = link };
        foreach (var (k, v) in values ?? new Dictionary<string, string>()) all[k] = v;
        subject = TextUtil.NoEmDash(EmailTemplates.Fill(subject, all));
        var (htmlPart, textPart) = Compose(body, template?.HtmlIn(lang == "ar" ? "ar" : "en"), all, subject, link, await SignatureAsync(lang, ct), TextUtil.IsRtl(lang));
        db.EmailMessages.Add(new EmailMessage
        {
            ToAddress = to, Subject = TextUtil.Clip(subject, 300), HtmlBody = htmlPart, TextBody = textPart,
            Status = EmailStatus.Queued, NextAttemptAt = clock.UtcNow, CreatedAt = clock.UtcNow,
        });
        return true;
    }

    /// <summary>
    /// Both parts of an email from a template: the plain text (filled, with the signature) and the HTML (the designed HTML
    /// version filled with escaped values when there is one, otherwise made from the plain text).
    /// </summary>
    public static (string Html, string Text) Compose(string textBody, string? htmlBody, IReadOnlyDictionary<string, string> values, string subject,
        string link, string signature, bool rtl)
    {
        var text = TextUtil.NoEmDash(EmailTemplates.Fill(textBody, values)).Replace("\r", "");
        var plain = text.Replace("\n# ", "\n").TrimStart('#', ' ') + (signature.Length > 0 ? "\n\n" + signature : "");
        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            if (values.TryGetValue("offer_letter_html", out var letterHtml) && letterHtml.Length > 0 && textBody.Contains("{offer_letter}"))
            {
                // A plain-text email that carries a letter with its own HTML version: the HTML part shows the designed letter.
                var others = values.Where(x => x.Key != "offer_letter").ToDictionary(x => x.Key, x => x.Value);
                var inner = Paragraphs(TextUtil.NoEmDash(EmailTemplates.Fill(textBody, others)), subject, link);
                inner = System.Text.RegularExpressions.Regex.Replace(inner, @"<p[^>]*>\{offer_letter\}</p>", letterHtml.Replace("$", "$$"));
                return (Wrap(inner, signature, rtl), plain);
            }
            return (Html(text, subject, link, signature, rtl), plain);
        }
        var html = htmlBody;
        foreach (var (k, v) in values)
        {
            var token = "{" + k + "}";
            if (!html.Contains(token)) continue;
            if (k == "link")
            {
                html = html.Replace($"<p>{token}</p>", v.Length == 0 ? "" : Button(v, subject)).Replace(token, TextUtil.Esc(v));
            }
            else if (k == "offer_letter")
            {
                // The letter's own HTML version when it has one (passed as offer_letter_html), otherwise its text as paragraphs.
                var letter = values.TryGetValue("offer_letter_html", out var lh) && lh.Length > 0 ? lh : Paragraphs(v, subject, "");
                html = html.Replace($"<p>{token}</p>", letter).Replace(token, letter);
            }
            else if (k == "offer_letter_html") continue;
            else html = html.Replace(token, TextUtil.Esc(TextUtil.NoEmDash(v)).Replace("\n", "<br>"));
        }
        return (Wrap(html, signature, rtl), plain);
    }

    static string Button(string link, string subject) =>
        $"<p style=\"margin:0 0 16px\"><a href=\"{TextUtil.Esc(link)}\" style=\"display:inline-block;background:#92722a;color:#ffffff;padding:12px 20px;border-radius:8px;text-decoration:none;font-weight:bold\">{TextUtil.Esc(subject)}</a></p>"
        + $"<p style=\"margin:0 0 16px;font-size:12px;color:#555;word-break:break-all\">{TextUtil.Esc(link)}</p>";

    public static string Wrap(string inner, string signature, bool rtl) =>
        $"<div dir=\"{(rtl ? "rtl" : "ltr")}\" style=\"font-family:Arial,sans-serif;font-size:15px;line-height:1.6;color:#232528;max-width:560px\">{inner}"
        + (signature.Length > 0 ? $"<p style=\"margin:24px 0 0;color:#555\">{TextUtil.Esc(signature)}</p>" : "") + "</div>";

    /// <summary>
    /// The email HTML: one paragraph per blank-line-separated block, a button for a block that is just the link, and a
    /// heading for a line starting with "# " (offer letters).
    /// </summary>
    public static string Html(string body, string subject, string link, string signature, bool rtl) => Wrap(Paragraphs(body, subject, link), signature, rtl);

    public static string Paragraphs(string body, string subject, string link)
    {
        var sb = new StringBuilder();
        foreach (var para in body.Replace("\r", "").Split("\n\n"))
        {
            if (para.Trim().Length == 0) continue;
            if (link.Length > 0 && para.Trim() == link) sb.Append(Button(link, subject));
            else if (para.TrimStart().StartsWith("# ")) sb.Append($"<h2 style=\"margin:0 0 16px;font-size:20px\">{TextUtil.Esc(para.TrimStart()[2..].Trim())}</h2>");
            else sb.Append($"<p style=\"margin:0 0 16px\">{TextUtil.Esc(para).Replace("\n", "<br>")}</p>");
        }
        return sb.ToString();
    }
}
