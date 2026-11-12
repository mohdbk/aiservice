using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Common;

public sealed record TextSettingInput(bool IsEnabled, string? Channel, string? Provider, string? Url, string? AccountId, string? Token, string? Sender,
    IReadOnlyList<string>? Events);

/// <summary>
/// Text messages (SMS or WhatsApp) alongside candidate emails: the provider settings (system administrators) and the outbox.
/// The text is the email's subject and its link, sent to the phone number of the candidate's latest application.
/// </summary>
public sealed class TextService(IAppDbContext db, ISecretProtector secrets, IClock clock, ICurrentActor actor, ITextSender sender)
{
    TextSetting? cached;

    async Task<TextSetting> SettingAsync(CancellationToken ct) => cached ??= await db.TextSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new TextSetting();

    public async Task<TextConfig?> ConfigAsync(CancellationToken ct = default)
    {
        var s = await SettingAsync(ct);
        return !s.IsEnabled ? null : new TextConfig(s.Provider, s.Channel, s.Url, s.AccountId,
            s.TokenProtected.Length > 0 ? secrets.Unprotect(s.TokenProtected) ?? "" : "", s.Sender);
    }

    /// <summary>Queues a text for an email event when texts are on for it and the candidate gave a usable number.</summary>
    public async Task<bool> QueueAsync(string email, string eventKey, string subject, string link, CancellationToken ct = default)
    {
        var s = await SettingAsync(ct);
        if (!s.IsEnabled || !s.EventSet().Contains(eventKey)) return false;
        var phone = await db.JobApplications.AsNoTracking().Where(a => a.Email == email && a.AnonymisedAt == null).OrderByDescending(a => a.CreatedAt)
            .Select(a => a.Phone).FirstOrDefaultAsync(ct);
        if (TextMessage.Normalise(phone) is not { } to) return false;
        var body = TextUtil.Clip(link.Length > 0 ? $"{subject}\n{link}" : subject, 900);
        db.TextMessages.Add(new TextMessage
        {
            ToNumber = to, Channel = s.Channel, Body = body, EventKey = eventKey, NextAttemptAt = clock.UtcNow, CreatedAt = clock.UtcNow,
        });
        return true;
    }

    public async Task<object> GetAsync(CancellationToken ct = default)
    {
        var s = await db.TextSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new TextSetting();
        var since = clock.UtcNow.AddDays(-1);
        return new
        {
            is_enabled = s.IsEnabled, channel = s.Channel.ToString(), provider = s.Provider, url = s.Url, account_id = s.AccountId,
            has_token = s.TokenProtected.Length > 0, sender = s.Sender, events = s.EventSet().ToList(),
            all_events = EmailTemplates.Events.Where(e => !e.Value.Required).Select(e => new { key = e.Key, name_ar = e.Value.NameAr, name_en = e.Value.NameEn }),
            sent_day = await db.TextMessages.CountAsync(m => m.Status == EmailStatus.Sent && m.SentAt >= since, ct),
            failed_day = await db.TextMessages.CountAsync(m => m.Status == EmailStatus.Failed && m.CreatedAt >= since, ct),
            queued = await db.TextMessages.CountAsync(m => m.Status == EmailStatus.Queued, ct),
            recent = await db.TextMessages.AsNoTracking().OrderByDescending(m => m.Id).Take(10).Select(m => new
            {
                id = m.Id, to = m.ToNumber.Length > 4 ? "..." + m.ToNumber.Substring(m.ToNumber.Length - 4) : m.ToNumber, @event = m.EventKey,
                status = m.Status.ToString(), error = m.LastError, created_at = m.CreatedAt,
            }).ToListAsync(ct),
        };
    }

    public async Task<object> SaveAsync(TextSettingInput input, CancellationToken ct = default)
    {
        var s = await db.TextSettings.FirstOrDefaultAsync(ct);
        if (s == null) db.TextSettings.Add(s = new TextSetting());
        s.IsEnabled = input.IsEnabled;
        s.Channel = Enum.TryParse<TextChannel>(input.Channel, out var ch) ? ch : TextChannel.Sms;
        s.Provider = input.Provider ?? "Webhook";
        s.Url = TextUtil.Clip(input.Url?.Trim(), 500);
        s.AccountId = TextUtil.Clip(input.AccountId?.Trim(), 100);
        s.Sender = TextUtil.Clip(input.Sender?.Trim(), 50);
        // An empty token keeps the stored one.
        if (!string.IsNullOrEmpty(input.Token)) s.TokenProtected = secrets.Protect(input.Token.Trim());
        s.Events = string.Join(",", (input.Events ?? []).Where(EmailTemplates.Events.ContainsKey).Distinct());
        s.Validate();
        s.UpdatedAt = clock.UtcNow;
        s.UpdatedByStaffId = actor.Id;
        await db.SaveChangesAsync(ct);
        cached = null;
        return new { ok = true };
    }

    /// <summary>Sends a test text straight away with the saved settings.</summary>
    public async Task<object> TestAsync(string? to, CancellationToken ct = default)
    {
        var number = TextMessage.Normalise(to) ?? throw DomainException.Invalid("phone_invalid");
        var config = await ConfigAsync(ct) ?? throw DomainException.Conflict("texts_off");
        try
        {
            await sender.SendAsync(config, number, "Ministry of Economy & Tourism: test message", ct);
            return new { ok = true, error = "" };
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return new { ok = false, error = TextUtil.Clip(e.Message, 300) };
        }
    }
}
