using System.Text;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Common;

/// <summary>Queues candidate emails built from portal wording (so HR can edit them), in the candidate's language.</summary>
public sealed class PortalMailer(IAppDbContext db, ContentService content, IClock clock, IMailStatus mail)
{
    public bool CanSend => mail.IsConfigured;

    public async Task<string> LinkAsync(string lang, string path, CancellationToken ct = default)
    {
        var s = await content.SettingsAsync(ct);
        var b = s.PortalUrl.TrimEnd('/');
        return b.Length == 0 ? "" : $"{b}/{lang}{path}";
    }

    /// <summary>Adds the email to the outbox (sent by the background worker). False when email is not set up.</summary>
    public async Task<bool> QueueAsync(string to, string name, string lang, string subjectKey, string bodyKey, string link,
        IDictionary<string, string>? values = null, CancellationToken ct = default)
    {
        if (!mail.IsConfigured) return false;
        var text = await content.PortalTextAsync(lang, ct);
        var all = new Dictionary<string, string> { ["name"] = name, ["link"] = link };
        foreach (var (k, v) in values ?? new Dictionary<string, string>()) all[k] = v;
        var subject = Fill(text[subjectKey], all);
        var body = Fill(text[bodyKey], all);
        db.EmailMessages.Add(new EmailMessage
        {
            ToAddress = to, Subject = TextUtil.Clip(subject, 300), HtmlBody = Html(body, subject, link, text["email.signature"], TextUtil.IsRtl(lang)),
            Status = EmailStatus.Queued, NextAttemptAt = clock.UtcNow, CreatedAt = clock.UtcNow,
        });
        return true;
    }

    static string Fill(string s, IDictionary<string, string> values)
    {
        foreach (var (k, v) in values) s = s.Replace("{" + k + "}", v);
        return s;
    }

    internal static string Html(string body, string subject, string link, string signature, bool rtl)
    {
        var sb = new StringBuilder();
        sb.Append($"<div dir=\"{(rtl ? "rtl" : "ltr")}\" style=\"font-family:Arial,sans-serif;font-size:15px;line-height:1.6;color:#232528;max-width:560px\">");
        foreach (var para in body.Replace("\r", "").Split("\n\n"))
        {
            if (link.Length > 0 && para.Trim() == link)
            {
                sb.Append($"<p style=\"margin:0 0 16px\"><a href=\"{TextUtil.Esc(link)}\" style=\"display:inline-block;background:#92722a;color:#ffffff;padding:12px 20px;border-radius:8px;text-decoration:none;font-weight:bold\">{TextUtil.Esc(subject)}</a></p>");
                sb.Append($"<p style=\"margin:0 0 16px;font-size:12px;color:#555;word-break:break-all\">{TextUtil.Esc(link)}</p>");
            }
            else
            {
                sb.Append($"<p style=\"margin:0 0 16px\">{TextUtil.Esc(para).Replace("\n", "<br>")}</p>");
            }
        }
        sb.Append($"<p style=\"margin:24px 0 0;color:#555\">{TextUtil.Esc(signature)}</p></div>");
        return sb.ToString();
    }
}
