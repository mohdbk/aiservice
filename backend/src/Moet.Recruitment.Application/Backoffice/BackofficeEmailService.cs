using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record EmailSettingInput(string? Host, int Port, bool UseTls, string? UserName, string? Password, bool ClearPassword,
    string? FromAddress, string? FromName, string? SignatureAr, string? SignatureEn, bool? AttachOfferPdf = null);
public sealed record TestEmailInput(string? To, string? Lang);
public sealed record EmailTemplateInput(string? Key, string? NameAr, string? NameEn, string? SubjectAr, string? BodyAr, string? SubjectEn, string? BodyEn,
    bool IsEnabled, string? RowVersion, string? BodyHtmlAr = null, string? BodyHtmlEn = null);

/// <summary>The email server (administrators), the email templates (recruitment admins) and the email log.</summary>
public sealed class BackofficeEmailService(IAppDbContext db, ICurrentActor actor, IClock clock, ISecretProtector secrets, MailConfig mail,
    IMailSender sender, PortalMailer mailer, IHtmlCleaner cleaner)
{
    // ---------------- server ----------------

    async Task<EmailSetting> SettingAsync(CancellationToken ct)
    {
        var s = await db.EmailSettings.FirstOrDefaultAsync(ct);
        if (s == null)
        {
            s = new EmailSetting { UpdatedAt = clock.UtcNow };
            db.EmailSettings.Add(s);
        }
        return s;
    }

    /// <summary>The password is never returned, only whether one is stored.</summary>
    public async Task<object> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.EmailSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new EmailSetting();
        var current = await mail.CurrentAsync(ct);
        return new
        {
            host = s.Host, port = s.Port, use_tls = s.UseTls, user_name = s.UserName, has_password = s.PasswordProtected.Length > 0,
            can_store_password = secrets.IsConfigured, from_address = s.FromAddress, from_name = s.FromName,
            signature_ar = s.SignatureAr, signature_en = s.SignatureEn, attach_offer_pdf = s.AttachOfferPdf, updated_at = s.UpdatedAt,
            in_use = current == null ? "None" : current.Source, environment_host = current?.Source == "Environment" ? current.Host : null,
        };
    }

    public async Task<object> SaveSettingsAsync(EmailSettingInput input, CancellationToken ct = default)
    {
        var s = await SettingAsync(ct);
        s.Host = TextUtil.Clip(input.Host?.Trim(), 200);
        s.Port = input.Port;
        s.UseTls = input.UseTls;
        s.UserName = TextUtil.Clip(input.UserName?.Trim(), 200);
        if (input.ClearPassword) s.PasswordProtected = "";
        else if (!string.IsNullOrEmpty(input.Password)) s.PasswordProtected = secrets.Protect(input.Password);
        s.FromAddress = TextUtil.Clip(input.FromAddress?.Trim(), 254);
        s.FromName = TextUtil.NoEmDash(TextUtil.Clip(input.FromName?.Trim(), 200));
        s.SignatureAr = TextUtil.NoEmDash(TextUtil.Clip(input.SignatureAr?.Trim(), 1000));
        s.SignatureEn = TextUtil.NoEmDash(TextUtil.Clip(input.SignatureEn?.Trim(), 1000));
        if (input.AttachOfferPdf is { } attach) s.AttachOfferPdf = attach;
        s.UpdatedAt = clock.UtcNow;
        s.UpdatedByStaffId = actor.Id;
        s.Validate();
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    /// <summary>Sends a short email straight away through the server in use and reports the server's answer.</summary>
    public async Task<object> TestServerAsync(TestEmailInput input, CancellationToken ct = default)
    {
        if (!EmailTemplates.IsEmail(input.To)) throw DomainException.Invalid("email_invalid");
        var config = await mail.CurrentAsync(ct) ?? throw DomainException.Conflict("email_not_configured");
        var ar = input.Lang == "ar";
        var subject = ar ? "رسالة تجريبية من نظام التوظيف" : "Test Email from the Recruitment System";
        var body = ar ? "هذه رسالة تجريبية للتأكد من إعدادات خادم البريد." : "This is a test email to check the email server settings.";
        var signature = await mailer.SignatureAsync(ar ? "ar" : "en", ct);
        return await TrySendAsync(config, input.To!.Trim(), subject, PortalMailer.Html(body, subject, "", signature, ar), $"{body}\n\n{signature}", ct);
    }

    async Task<object> TrySendAsync(SmtpConfig config, string to, string subject, string html, string text, CancellationToken ct)
    {
        try
        {
            await sender.SendAsync(config, to, subject, html, text, ct);
            return new { ok = true, server = config.Host };
        }
        catch (Exception e) when (e is System.Net.Mail.SmtpException or InvalidOperationException or FormatException or System.Net.Sockets.SocketException)
        {
            return new { ok = false, server = config.Host, error = TextUtil.Clip(e.Message, 500) };
        }
    }

    // ---------------- templates ----------------

    public async Task<object> TemplatesAsync(CancellationToken ct = default)
    {
        var rows = await db.EmailTemplates.AsNoTracking().ToListAsync(ct);
        var order = EmailTemplates.Events.Keys.ToList();
        return new
        {
            items = rows.OrderBy(t => t.Event == null ? 1 : 0).ThenBy(t => t.Event == null ? 0 : order.IndexOf(t.Event)).ThenBy(t => t.NameEn).Select(t => new
            {
                id = t.Id, key = t.Key, @event = t.Event, name_ar = t.NameAr, name_en = t.NameEn, subject_ar = t.SubjectAr, subject_en = t.SubjectEn,
                is_enabled = t.IsEnabled, required = t.Event != null && EmailTemplates.Events[t.Event].Required, updated_at = t.UpdatedAt,
            }),
            email_ready = await mail.IsConfiguredAsync(ct),
        };
    }

    public async Task<object> TemplateAsync(int id, CancellationToken ct = default)
    {
        var t = await db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        return Full(t);
    }

    static object Full(EmailTemplate t)
    {
        var ev = t.Event != null ? EmailTemplates.Events[t.Event] : null;
        return new
        {
            id = t.Id, key = t.Key, @event = t.Event, name_ar = t.NameAr, name_en = t.NameEn, subject_ar = t.SubjectAr, body_ar = t.BodyAr,
            subject_en = t.SubjectEn, body_en = t.BodyEn, body_html_ar = t.BodyHtmlAr, body_html_en = t.BodyHtmlEn, is_enabled = t.IsEnabled, required = ev?.Required ?? false,
            placeholders = ev?.Placeholders ?? ["name", "job_title", "reference"], row_version = Convert.ToBase64String(t.RowVersion),
        };
    }

    public async Task<object> SaveTemplateAsync(int? id, EmailTemplateInput input, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        EmailTemplate t;
        if (id == null)
        {
            t = new EmailTemplate { CreatedAt = now, Key = (input.Key ?? "").Trim().ToLowerInvariant() };
            db.EmailTemplates.Add(t);
        }
        else
        {
            t = await db.EmailTemplates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
            if (!string.IsNullOrEmpty(input.RowVersion) && input.RowVersion != Convert.ToBase64String(t.RowVersion))
                throw DomainException.Conflict("changed_elsewhere");
            // Event templates keep their key; HR's own templates can be renamed.
            if (t.Event == null) t.Key = (input.Key ?? "").Trim().ToLowerInvariant();
        }
        t.NameAr = TextUtil.NoEmDash(TextUtil.Clip(input.NameAr?.Trim(), 150));
        t.NameEn = TextUtil.NoEmDash(TextUtil.Clip(input.NameEn?.Trim(), 150));
        t.SubjectAr = TextUtil.NoEmDash(TextUtil.Clip(input.SubjectAr?.Trim(), 300));
        t.SubjectEn = TextUtil.NoEmDash(TextUtil.Clip(input.SubjectEn?.Trim(), 300));
        t.BodyAr = TextUtil.NoEmDash(TextUtil.Clip(input.BodyAr?.Trim(), 20000));
        t.BodyEn = TextUtil.NoEmDash(TextUtil.Clip(input.BodyEn?.Trim(), 20000));
        t.BodyHtmlAr = CleanHtml(input.BodyHtmlAr);
        t.BodyHtmlEn = CleanHtml(input.BodyHtmlEn);
        t.IsEnabled = input.IsEnabled;
        t.UpdatedAt = now;
        t.UpdatedByStaffId = actor.Id;
        t.Validate();
        if (await db.EmailTemplates.AnyAsync(x => x.Id != t.Id && x.Key == t.Key, ct)) throw DomainException.Conflict("template_key_taken");
        await db.SaveChangesAsync(ct);
        return new { id = t.Id };
    }

    /// <summary>A designed HTML version, cleaned like all staff HTML; empty when it has no text (an emptied editor).</summary>
    string CleanHtml(string? html)
    {
        var clean = TextUtil.NoEmDash(cleaner.Clean(TextUtil.Clip(html, 100000)));
        return TextUtil.StripHtml(clean).Trim().Length == 0 ? "" : clean;
    }

    public async Task<object> DeleteTemplateAsync(int id, CancellationToken ct = default)
    {
        var t = await db.EmailTemplates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        if (t.Event != null) throw DomainException.Conflict("template_is_event");
        db.EmailTemplates.Remove(t);
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    /// <summary>Sample values for previews and test emails.</summary>
    static Dictionary<string, string> Samples(bool ar) => new()
    {
        ["name"] = ar ? "سارة أحمد" : "Sara Ahmed", ["link"] = "https://careers.example/ar", ["job_title"] = ar ? "محلل بيانات" : "Data Analyst",
        ["reference"] = "APP-2026-0042", ["test"] = ar ? "اختبار التحليل" : "Analysis Test", ["deadline"] = ar ? "20 نوفمبر 2026، 5:00 م" : "20 November 2026, 5:00 PM",
        ["round"] = ar ? "المقابلة الفنية" : "Technical Interview", ["date"] = "2026-11-25", ["time"] = "10:00", ["location"] = ar ? "مبنى الوزارة، دبي" : "Ministry building, Dubai",
        ["position_title"] = ar ? "محلل بيانات أول" : "Senior Data Analyst", ["start_date"] = "2027-01-01",
        ["offer_letter"] = ar ? "# عرض عمل\n\nنص خطاب العرض يظهر هنا." : "# Offer of Employment\n\nThe offer letter text appears here.",
    };

    /// <summary>Sends the template, filled with sample values, to the given address (to check how it looks in a mail client).</summary>
    public async Task<object> TestTemplateAsync(int id, TestEmailInput input, CancellationToken ct = default)
    {
        if (!EmailTemplates.IsEmail(input.To)) throw DomainException.Invalid("email_invalid");
        var t = await db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        var config = await mail.CurrentAsync(ct) ?? throw DomainException.Conflict("email_not_configured");
        var lang = input.Lang == "ar" ? "ar" : "en";
        var (subject, body) = t.In(lang);
        var values = Samples(lang == "ar");
        subject = EmailTemplates.Fill(subject, values);
        var (html, text) = PortalMailer.Compose(body, t.HtmlIn(lang), values, subject, values["link"], await mailer.SignatureAsync(lang, ct), lang == "ar");
        return await TrySendAsync(config, input.To!.Trim(), subject, html, text, ct);
    }

    /// <summary>The templates for the assistant and for staff writing to a candidate: wording and placeholders.</summary>
    public async Task<object> WordingAsync(CancellationToken ct = default)
    {
        var rows = await db.EmailTemplates.AsNoTracking().Where(t => t.IsEnabled).OrderBy(t => t.NameEn).ToListAsync(ct);
        return new
        {
            items = rows.Select(t => new
            {
                key = t.Key, @event = t.Event, name_ar = t.NameAr, name_en = t.NameEn, subject_ar = t.SubjectAr, body_ar = t.BodyAr,
                subject_en = t.SubjectEn, body_en = t.BodyEn,
            }),
        };
    }

    // ---------------- log ----------------

    static readonly Sorts<EmailMessage> LogSorts = new Sorts<EmailMessage>("created")
        .Add("created", m => m.CreatedAt).Add("to", m => m.ToAddress).Add("status", m => m.Status).Add("sent", m => m.SentAt).Add("attempts", m => m.Attempts);

    public Task<PageResult<object>> LogAsync(ListQuery lq, string? status, CancellationToken ct = default)
    {
        var q = db.EmailMessages.AsNoTracking().AsQueryable();
        if (Enum.TryParse<EmailStatus>(status, out var st)) q = q.Where(m => m.Status == st);
        if (lq.Term.Length > 0)
        {
            var term = lq.Term;
            q = q.Where(m => m.ToAddress.Contains(term) || m.Subject.Contains(term));
        }
        return q.PageAsync(lq, LogSorts, m => (object)new
        {
            id = m.Id, to = m.ToAddress, subject = m.Subject, status = m.Status, attempts = m.Attempts, last_error = m.LastError,
            created_at = m.CreatedAt, sent_at = m.SentAt, next_attempt_at = m.NextAttemptAt,
        }, ct);
    }

    public async Task<object> LogItemAsync(long id, CancellationToken ct = default)
    {
        var m = await db.EmailMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        return new { id = m.Id, to = m.ToAddress, subject = m.Subject, html = m.HtmlBody, text = m.TextBody, status = m.Status, attempts = m.Attempts, last_error = m.LastError, created_at = m.CreatedAt, sent_at = m.SentAt };
    }

    /// <summary>Puts a failed email back in the outbox for another round of attempts.</summary>
    public async Task<object> RetryAsync(long id, CancellationToken ct = default)
    {
        var m = await db.EmailMessages.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        if (m.Status != EmailStatus.Failed) throw DomainException.Conflict("email_not_failed");
        m.Status = EmailStatus.Queued;
        m.Attempts = 0;
        m.NextAttemptAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }
}
