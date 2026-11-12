using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Backoffice;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Assessments;
using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Application.Assessments;

public sealed record RoleProfileInput(string? NameAr, string? NameEn, string? PersonalityModel, IReadOnlyDictionary<string, int>? Weights,
    IReadOnlyDictionary<string, string>? Targets, IReadOnlyList<string>? Types, int StrongFrom, int ModerateFrom, bool IsActive, int SortOrder, string? RowVersion);

/// <summary>HR's role profiles for personality questionnaires, and the personality results worked out with them.</summary>
public sealed class RoleProfileService(IAppDbContext db, ICurrentActor actor, IClock clock)
{
    public async Task<object> ListAsync(string? model, CancellationToken ct = default)
    {
        var q = db.RoleProfiles.AsNoTracking().AsQueryable();
        if (Enum.TryParse<PersonalityModel>(model, out var m)) q = q.Where(r => r.PersonalityModel == m);
        var rows = await q.OrderBy(r => r.PersonalityModel).ThenBy(r => r.SortOrder).ThenBy(r => r.NameEn).ToListAsync(ct);
        return new { items = rows.Select(Full) };
    }

    public async Task<object> GetAsync(int id, CancellationToken ct = default) =>
        Full(await db.RoleProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound());

    public async Task<object> SaveAsync(int? id, RoleProfileInput input, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        RoleProfile r;
        if (id == null)
        {
            r = new RoleProfile { CreatedAt = now };
            db.RoleProfiles.Add(r);
        }
        else
        {
            r = await db.RoleProfiles.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
            if (!string.IsNullOrEmpty(input.RowVersion) && input.RowVersion != Convert.ToBase64String(r.RowVersion))
                throw DomainException.Conflict("changed_elsewhere");
        }
        r.NameAr = TextUtil.NoEmDash(TextUtil.Clip(input.NameAr?.Trim(), 150));
        r.NameEn = TextUtil.NoEmDash(TextUtil.Clip(input.NameEn?.Trim(), 150));
        r.PersonalityModel = BackofficeJobService.Parse<PersonalityModel>(input.PersonalityModel, "personality_model");
        r.Weights = JsonSerializer.Serialize(input.Weights ?? new Dictionary<string, int>());
        r.Targets = JsonSerializer.Serialize(input.Targets ?? new Dictionary<string, string>());
        r.Types = string.Join(",", input.Types ?? []);
        r.StrongFrom = (byte)Math.Clamp(input.StrongFrom, 0, 255);
        r.ModerateFrom = (byte)Math.Clamp(input.ModerateFrom, 0, 255);
        r.IsActive = input.IsActive;
        r.SortOrder = input.SortOrder;
        r.UpdatedAt = now;
        r.UpdatedByStaffId = actor.Id;
        r.Validate();
        await db.SaveChangesAsync(ct);
        return new { id = r.Id };
    }

    public async Task<object> DeleteAsync(int id, CancellationToken ct = default)
    {
        var r = await db.RoleProfiles.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        db.RoleProfiles.Remove(r);
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    static object Full(RoleProfile r) => new
    {
        id = r.Id, name_ar = r.NameAr, name_en = r.NameEn, personality_model = r.PersonalityModel.ToString(), weights = r.WeightMap(), targets = r.TargetMap(),
        types = r.TypeList(), strong_from = (int)r.StrongFrom, moderate_from = (int)r.ModerateFrom, is_active = r.IsActive, sort_order = r.SortOrder,
        updated_at = r.UpdatedAt, row_version = Convert.ToBase64String(r.RowVersion),
    };

    /// <summary>Active profiles, loaded once per request by the pages that show personality results.</summary>
    public Task<List<RoleProfile>> ActiveAsync(CancellationToken ct = default) =>
        db.RoleProfiles.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.SortOrder).ThenBy(r => r.NameEn).ToListAsync(ct);

    /// <summary>
    /// What the backoffice shows next to a trait profile: the 4-letter type and its notes (16-type questionnaire) and the fit
    /// with each active role profile of the same model. Null when there is no profile yet.
    /// </summary>
    public static object? Summary(PersonalityModel model, string? traitProfileJson, IReadOnlyList<RoleProfile> roles)
    {
        if (string.IsNullOrEmpty(traitProfileJson)) return null;
        var profile = JsonSerializer.Deserialize<List<TraitScore>>(traitProfileJson) ?? [];
        if (profile.Count == 0) return null;
        var type = model == PersonalityModel.WorkplaceType16 ? Personality.TypeOf(profile) : null;
        var report = type != null && TypeReports.All.TryGetValue(type.Value.Type, out var r) ? r : null;
        return new
        {
            type = type?.Type,
            pairs = type?.Pairs.Select(p => new { first = p.First, second = p.Second, first_mean = p.FirstMean, second_mean = p.SecondMean, letter = p.Letter }),
            report = report == null ? null : new
            {
                name_ar = report.NameAr, name_en = report.NameEn, strengths_ar = report.StrengthsAr, strengths_en = report.StrengthsEn,
                risks_ar = report.RisksAr, risks_en = report.RisksEn,
            },
            fits = roles.Select(role => role.Fit(model, profile)).Where(f => f != null).Select(f => new
            {
                profile_id = f!.ProfileId, name_ar = f.NameAr, name_en = f.NameEn, percent = f.Percent, level = f.Level, below_target = f.BelowTarget,
            }),
        };
    }
}
