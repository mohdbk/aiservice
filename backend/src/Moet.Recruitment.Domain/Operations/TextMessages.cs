using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Operations;

public enum TextChannel { Sms, WhatsApp }

/// <summary>
/// Text messages to candidates (SMS or WhatsApp) through a provider set by system administrators: a generic webhook (the
/// Ministry's own gateway or any service that accepts a JSON POST) or Twilio. A message goes out alongside the emails of the
/// chosen events. One row; the token is stored encrypted.
/// </summary>
public class TextSetting
{
    public byte Id { get; set; } = 1;
    public bool IsEnabled { get; set; }
    public TextChannel Channel { get; set; } = TextChannel.Sms;
    /// <summary>Webhook or Twilio.</summary>
    public string Provider { get; set; } = "Webhook";
    public string Url { get; set; } = "";
    /// <summary>Twilio account SID, or empty for a webhook.</summary>
    public string AccountId { get; set; } = "";
    public string TokenProtected { get; set; } = "";
    public string Sender { get; set; } = "";
    /// <summary>Comma-separated email event keys that also send a text.</summary>
    public string Events { get; set; } = "interview_scheduled,interview_booking,assessment,offer_sent";
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByStaffId { get; set; }

    public static readonly string[] Providers = ["Webhook", "Twilio"];

    public HashSet<string> EventSet() => Events.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

    public void Validate()
    {
        if (!Providers.Contains(Provider)) throw DomainException.Invalid("invalid_value", new Dictionary<string, object?> { ["field"] = "provider" });
        if (!IsEnabled) return;
        if (Provider == "Webhook" && !(Uri.TryCreate(Url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Host == "localhost")))
            throw DomainException.Invalid("text_url_invalid");
        if (Provider == "Twilio" && (AccountId.Trim().Length == 0 || Sender.Trim().Length == 0)) throw DomainException.Invalid("text_twilio_required");
    }
}

public class TextMessage
{
    public long Id { get; set; }
    public string ToNumber { get; set; } = "";
    public TextChannel Channel { get; set; }
    public string Body { get; set; } = "";
    public string EventKey { get; set; } = "";
    public EmailStatus Status { get; set; } = EmailStatus.Queued;
    public int Attempts { get; set; }
    public string LastError { get; set; } = "";
    public DateTime NextAttemptAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }

    public const int MaxAttempts = 5;

    public void Failed(string error, DateTime now)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        if (Attempts >= MaxAttempts) Status = EmailStatus.Failed;
        else NextAttemptAt = now.AddMinutes(Attempts * Attempts);
    }

    /// <summary>A UAE or international number in E.164 form (+9715...), or null when it cannot be one.</summary>
    public static string? Normalise(string? phone)
    {
        var d = new string((phone ?? "").Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (d.StartsWith("00", StringComparison.Ordinal)) d = "+" + d[2..];
        else if (d.StartsWith("05", StringComparison.Ordinal) && d.Length == 10) d = "+971" + d[1..];
        else if (!d.StartsWith('+')) return null;
        return d.Length is >= 9 and <= 16 && d[1..].All(char.IsDigit) ? d : null;
    }
}
