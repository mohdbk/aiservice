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
    DbSet<OfferTemplate> OfferTemplates { get; }
    DbSet<BackgroundJob> BackgroundJobs { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<AgentProposal> AgentProposals { get; }
    DbSet<AgentComponent> AgentComponents { get; }
    DbSet<RoleProfile> RoleProfiles { get; }
    DbSet<AgentSetting> AgentSettings { get; }
    DbSet<AgentRun> AgentRuns { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IClock
{
    DateTime UtcNow { get; }
    TimeZoneInfo Zone { get; }
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

public enum CodeKind { JobOpening, JobApplication, Interview, JobOffer, Invitation }

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
public interface IMailSender
{
    Task SendAsync(SmtpConfig config, string to, string subject, string html, CancellationToken ct = default);
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
