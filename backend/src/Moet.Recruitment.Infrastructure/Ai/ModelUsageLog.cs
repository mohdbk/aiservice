using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain.Operations;
using Moet.Recruitment.Infrastructure.Persistence;

namespace Moet.Recruitment.Infrastructure.Ai;

/// <summary>Saves each model call's token counts in its own scope, so it never mixes with the caller's unsaved changes.</summary>
public sealed class ModelUsageLog(IServiceScopeFactory scopes, ILogger<ModelUsageLog> log) : IModelUsageLog
{
    public async Task RecordAsync(string source, string feature, string model, int inputTokens, int outputTokens, int? staffId = null)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ModelUsages.Add(new ModelUsage
            {
                At = DateTime.UtcNow, Source = Clip(source, 20), Feature = Clip(feature, 60), Model = Clip(model, 120),
                InputTokens = Math.Max(0, inputTokens), OutputTokens = Math.Max(0, outputTokens), StaffUserId = staffId,
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception e) when (e is Microsoft.EntityFrameworkCore.DbUpdateException or InvalidOperationException or Microsoft.Data.SqlClient.SqlException)
        {
            // Use tracking must never break the feature that used the model.
            log.LogWarning(e, "Could not record model use");
        }
    }

    static string Clip(string s, int n) => s.Length <= n ? s : s[..n];
}
