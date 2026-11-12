using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Assessments;
using Moet.Recruitment.Domain.Content;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Identity;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Infrastructure.Persistence;

/// <summary>
/// EF Core mapping onto the hand-written schema in Persistence/Schema (the schema scripts are the source of truth;
/// EF never creates or migrates tables). Enums are stored as their names; times are UTC.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentActor actor, IClock clock, IAuditContext audit)
    : DbContext(options), IAppDbContext, IDataProtectionKeyContext
{
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<PortalSetting> PortalSettings => Set<PortalSetting>();
    public DbSet<PortalText> PortalTexts => Set<PortalText>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<JobLocation> JobLocations => Set<JobLocation>();
    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();
    public DbSet<ApplicantAccount> ApplicantAccounts => Set<ApplicantAccount>();
    public DbSet<AccountToken> AccountTokens => Set<AccountToken>();
    public DbSet<JobOpening> JobOpenings => Set<JobOpening>();
    public DbSet<JobOpeningTranslation> JobOpeningTranslations => Set<JobOpeningTranslation>();
    public DbSet<ScreeningQuestion> ScreeningQuestions => Set<ScreeningQuestion>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<CvAnalysis> CvAnalyses => Set<CvAnalysis>();
    public DbSet<ApplicationNote> ApplicationNotes => Set<ApplicationNote>();
    public DbSet<InterviewRound> InterviewRounds => Set<InterviewRound>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<InterviewFeedback> InterviewFeedback => Set<InterviewFeedback>();
    public DbSet<JobOffer> JobOffers => Set<JobOffer>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AttemptQuestion> AttemptQuestions => Set<AttemptQuestion>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<EmailSetting> EmailSettings => Set<EmailSetting>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<AuditComment> AuditComments => Set<AuditComment>();
    public DbSet<AgentProposal> AgentProposals => Set<AgentProposal>();
    public DbSet<AgentComponent> AgentComponents => Set<AgentComponent>();
    public DbSet<RoleProfile> RoleProfiles => Set<RoleProfile>();
    public DbSet<TypeNote> TypeNotes => Set<TypeNote>();
    public DbSet<DataRequest> DataRequests => Set<DataRequest>();
    public DbSet<InterviewBooking> InterviewBookings => Set<InterviewBooking>();
    public DbSet<TextSetting> TextSettings => Set<TextSetting>();
    public DbSet<AgentConversation> AgentConversations => Set<AgentConversation>();
    public DbSet<AgentAttachment> AgentAttachments => Set<AgentAttachment>();
    public DbSet<HandOffSetting> HandOffSettings => Set<HandOffSetting>();
    public DbSet<TextMessage> TextMessages => Set<TextMessage>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<RetentionSetting> RetentionSettings => Set<RetentionSetting>();
    public DbSet<PersonalitySetting> PersonalitySettings => Set<PersonalitySetting>();
    public DbSet<AgentSetting> AgentSettings => Set<AgentSetting>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    /// <summary>The key ring that protects sign-in cookies, shared by every API instance and kept across restarts.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<decimal>().HavePrecision(18, 4);
        b.Properties<DateTime>().HaveConversion<UtcConverter>();
        b.Properties<DateTime?>().HaveConversion<UtcNullableConverter>();
    }

    sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    sealed class UtcNullableConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v, v => v == null ? null : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder m)
    {
        ModelMap.Configure(m);

        // Enums are stored by name (CHECK constraints in the schema list the names).
        foreach (var entity in m.Model.GetEntityTypes())
            foreach (var p in entity.GetProperties())
            {
                var t = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
                if (t.IsEnum) p.SetProviderClrType(typeof(string));
            }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var entries = Audit.Collect(ChangeTracker, actor, audit, clock.UtcNow);
        var n = await base.SaveChangesAsync(ct);
        if (entries.Count > 0)
        {
            foreach (var e in entries) e.Complete();
            AuditEntries.AddRange(entries.Select(e => e.Entry));
            await base.SaveChangesAsync(ct);
        }
        return n;
    }
}

/// <summary>
/// Records who changed what, on every kind of record except the log itself and high-volume plumbing (outboxes, background
/// jobs, sign-in keys, stored file bytes, single test answers, chat history).
/// </summary>
static class Audit
{
    static readonly HashSet<string> NotAudited =
    [
        nameof(AuditEntry), nameof(AuditComment), nameof(BackgroundJob), nameof(EmailMessage), nameof(TextMessage), nameof(AgentRun),
        nameof(StoredFile), "DataProtectionKey", nameof(AccountToken), nameof(AttemptQuestion), "AttemptOption", "AttemptQuestionTranslation",
        "AttemptAcceptedAnswer", "AttemptSection", nameof(AgentConversation), "CodeSequence",
    ];

    static readonly HashSet<string> Secret = ["PasswordHash", "RowVersion", "Content", "ModelApiKeyProtected", "PasswordProtected", "TokenProtected", "SecretProtected"];

    public sealed class Pending(EntityEntry source, AuditEntry entry, bool added)
    {
        public AuditEntry Entry { get; } = entry;

        /// <summary>Generated keys are only known after saving.</summary>
        public void Complete()
        {
            if (!added) return;
            Entry.EntityKey = Key(source);
        }
    }

    public static List<Pending> Collect(ChangeTracker tracker, ICurrentActor actor, IAuditContext ctx, DateTime now)
    {
        var list = new List<Pending>();
        foreach (var e in tracker.Entries())
        {
            if (NotAudited.Contains(e.Entity.GetType().Name) || e.Metadata.IsOwned() || e.State is EntityState.Unchanged or EntityState.Detached) continue;
            var changes = new Dictionary<string, object?>();
            foreach (var p in e.Properties)
            {
                if (Secret.Contains(p.Metadata.Name)) continue;
                if (e.State == EntityState.Modified && !p.IsModified) continue;
                if (e.State == EntityState.Modified && Equals(p.OriginalValue, p.CurrentValue)) continue;
                changes[p.Metadata.Name] = e.State switch
                {
                    EntityState.Added => new { to = Short(p.CurrentValue) },
                    EntityState.Deleted => new { from = Short(p.OriginalValue) },
                    _ => new { from = Short(p.OriginalValue), to = Short(p.CurrentValue) },
                };
            }
            if (e.State == EntityState.Modified && changes.Count == 0) continue;
            var action = e.State switch { EntityState.Added => AuditAction.Created, EntityState.Deleted => AuditAction.Deleted, _ => AuditAction.Updated };
            list.Add(new Pending(e, new AuditEntry
            {
                EntityName = e.Metadata.ClrType.Name, EntityKey = Key(e), Action = action, Changes = JsonSerializer.Serialize(changes),
                ActorType = actor.Type, ActorId = actor.Id, ActorName = actor.Name, OccurredAt = now,
                Via = ctx.Via, Comment = TextUtil.Clip(ctx.Comment, 1000), ProposalId = ctx.ProposalId, RequestId = ctx.RequestId, IpAddress = ctx.IpAddress,
            }, e.State == EntityState.Added));
        }
        return list;
    }

    static string Key(EntityEntry e)
    {
        var pk = e.Metadata.FindPrimaryKey();
        return pk == null ? "" : string.Join("/", pk.Properties.Select(p => e.Property(p.Name).CurrentValue?.ToString() ?? ""));
    }

    static object? Short(object? v) => v switch
    {
        null => null,
        string s when s.Length > 500 => s[..500] + "…",
        DateTime d => d.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        byte[] => "(binary)",
        _ => v.ToString(),
    };
}
