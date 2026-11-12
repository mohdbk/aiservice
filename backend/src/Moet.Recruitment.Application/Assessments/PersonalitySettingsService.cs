using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Assessments;
using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Application.Assessments;

public sealed record BandCutoffsInput(int Low, int Moderate, int High, int VeryHigh);

public sealed record TypeNoteInput(string? NameAr, string? NameEn, string? StrengthsAr, string? StrengthsEn, string? RisksAr, string? RisksEn);

/// <summary>
/// What HR can tune about personality results: the workplace Big Five band cut-offs (used when a questionnaire is scored and for
/// role profile targets) and the notes for each of the 16 types (HR's wording replaces the built-in notes).
/// </summary>
public sealed class PersonalitySettingsService(IAppDbContext db, ICurrentActor actor, IClock clock)
{
    BandCutoffs? cutoffs;
    Dictionary<string, TypeReport>? reports;

    /// <summary>The cut-offs in force (loaded once per request).</summary>
    public async Task<BandCutoffs> CutoffsAsync(CancellationToken ct = default) =>
        cutoffs ??= BandCutoffs.Parse(await db.PersonalitySettings.AsNoTracking().Select(s => s.BandCutoffs).FirstOrDefaultAsync(ct));

    /// <summary>The notes for every type: HR's wording where they changed it, the built-in notes otherwise.</summary>
    public async Task<IReadOnlyDictionary<string, TypeReport>> ReportsAsync(CancellationToken ct = default)
    {
        if (reports != null) return reports;
        var own = await db.TypeNotes.AsNoTracking().ToListAsync(ct);
        reports = TypeReports.All.ToDictionary(x => x.Key, x => own.FirstOrDefault(n => n.Type == x.Key)?.ToReport() ?? x.Value);
        return reports;
    }

    public async Task<object> GetAsync(CancellationToken ct = default)
    {
        var c = await CutoffsAsync(ct);
        var own = await db.TypeNotes.AsNoTracking().Select(n => n.Type).ToListAsync(ct);
        var all = await ReportsAsync(ct);
        return new
        {
            band_cutoffs = new { low = c.Low, moderate = c.Moderate, high = c.High, very_high = c.VeryHigh },
            default_cutoffs = new { low = BandCutoffs.Default.Low, moderate = BandCutoffs.Default.Moderate, high = BandCutoffs.Default.High, very_high = BandCutoffs.Default.VeryHigh },
            types = all.Values.OrderBy(r => r.Type).Select(r => new
            {
                type = r.Type, name_ar = r.NameAr, name_en = r.NameEn, strengths_ar = r.StrengthsAr, strengths_en = r.StrengthsEn,
                risks_ar = r.RisksAr, risks_en = r.RisksEn, customised = own.Contains(r.Type),
            }),
        };
    }

    public async Task<object> SaveCutoffsAsync(BandCutoffsInput input, CancellationToken ct = default)
    {
        var c = new BandCutoffs(input.Low, input.Moderate, input.High, input.VeryHigh);
        c.Validate();
        var row = await db.PersonalitySettings.FirstOrDefaultAsync(ct);
        if (row == null) db.PersonalitySettings.Add(row = new PersonalitySetting());
        row.BandCutoffs = c.ToString();
        row.UpdatedAt = clock.UtcNow;
        row.UpdatedByStaffId = actor.Id;
        await db.SaveChangesAsync(ct);
        cutoffs = c;
        return new { ok = true };
    }

    public async Task<object> SaveTypeAsync(string type, TypeNoteInput input, CancellationToken ct = default)
    {
        type = (type ?? "").Trim().ToUpperInvariant();
        var n = await db.TypeNotes.FirstOrDefaultAsync(x => x.Type == type, ct);
        if (n == null) db.TypeNotes.Add(n = new TypeNote { Type = type });
        static string Lines(string? s) => TextUtil.NoEmDash(TextUtil.Clip(string.Join("\n",
            (s ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)), 2000));
        n.NameAr = TextUtil.NoEmDash(TextUtil.Clip(input.NameAr?.Trim(), 100));
        n.NameEn = TextUtil.NoEmDash(TextUtil.Clip(input.NameEn?.Trim(), 100));
        n.StrengthsAr = Lines(input.StrengthsAr);
        n.StrengthsEn = Lines(input.StrengthsEn);
        n.RisksAr = Lines(input.RisksAr);
        n.RisksEn = Lines(input.RisksEn);
        n.UpdatedAt = clock.UtcNow;
        n.UpdatedByStaffId = actor.Id;
        n.Validate();
        await db.SaveChangesAsync(ct);
        reports = null;
        return new { ok = true };
    }

    /// <summary>Back to the built-in notes.</summary>
    public async Task<object> ResetTypeAsync(string type, CancellationToken ct = default)
    {
        var n = await db.TypeNotes.FirstOrDefaultAsync(x => x.Type == type.ToUpperInvariant(), ct);
        if (n != null)
        {
            db.TypeNotes.Remove(n);
            await db.SaveChangesAsync(ct);
        }
        reports = null;
        return new { ok = true };
    }
}
