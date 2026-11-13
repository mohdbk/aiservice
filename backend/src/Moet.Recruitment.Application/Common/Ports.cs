using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Domain.Assessments;
using Moet.Recruitment.Domain.Content;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Identity;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Common;

/// <summary>The unit of work over the recruitment database. Implemented in Infrastructure.</summary>
public interface IAppDbContext
{
    DbSet<Language> Languages { get; }
    DbSet<PortalSetting> PortalSettings { get; }
    DbSet<PortalText> PortalTexts { get; }
    DbSet<Department> Departments { get; }
    DbSet<JobLocation> JobLocations { get; }
    DbSet<Country> Countries { get; }
    DbSet<StaffUser> StaffUsers { get; }
    DbSet<ApplicantAccount> ApplicantAccounts { get; }
    DbSet<AccountToken> AccountTokens { get; }
    DbSet<JobOpening> JobOpenings { get; }
    DbSet<JobOpeningTranslation> JobOpeningTranslations { get; }
    DbSet<ScreeningQuestion> ScreeningQuestions { get; }
    DbSet<JobApplication> JobApplications { get; }
    DbSet<CvAnalysis> CvAnalyses { get; }
    DbSet<ApplicationNote> ApplicationNotes { get; }
    DbSet<InterviewRound> InterviewRounds { get; }
    DbSet<Interview> Interviews { get; }
    DbSet<InterviewFeedback> InterviewFeedback { get; }
    DbSet<JobOffer> JobOffers { get; }
    DbSet<Question> Questions { get; }
    DbSet<Assessment> Assessments { get; }
    DbSet<Invitation> Invitations { get; }
    DbSet<Attempt> Attempts { get; }
    DbSet<AttemptQuestion> AttemptQuestions { get; }
    DbSet<StoredFile> StoredFiles { get; }
    DbSet<EmailMessage> EmailMessages { get; }
    DbSet<EmailSetting> EmailSettings { get; }
    DbSet<EmailTemplate> EmailTemplates { get; }
    DbSet<DocumentTemplate> DocumentTemplates { get; }
    DbSet<BackgroundJob> BackgroundJobs { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<AuditComment> AuditComments { get; }
    DbSet<AgentProposal> AgentProposals { get; }
    DbSet<AgentComponent> AgentComponents { get; }
    DbSet<RoleProfile> RoleProfiles { get; }
    DbSet<TypeNote> TypeNotes { get; }
    DbSet<DataRequest> DataRequests { get; }
    DbSet<ApprovalRequest> ApprovalRequests { get; }
    DbSet<HealthAlertSetting> HealthAlertSettings { get; }
    DbSet<JobAlert> JobAlerts { get; }
    DbSet<JobAlertSent> JobAlertSents { get; }
    DbSet<Requisition> Requisitions { get; }
    DbSet<InterviewBooking> InterviewBookings { get; }
    DbSet<TextSetting> TextSettings { get; }
    DbSet<HandOffSetting> HandOffSettings { get; }
    DbSet<TextMessage> TextMessages { get; }
    DbSet<ConsentRecord> ConsentRecords { get; }
    DbSet<RetentionSetting> RetentionSettings { get; }
    DbSet<PersonalitySetting> PersonalitySettings { get; }
    DbSet<AgentSetting> AgentSettings { get; }
    DbSet<AgentRun> AgentRuns { get; }
    DbSet<AgentConversation> AgentConversations { get; }
    DbSet<AgentAttachment> AgentAttachments { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IClock
{
    DateTime UtcNow { get; }
    /// <summary>The ministry's time zone (Settings; TIME_ZONE or Asia/Dubai until one is chosen).</summary>
    TimeZoneInfo Zone { get; }
    /// <summary>Switches to another time zone (an IANA name such as Asia/Dubai); false when the name is not known.</summary>
    bool UseZone(string? id) => false;
}

/// <summary>Who is acting, for audit and "approved by" fields.</summary>
/// <summary>The language the backoffice is shown in (X-Lang): lists show English names and titles when it is English.</summary>
public interface IRequestLanguage
{
    bool English { get; }
}

public interface ICurrentActor
{
    Domain.ActorType Type { get; }
    int? Id { get; }
    string Name { get; }
    /// <summary>A recruitment admin or administrator (who can approve jobs, offers and requisitions).</summary>
    bool IsAdmin => false;
}

public interface IPasswordHasher
{
    string Hash(string password);
    /// <summary>True when the password matches.</summary>
    bool Verify(string hash, string password);
}

/// <summary>Readable codes from database sequences: JOB-2026-0001, APP-2026-00001, and so on.</summary>
public interface ICodeGenerator
{
    Task<string> NextAsync(CodeKind kind, CancellationToken ct = default);
}

public enum CodeKind { JobOpening, JobApplication, Interview, JobOffer, Invitation, Requisition }

/// <summary>The wording that ships with the product (English base and other languages).</summary>
public interface ITextCatalog
{
    IReadOnlyDictionary<string, string> Shipped(string lang);
}

public interface IHtmlCleaner
{
    /// <summary>Removes scripts, styles and anything unsafe from HTML written by staff or a language model.</summary>
    string Clean(string? html);
}

/// <summary>An SMTP server to send through.</summary>
public sealed record SmtpConfig(string Host, int Port, bool UseTls, string UserName, string Password, string FromAddress, string FromName, string Source);

/// <summary>The SMTP_* environment settings (used when no email server is set in the backoffice), or null.</summary>
public interface IMailEnvironment
{
    SmtpConfig? FromEnvironment { get; }
}

/// <summary>Sends one email straight away (the outbox worker and the Send Test Email button).</summary>
/// <summary>The provider settings a text is sent with (the token already decrypted).</summary>
public sealed record TextConfig(string Provider, TextChannel Channel, string Url, string AccountId, string Token, string Sender);

/// <summary>POSTs JSON to another system; throws with the reason on any failure.</summary>
public interface IWebhookClient
{
    Task PostAsync(string url, string json, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default);
}

/// <summary>Sends one SMS or WhatsApp message; throws on failure.</summary>
public interface ITextSender
{
    Task SendAsync(TextConfig config, string to, string body, CancellationToken ct = default);
}

/// <summary>Database backups in a bucket (see the infrastructure's BackupService).</summary>
public interface IBackups
{
    bool IsConfigured { get; }
    double IntervalHours { get; }
    Task<(string Key, long Size)> BackUpAsync(CancellationToken ct);
    Task<List<(string Key, long Size, DateTime Modified)>> ListAsync(CancellationToken ct);
}

/// <summary>
/// Who and what is behind the changes of the current request, for the audit log: whether it came through the assistant, the
/// comment given, the proposal approved, and the request id and address.
/// </summary>
public interface IAuditContext
{
    string Via { get; set; }
    string Comment { get; set; }
    Guid? ProposalId { get; set; }
    string RequestId { get; }
    string IpAddress { get; }
}

/// <summary>A file sent with an email.</summary>
public sealed record MailAttachment(string FileName, string ContentType, byte[] Content);

public interface IMailSender
{
    Task SendAsync(SmtpConfig config, string to, string subject, string html, string text, CancellationToken ct = default, MailAttachment? attachment = null);
}

/// <summary>Lays a letter out as a PDF (Arabic right to left, English left to right).</summary>
public enum ScanOutcome { Clean, Infected, Unavailable, Off }

/// <summary>Virus scanning of uploaded files (ClamAV when CLAMAV_HOST is set; off otherwise).</summary>
public interface IVirusScanner
{
    Task<(ScanOutcome Outcome, string Detail)> ScanAsync(byte[] data, CancellationToken ct = default);
    /// <summary>Whether a scanner answers (for the health page): "off", "ok" or "down".</summary>
    Task<string> StateAsync(CancellationToken ct = default);
    /// <summary>Refuses an upload that is infected, or (when scanning is required) one that could not be scanned.</summary>
    async Task CheckAsync(byte[] data, CancellationToken ct = default)
    {
        var (outcome, _) = await ScanAsync(data, ct);
        if (outcome == ScanOutcome.Infected) throw Domain.Common.DomainException.Invalid("file_rejected");
        if (outcome == ScanOutcome.Unavailable && Required) throw Domain.Common.DomainException.Conflict("scan_unavailable");
    }
    bool Required { get; }
}

/// <summary>Prints a letter's HTML (the same as its preview) as an A4 PDF, with the ministry logo at the top.</summary>
public interface IPdfRenderer
{
    Task<byte[]> LetterAsync(string title, string html, bool rtl, byte[]? logo, CancellationToken ct = default);
}

/// <summary>An OpenAI-compatible chat model, on-prem or hosted.</summary>
public interface ILanguageModel
{
    bool IsConfigured { get; }
    string ModelName { get; }

    /// <summary>One completion constrained to a JSON schema; returns the parsed object.</summary>
    Task<JsonElement> CompleteJsonAsync(string system, string user, object schema, string schemaName, CancellationToken ct = default);

    /// <summary>A chat turn with tools (for the HR assistant).</summary>
    Task<ChatTurn> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools, CancellationToken ct = default);
}

public sealed record ChatMessage(string Role, string? Content, IReadOnlyList<ToolCall>? ToolCalls = null, string? ToolCallId = null);

public sealed record ToolCall(string Id, string Name, string Arguments);

public sealed record ToolSpec(string Name, string Description, object Parameters);

public sealed record ChatTurn(string? Content, IReadOnlyList<ToolCall> ToolCalls, int PromptTokens, int CompletionTokens);

/// <summary>Reads the text of a CV file (PDF text layer, Word, or OCR for scans and photos when configured).</summary>
public interface ICvReader
{
    Task<CvText> ReadAsync(byte[] content, string extension, CancellationToken ct = default);
}

public sealed record CvText(string Text, string Method);

/// <summary>Encrypts small secrets (such as the assistant's model key) before they are stored.</summary>
public interface ISecretProtector
{
    bool IsConfigured { get; }
    string Protect(string plain);
    /// <summary>Null when the value cannot be read (for example the key changed).</summary>
    string? Unprotect(string protectedValue);
}
