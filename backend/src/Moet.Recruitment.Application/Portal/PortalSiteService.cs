using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;

namespace Moet.Recruitment.Application.Portal;

public sealed record SalaryView(string Currency, decimal Min, decimal Max, string Per);

public sealed record JobView(
    string Id, string Title, string ContentLang, string ContentDir, string Department, string Location, string EmploymentType,
    string PostedOn, string ClosesOn, SalaryView? Salary, string Summary, bool Accepting)
{
    public string? DescriptionHtml { get; init; }
    public IReadOnlyList<ScreeningQuestionView>? Screening { get; init; }
}

public sealed record OptionView(string Value, string Label);

public sealed record ScreeningQuestionView(string Key, string Type, bool Required, string Text, IReadOnlyList<OptionView> Options);

public sealed record JobListView(IReadOnlyList<JobView> Jobs, int Total, int Page, int Pages, JobFilters Filters);

public sealed record JobFilters(IReadOnlyList<string> Departments, IReadOnlyList<string> Locations);

/// <summary>What the career portal shows publicly: languages, wording, settings and published jobs.</summary>
public sealed class PortalSiteService(IAppDbContext db, ContentService content, IClock clock, IHtmlCleaner html)
{
    public const int PageSize = 10;
    public const string Version = "1.0.0";

    public async Task<Dictionary<string, object?>> ConfigAsync(string? lang, CancellationToken ct = default)
    {
        var (langs, def) = await content.LanguagesAsync(ct);
        var o = new Dictionary<string, object?> { ["languages"] = langs, ["default_language"] = def, ["version"] = Version };
        if (string.IsNullOrEmpty(lang)) return o;
        lang = await content.RequireLanguageAsync(lang, ct);
        var s = await content.SettingsAsync(ct);
        o["lang"] = lang;
        o["dir"] = TextUtil.Dir(lang);
        o["text"] = await content.PortalTextAsync(lang, ct);
        o["main_site_url"] = s.MainSiteUrl;
        o["logo"] = s.LogoFileId != null ? "/api/logo" : "";
        o["logo_dark"] = s.LogoDarkFileId != null ? "/api/logo?dark=1" : "";
        o["allow_signup"] = s.AllowSignup;
        o["max_cv_mb"] = (int)s.MaxCvMb;
        o["countries"] = Countries.All;
        return o;
    }

    IQueryable<JobOpening> Listed()
    {
        var today = clock.Today();
        return db.JobOpenings.AsNoTracking()
            .Where(j => j.IsPublished && j.Status == JobStatus.Open && (j.ClosesOn == null || j.ClosesOn >= today));
    }

