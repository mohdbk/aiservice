using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Operations;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record ComponentInput(string? Key, string? Name, string? NameEn, string? Purpose, string? Kind, string? ActionName,
    JsonElement Definition, bool IsActive, string? RowVersion);

/// <summary>The component builder: forms and approval cards that admins design for the recruitment agent.</summary>
public sealed class BackofficeComponentService(IAppDbContext db, ICurrentActor actor, IClock clock)
{
    static readonly Sorts<AgentComponent> ComponentSorts = new Sorts<AgentComponent>("updated")
        .Add("updated", c => c.UpdatedAt).Add("name", c => c.Name).Add("key", c => c.Key).Add("kind", c => c.Kind)
        .Add("action", c => c.ActionName).Add("active", c => c.IsActive);

    public Task<PageResult<object>> ListAsync(ListQuery lq, string? kind, bool? active, string? action, CancellationToken ct = default)
    {
        var q = db.AgentComponents.AsNoTracking().AsQueryable();
        if (Enum.TryParse<ComponentKind>(kind, out var k)) q = q.Where(c => c.Kind == k);
        if (active != null) q = q.Where(c => c.IsActive == active);
        if (!string.IsNullOrEmpty(action)) q = q.Where(c => c.ActionName == action);
        if (lq.Term.Length > 0)
        {
            var t = lq.Term;
            q = q.Where(c => c.Name.Contains(t) || c.NameEn.Contains(t) || c.Key.Contains(t) || c.Purpose.Contains(t));
        }
        return q.PageAsync(lq, ComponentSorts, c => (object)new
        {
            id = c.Id, key = c.Key, name = c.Name, name_en = c.NameEn, kind = c.Kind, action_name = c.ActionName, is_active = c.IsActive, updated_at = c.UpdatedAt,
        }, ct);
    }

    public async Task<object> GetAsync(int id, CancellationToken ct = default)
    {
        var c = await db.AgentComponents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        return Full(c);
    }

    /// <summary>Active components, for the agent (any signed-in staff member; the agent calls this as them).</summary>
    public async Task<object> ActiveAsync(CancellationToken ct = default)
    {
        var rows = await db.AgentComponents.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Key).ToListAsync(ct);
        return new { items = rows.Select(Full), actions = AgentActions.Editable };
    }

    public async Task<object> SaveAsync(int? id, ComponentInput input, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        AgentComponent c;
        if (id == null)
        {
            c = new AgentComponent { CreatedAt = now };
            db.AgentComponents.Add(c);
        }
        else
        {
            c = await db.AgentComponents.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
            if (!string.IsNullOrEmpty(input.RowVersion) && input.RowVersion != Convert.ToBase64String(c.RowVersion))
                throw DomainException.Conflict("changed_elsewhere");
        }
        c.Key = (input.Key ?? "").Trim().ToLowerInvariant();
        c.Name = TextUtil.NoEmDash(TextUtil.Clip(input.Name?.Trim(), 140));
        c.NameEn = TextUtil.NoEmDash(TextUtil.Clip(input.NameEn?.Trim(), 140));
        c.Purpose = TextUtil.NoEmDash(TextUtil.Clip(input.Purpose?.Trim(), 1000));
        c.Kind = BackofficeJobService.Parse<ComponentKind>(input.Kind, "kind");
        c.ActionName = string.IsNullOrWhiteSpace(input.ActionName) ? null : input.ActionName.Trim();
        c.Definition = input.Definition.ValueKind == JsonValueKind.Object ? TextUtil.NoEmDash(input.Definition.GetRawText()) : "{}";
        c.IsActive = input.IsActive;
        c.UpdatedAt = now;
        c.UpdatedByStaffId = actor.Id;
        c.Validate();

        if (await db.AgentComponents.AnyAsync(x => x.Id != c.Id && x.Key == c.Key, ct)) throw DomainException.Conflict("component_key_taken");
        if (c.IsActive && c.ActionName != null && await db.AgentComponents.AnyAsync(x => x.Id != c.Id && x.IsActive && x.ActionName == c.ActionName, ct))
            throw DomainException.Conflict("component_action_taken");
        await db.SaveChangesAsync(ct);
        return new { id = c.Id };
    }

    public async Task<object> DeleteAsync(int id, CancellationToken ct = default)
    {
        var c = await db.AgentComponents.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        db.AgentComponents.Remove(c);
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    static object Full(AgentComponent c) => new
    {
        id = c.Id, key = c.Key, name = c.Name, name_en = c.NameEn, purpose = c.Purpose, kind = c.Kind, action_name = c.ActionName,
        definition = JsonDocument.Parse(c.Definition).RootElement.Clone(), is_active = c.IsActive, updated_at = c.UpdatedAt,
        row_version = Convert.ToBase64String(c.RowVersion),
    };
}
