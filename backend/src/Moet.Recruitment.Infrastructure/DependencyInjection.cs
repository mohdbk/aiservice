using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Identity;
using Moet.Recruitment.Infrastructure.Ai;
using Moet.Recruitment.Infrastructure.Background;
using Moet.Recruitment.Infrastructure.Cv;
using Moet.Recruitment.Infrastructure.Persistence;

namespace Moet.Recruitment.Infrastructure;

public static class DependencyInjection
{
    public static string ConnectionString(IConfiguration cfg) =>
        cfg["DB_CONNECTION"] ?? cfg.GetConnectionString("Recruitment")
        ?? throw new InvalidOperationException("Set DB_CONNECTION (SQL Server connection string).");

    public static IServiceCollection AddInfrastructure(this IServiceCollection s, IConfiguration cfg, bool runWorkers = true)
    {
        s.AddHttpContextAccessor();
        s.AddDbContext<AppDbContext>(o => o.UseSqlServer(ConnectionString(cfg), sql =>
        {
            sql.EnableRetryOnFailure(3);
            sql.CommandTimeout(60);
        }));
        s.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        // Sign-in cookies stay valid across restarts and between API instances: the key ring lives in SQL Server.
        s.AddDataProtection().SetApplicationName("MoET Recruitment").PersistKeysToDbContext<AppDbContext>();
        s.AddSingleton<IClock, SystemClock>();
        s.AddScoped<ICurrentActor, HttpCurrentActor>();
        s.AddScoped<IRequestLanguage, HttpRequestLanguage>();
        s.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        s.AddScoped<ICodeGenerator, SqlCodeGenerator>();
        s.AddSingleton<ITextCatalog, EmbeddedTextCatalog>();
        s.AddSingleton<IHtmlCleaner, HtmlCleaner>();
        s.AddSingleton<IMailEnvironment, EnvironmentMail>();
        s.AddSingleton<IMailSender, SmtpMailSender>();
        s.AddSingleton<ISecretProtector, AesSecretProtector>();
        s.AddHttpClient<ILanguageModel, OpenAiCompatibleModel>(c => c.Timeout = TimeSpan.FromMinutes(5));
        s.AddHttpClient<ICvReader, CvReader>(c => c.Timeout = TimeSpan.FromMinutes(5));
        if (runWorkers)
        {
            s.AddHostedService<EmailOutboxWorker>();
            s.AddHostedService<JobWorker>();
        }
        return s;
    }

    /// <summary>Applies schema scripts and creates the first administrator from ADMIN_EMAIL and ADMIN_PASSWORD.</summary>
    public static async Task PrepareDatabaseAsync(IServiceProvider services, IConfiguration cfg, ILogger logger, CancellationToken ct = default)
    {
        await SchemaMigrator.MigrateAsync(ConnectionString(cfg), logger, ct);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var portalUrl = (cfg["PORTAL_URL"] ?? "").Trim().TrimEnd('/');
        var settings = await db.PortalSettings.FirstAsync(ct);
        if (portalUrl.Length > 0 && settings.PortalUrl.Length == 0)
        {
            settings.PortalUrl = portalUrl;
            await db.SaveChangesAsync(ct);
        }
        var email = (cfg["ADMIN_EMAIL"] ?? "").Trim().ToLowerInvariant();
        var password = cfg["ADMIN_PASSWORD"] ?? "";
        if (email.Length > 0 && password.Length >= 12 && !await db.StaffUsers.AnyAsync(ct))
        {
            var now = DateTime.UtcNow;
            db.StaffUsers.Add(new StaffUser
            {
                Email = email, FullName = cfg["ADMIN_NAME"] ?? "Administrator", Role = StaffRole.Administrator,
                PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password), CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Created the first administrator {Email}", email);
        }
    }
}