    public async Task<JobListView> JobsAsync(string? lang, string? q, string? department, string? location, int page, CancellationToken ct = default)
    {
        lang = await content.RequireLanguageAsync(lang, ct);
        var jobs = await Listed()
            .Include(j => j.Department).ThenInclude(d => d!.Translations)
            .Include(j => j.JobLocation).ThenInclude(l => l!.Translations)
            .Include(j => j.Translations)
            .OrderByDescending(j => j.PostedOn).ThenByDescending(j => j.CreatedAt)
            .Take(500).ToListAsync(ct);
        var filters = new JobFilters(
            jobs.Where(j => j.Department != null).Select(j => j.Department!.NameIn(lang)).Distinct().Order().ToList(),
            jobs.Select(j => j.LocationIn(lang)).Where(l => l.Length > 0).Distinct().Order().ToList());
        IEnumerable<JobOpening> rows = jobs;
        if (!string.IsNullOrWhiteSpace(department))
            rows = rows.Where(j => j.Department != null && j.Department.NameIn(lang).StartsWith(department.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(location)) rows = rows.Where(j => j.LocationIn(lang) == location.Trim() || j.Location == location.Trim());
        var term = TextUtil.Clip(q?.Trim(), 100);
        if (term.Length > 0)
            rows = rows.Where(j => Matches(j.Title, term) || Matches(TextUtil.StripHtml(j.Description), term)
                                   || j.Translations.Any(t => t.ReviewStatus == ReviewStatus.Approved && (Matches(t.Title, term) || Matches(TextUtil.StripHtml(t.Description), term))));
        var list = rows.ToList();
        var total = list.Count;
        page = Math.Max(1, page);
        var chunk = list.Skip((page - 1) * PageSize).Take(PageSize).Select(j => Present(j, lang, false)).ToList();
        return new JobListView(chunk, total, page, Math.Max(1, (total + PageSize - 1) / PageSize), filters);
    }

    static bool Matches(string text, string term) => text.Contains(term, StringComparison.OrdinalIgnoreCase);

    public async Task<JobView> JobAsync(string? lang, string? code, CancellationToken ct = default)
    {
        lang = await content.RequireLanguageAsync(lang, ct);
        var job = await LoadPublishedAsync(code, ct);
        var view = Present(job, lang, true);
        var asked = Asked(job);
        return view with { Screening = asked.Select(a => Public(a, lang)).ToList() };
    }

    internal async Task<JobOpening> LoadPublishedAsync(string? code, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code)) throw DomainException.NotFound("job_not_found");
        return await db.JobOpenings.AsNoTracking()
                   .Include(j => j.Department).ThenInclude(d => d!.Translations)
            .Include(j => j.JobLocation).ThenInclude(l => l!.Translations)
                   .Include(j => j.Translations)
                   .Include(j => j.ScreeningQuestions).ThenInclude(s => s.ScreeningQuestion!).ThenInclude(q => q.Options).ThenInclude(o => o.Translations)
                   .Include(j => j.ScreeningQuestions).ThenInclude(s => s.ScreeningQuestion!).ThenInclude(q => q.Translations)
                   .AsSplitQuery()
                   .FirstOrDefaultAsync(j => j.Code == code && j.IsPublished, ct)
               ?? throw DomainException.NotFound("job_not_found");
    }

    /// <summary>The job's active screening questions, in order, keyed sq1, sq2...</summary>
    internal static List<AskedQuestion> Asked(JobOpening job) =>
        job.ScreeningQuestions.Where(s => s.ScreeningQuestion is { IsActive: true }).OrderBy(s => s.SortOrder)
            .Select((s, i) => new AskedQuestion($"sq{i + 1}", s.ScreeningQuestion!, s.IsRequired, s.RequirementRule, s.ExpectedValue))
            .ToList();

    static ScreeningQuestionView Public(AskedQuestion a, string lang)
    {
        var q = a.Question;
        var approved = lang != q.SourceLanguageCode && q.Translations.Any(t => t.LanguageCode == lang && t.ReviewStatus == ReviewStatus.Approved);
        var text = approved ? q.Translations.First(t => t.LanguageCode == lang).Text : q.Text;
        var options = q.AnswerType == AnswerType.YesNo
            ? new List<OptionView> { new(AskedQuestion.Yes, AskedQuestion.Yes), new(AskedQuestion.No, AskedQuestion.No) }
            : q.Options.OrderBy(o => o.SortOrder).Select(o => new OptionView(o.Id.ToString(CultureInfo.InvariantCulture),
                approved && o.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.Text is { Length: > 0 } l ? l : o.Text)).ToList();
        return new ScreeningQuestionView(a.Key, Labels.Wire(q.AnswerType), a.Required, string.IsNullOrWhiteSpace(text) ? q.Text : text, options);
    }

    JobView Present(JobOpening j, string lang, bool full)
    {
        var (title, desc, contentLang) = j.In(lang);
        SalaryView? salary = j.ShowSalary && (j.SalaryMin != null || j.SalaryMax != null)
            ? new SalaryView(j.SalaryCurrency, j.SalaryMin ?? 0, j.SalaryMax ?? 0, j.SalaryPeriod == SalaryPeriod.Year ? "Year" : "Month")
            : null;
        return new JobView(j.Code, title, contentLang, TextUtil.Dir(contentLang), j.Department?.NameIn(lang) ?? "", j.LocationIn(lang),
            Labels.Employment(j.EmploymentType, lang), AppTime.Date(j.PostedOn), AppTime.Date(j.ClosesOn), salary,
            TextUtil.Summary(desc), j.IsAccepting(clock.Today()))
        {
            DescriptionHtml = full ? html.Clean(desc) : null,
        };
    }

    public async Task<(byte[] Content, string ContentType, string FileName)> LogoAsync(bool dark, CancellationToken ct = default)
    {
        var s = await content.SettingsAsync(ct);
        var id = (dark ? s.LogoDarkFileId : s.LogoFileId) ?? s.LogoFileId ?? throw DomainException.NotFound("no_logo");
        var f = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("no_logo");
        return (f.Content, f.ContentType, f.FileName);
    }
}
