using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Content;

namespace Moet.Recruitment.Application.Common;

public sealed record LanguageInfo(string Code, string Label, string Dir);

/// <summary>Languages, settings and portal wording (shipped text overlaid with approved HR overrides).</summary>
public sealed class ContentService(IAppDbContext db, ITextCatalog catalog, IMemoryCache cache)
{
    static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public async Task<(IReadOnlyList<LanguageInfo> Languages, string Default)> LanguagesAsync(CancellationToken ct = default)
    {
        var result = await cache.GetOrCreateAsync("content:languages", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = Ttl;
            var langs = await db.Languages.AsNoTracking().Where(l => l.IsEnabled).OrderBy(l => l.SortOrder).ToListAsync(ct);
            var def = await db.PortalSettings.AsNoTracking().Select(s => s.DefaultLanguageCode).FirstAsync(ct);
            var list = langs.Select(l => new LanguageInfo(l.Code, l.NativeName, l.IsRtl ? "rtl" : "ltr")).ToList();
            if (list.Count == 0) list.Add(new LanguageInfo("ar", "العربية", "rtl"));
            if (list.All(l => l.Code != def)) def = list[0].Code;
            return (list as IReadOnlyList<LanguageInfo>, def);
        });
        return result;
    }

    /// <summary>The language if enabled, otherwise unknown_language (404) with the list of languages.</summary>
    public async Task<string> RequireLanguageAsync(string? lang, CancellationToken ct = default)
    {
        var (langs, def) = await LanguagesAsync(ct);
        if (lang != null && langs.Any(l => l.Code == lang)) return lang;
        throw new DomainException("unknown_language", 404,
            new Dictionary<string, object?> { ["languages"] = langs.Select(l => l.Code).ToList(), ["default"] = def });
    }

    /// <summary>An enabled language, or the default one.</summary>
    public async Task<string> PortalLanguageAsync(string? lang, CancellationToken ct = default)
    {
        var (langs, def) = await LanguagesAsync(ct);
        return lang != null && langs.Any(l => l.Code == lang) ? lang : def;
    }

    public Task<PortalSetting> SettingsAsync(CancellationToken ct = default) =>
        cache.GetOrCreateAsync("content:settings", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = Ttl;
            return await db.PortalSettings.AsNoTracking().FirstAsync(ct);
        })!;

    public async Task<IReadOnlyDictionary<string, string>> PortalTextAsync(string lang, CancellationToken ct = default)
    {
        return (await cache.GetOrCreateAsync($"content:text:{lang}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = Ttl;
            var overrides = await db.PortalTexts.AsNoTracking()
                .Where(t => t.LanguageCode == lang && t.ReviewStatus == ReviewStatus.Approved)
                .ToDictionaryAsync(t => t.TextKey, t => t.Text, ct);
            return Merge(catalog.Shipped("en"), lang == "en" ? null : catalog.Shipped(lang), overrides);
        }))!;
    }

    /// <summary>English base, then the language file, then approved overrides. Unknown keys and empty values are ignored.</summary>
    public static IReadOnlyDictionary<string, string> Merge(IReadOnlyDictionary<string, string> @base, params IReadOnlyDictionary<string, string>?[] overlays)
    {
        var o = new Dictionary<string, string>(@base);
        foreach (var layer in overlays)
            foreach (var (k, v) in layer ?? new Dictionary<string, string>())
                if (o.ContainsKey(k) && !string.IsNullOrWhiteSpace(v)) o[k] = v;
        return o;
    }

    public void Invalidate()
    {
        if (cache is MemoryCache mc) mc.Clear();
        else
        {
            cache.Remove("content:languages");
            cache.Remove("content:settings");
        }
    }
}
