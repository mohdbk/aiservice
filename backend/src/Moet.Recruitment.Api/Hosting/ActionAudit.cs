using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Operations;
using Moet.Recruitment.Infrastructure.Persistence;

namespace Moet.Recruitment.Api.Hosting;

/// <summary>
/// Logs every action people take through the API, next to the record changes it caused (same request id): all changes
/// (POST, PUT, DELETE, including sign-ins and failed attempts), and reads of personal data (an applicant's record, CVs, letters,
/// exports, data copies, attachments). Each says who, from where, whether through the assistant, and the result.
/// </summary>
public sealed partial class ActionAudit(RequestDelegate next, ILogger<ActionAudit> log)
{
    [GeneratedRegex(@"^/api/backoffice/(applicants/\d+(/cv)?|documents/.+/pdf|reports/hires\.csv|offers/\d+/handoff|agent/attachments/.+|audit|data-requests|consents)$|^/api/portal/(my_data|application|assessment)$")]
    private static partial Regex SensitiveRead();

    [GeneratedRegex(@"^/api/portal/(save_assessment_answer|assessment_event|upload_assessment_file|logo)$|^/api/backoffice/agent/conversations/[^/]+$")]
    private static partial Regex Noise();

    public async Task InvokeAsync(HttpContext ctx, AppDbContext db, ICurrentActor actor, IAuditContext audit, IClock clock)
    {
        await next(ctx);
        var path = ctx.Request.Path.Value ?? "";
        if (!path.StartsWith("/api/backoffice", StringComparison.Ordinal) && !path.StartsWith("/api/portal", StringComparison.Ordinal)) return;
        var method = ctx.Request.Method;
        var write = !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method);
        // Reads are logged for personal data, and every read the assistant makes on someone's behalf.
        if (!write && !SensitiveRead().IsMatch(path) && audit.Via != "Assistant") return;
        // Saving each test answer and the chat's own history are not actions worth a line each.
        if (Noise().IsMatch(path)) return;
        try
        {
            var route = (ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? path;
            var portalMethod = path.StartsWith("/api/portal/", StringComparison.Ordinal) ? path["/api/portal/".Length..] : null;
            var label = portalMethod != null ? $"portal {portalMethod}" : Regex.Replace(route.Replace("/api/backoffice/", ""), @":[a-z]+\}", "}");
            var values = ctx.GetRouteData().Values;
            var key = values.TryGetValue("id", out var id) ? id?.ToString() ?? "" : values.TryGetValue("thread", out var th) ? th?.ToString() ?? "" : "";
            var record = portalMethod != null ? "Portal" : route.Replace("/api/backoffice/", "").Split('/')[0];
            db.AuditEntries.Add(new AuditEntry
            {
                EntityName = record.Length > 100 ? record[..100] : record, EntityKey = key.Length > 60 ? key[..60] : key, Action = AuditAction.Action, Changes = "{}",
                ActorType = actor.Type, ActorId = actor.Id, ActorName = actor.Name.Length > 0 ? actor.Name : (ctx.User.Identity?.Name ?? ""),
                OccurredAt = clock.UtcNow, Description = $"{method} {label} ({ctx.Response.StatusCode})",
                Via = audit.Via, Comment = audit.Comment.Length > 1000 ? audit.Comment[..1000] : audit.Comment, ProposalId = audit.ProposalId,
                RequestId = audit.RequestId, IpAddress = audit.IpAddress,
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception e) when (e is Microsoft.EntityFrameworkCore.DbUpdateException or InvalidOperationException)
        {
            // The audit trail must never break the request itself.
            log.LogWarning(e, "Could not record the action {Path}", path);
        }
    }
}
