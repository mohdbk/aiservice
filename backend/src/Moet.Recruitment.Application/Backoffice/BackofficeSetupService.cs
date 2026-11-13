using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Content;
using Moet.Recruitment.Domain.Hiring;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record ScreeningOptionInput(int? Id, string Text, string? TextEn);

public sealed record ScreeningQuestionInput(string Text, string? TextEn, bool EnglishApproved, string AnswerType, string? Category, bool IsActive,
    IReadOnlyList<ScreeningOptionInput>? Options);

public sealed record SettingsInput(string MainSiteUrl, string PortalUrl, string DefaultLanguageCode, bool AllowSignup, bool RequireEmailVerification,
    int MaxCvMb, bool AutoScoreApplications, string? TimeZone = null);

public sealed record PortalTextInput(string Lang, string Key, string Text, bool Approved);

public sealed record LocationInput(string Name, string? NameEn, bool IsActive, int SortOrder);

public sealed record CountryInput(string? Code, string? NameAr, string? NameEn, bool IsActive, int SortOrder);

public sealed record NamedInput(string Name, string? NameEn, bool IsActive, string? Description, IReadOnlyList<string>? Skills);

/// <summary>Screening questions, dashboard, lookups and settings.</summary>
public sealed class BackofficeSetupService(IAppDbContext db, ICurrentActor actor, IRequestLanguage language, IClock clock, ContentService content, ITextCatalog catalog, JobQueue jobs)
{
    const string En = "en";

    // ---------------- screening questions ----------------

    static readonly Sorts<ScreeningQuestion> ScreeningSorts = new Sorts<ScreeningQuestion>("updated")
        .Add("updated", q => q.UpdatedAt).Add("text", q => q.Text).Add("type", q => q.AnswerType).Add("category", q => q.Category);

    public async Task<object> ScreeningQuestionsAsync(ListQuery lq, string? type, string? category, bool? active, string? english, CancellationToken ct = default)
    {
        var q = db.ScreeningQuestions.AsNoTracking().AsQueryable();
        if (Enum.TryParse<AnswerType>(type, out var at)) q = q.Where(x => x.AnswerType == at);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(x => x.Category == category);
        if (active != null) q = q.Where(x => x.IsActive == active);
        if (english == "Missing") q = q.Where(x => !x.Translations.Any(t => t.LanguageCode == En));
        else if (Enum.TryParse<ReviewStatus>(english, out var rs)) q = q.Where(x => x.Translations.Any(t => t.LanguageCode == En && t.ReviewStatus == rs));
        if (lq.Term.Length > 0) q = q.Where(x => x.Text.Contains(lq.Term) || x.Translations.Any(t => t.Text.Contains(lq.Term)));
        var en = language.English;
        return await q.PageAsync(lq, ScreeningSorts, x => new
        {
            id = x.Id, text = en ? x.Translations.Where(t => t.LanguageCode == En && t.Text != "").Select(t => t.Text).FirstOrDefault() ?? x.Text : x.Text, answer_type = x.AnswerType, category = x.Category, is_active = x.IsActive,
            english = x.Translations.Where(t => t.LanguageCode == En).Select(t => (ReviewStatus?)t.ReviewStatus).FirstOrDefault(),
            options = x.Options.OrderBy(o => o.SortOrder).Select(o => o.Text).ToList(),
        }, ct);
    }

    public async Task<object> ScreeningQuestionAsync(int id, CancellationToken ct = default)
    {
        var q = await LoadScreening(id).AsNoTracking().FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
        var tr = q.Translations.FirstOrDefault(t => t.LanguageCode == En);
        return new
        {
            id = q.Id, text = q.Text, text_en = tr?.Text ?? "", english_status = tr?.ReviewStatus.ToString() ?? "Missing",
            english_approved = tr?.ReviewStatus == ReviewStatus.Approved, answer_type = q.AnswerType.ToString(), category = q.Category,
            is_active = q.IsActive,
            options = q.Options.OrderBy(o => o.SortOrder).Select(o => new { id = o.Id, text = o.Text, text_en = o.Translations.FirstOrDefault(t => t.LanguageCode == En)?.Text ?? "" }).ToList(),
        };
    }

    IQueryable<ScreeningQuestion> LoadScreening(int id) => db.ScreeningQuestions.Where(q => q.Id == id)
        .Include(q => q.Options).ThenInclude(o => o.Translations).Include(q => q.Translations).AsSplitQuery();

    public async Task<object> SaveScreeningQuestionAsync(int? id, ScreeningQuestionInput input, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        ScreeningQuestion q;
        if (id == null)
        {
            q = new ScreeningQuestion { CreatedAt = now, SourceLanguageCode = "ar" };
            db.ScreeningQuestions.Add(q);
        }
        else q = await LoadScreening(id.Value).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
        q.Text = TextUtil.Clip(input.Text?.Trim(), 1000);
        q.AnswerType = BackofficeJobService.Parse<AnswerType>(input.AnswerType, "answer_type");
        q.Category = TextUtil.Clip(input.Category?.Trim(), 100);
        q.IsActive = input.IsActive;
        q.UpdatedAt = now;
        // Options keep their ids (answers and requirements refer to them); removed ones are deleted.
        var incoming = (input.Options ?? []).Where(o => o.Text.Trim().Length > 0).ToList();
        q.Options.RemoveAll(o => o.Id != 0 && incoming.All(i => i.Id != o.Id));
        var order = 0;
        foreach (var oi in incoming)
        {
            var o = oi.Id == null ? null : q.Options.FirstOrDefault(x => x.Id == oi.Id);
            if (o == null) q.Options.Add(o = new ScreeningQuestionOption());
            o.SortOrder = order++;
            o.Text = TextUtil.Clip(oi.Text.Trim(), 300);
            var t = o.Translations.FirstOrDefault(x => x.LanguageCode == En);
            if (string.IsNullOrWhiteSpace(oi.TextEn)) { if (t != null) o.Translations.Remove(t); }
            else
            {
                if (t == null) o.Translations.Add(t = new ScreeningQuestionOptionTranslation { LanguageCode = En });
                t.Text = TextUtil.Clip(oi.TextEn.Trim(), 300);
            }
        }
        q.Validate();
        var tr = q.Translations.FirstOrDefault(t => t.LanguageCode == En);
        if (string.IsNullOrWhiteSpace(input.TextEn))
        {
            if (tr != null) q.Translations.Remove(tr);
        }
        else
        {
            if (tr == null) q.Translations.Add(tr = new ScreeningQuestionTranslation { LanguageCode = En, ReviewStatus = ReviewStatus.Suggested });
            tr.Text = TextUtil.Clip(input.TextEn.Trim(), 1000);
            var hash = TextUtil.ContentHash(tr.Text, string.Join("\n", q.Options.OrderBy(o => o.SortOrder).Select(o => o.Translations.FirstOrDefault(x => x.LanguageCode == En)?.Text ?? "")));
            TranslationReview.Apply(tr, input.EnglishApproved, false, hash, actor.Id, now);
        }
        await db.SaveChangesAsync(ct);
        return new { id = q.Id };
    }

    /// <summary>Queues an English draft from the language model for a screening question, test question or test.</summary>
    public async Task<object> SuggestEnglishAsync(string kind, int id, CancellationToken ct = default)
    {
        var type = kind switch
        {
            "screening" => JobTypes.SuggestScreeningTranslation,
            "question" => JobTypes.SuggestQuestionTranslation,
            "assessment" => JobTypes.SuggestAssessmentTranslation,
            "job" => JobTypes.SuggestJobTranslation,
            _ => throw DomainException.NotFound(),
        };
        await jobs.EnqueueAsync(type, id, ct);
        await db.SaveChangesAsync(ct);
        return new { queued = true };
    }

    // ---------------- dashboard and lookups ----------------

    public async Task<object> DashboardAsync(CancellationToken ct = default)
    {
        var en = language.English;
        var now = clock.UtcNow;
        var weekAgo = now.AddDays(-7);
        var today = clock.Today();
        return new
        {
            open_jobs = await db.JobOpenings.CountAsync(j => j.Status == JobStatus.Open, ct),
            published_jobs = await db.JobOpenings.CountAsync(j => j.Status == JobStatus.Open && j.IsPublished && (j.ClosesOn == null || j.ClosesOn >= today), ct),
            new_applicants = await db.JobApplications.CountAsync(a => a.CreatedAt >= weekAgo, ct),
            to_review = await db.JobApplications.CountAsync(a => a.Status == ApplicationStatus.Open, ct),
            upcoming_interviews = await db.Interviews.CountAsync(i => i.StartsAt >= now && i.Status == InterviewStatus.Scheduled, ct),
            to_mark = await db.Attempts.CountAsync(a => a.Status == AttemptStatus.Submitted, ct),
            offers_out = await db.JobOffers.CountAsync(o => o.Status == OfferStatus.Sent, ct),
            english_to_review = await db.JobOpeningTranslations.CountAsync(t => t.ReviewStatus == ReviewStatus.Suggested, ct),
            recent = await db.JobApplications.AsNoTracking().OrderByDescending(a => a.CreatedAt).Take(8).Select(a => new
            {
                id = a.Id, full_name = a.FullName,
                job_title = en ? a.JobOpening!.Translations.Where(t => t.LanguageCode == En && t.Title != "").Select(t => t.Title).FirstOrDefault() ?? a.JobOpening.Title : a.JobOpening!.Title,
                created_at = a.CreatedAt,
                score = a.CvAnalysis == null ? null : a.CvAnalysis.Score, status = a.Status,
            }).ToListAsync(ct),
            interviews = await db.Interviews.AsNoTracking().Where(i => i.StartsAt >= now && i.Status == InterviewStatus.Scheduled)
                .OrderBy(i => i.StartsAt).Take(6).Select(i => new { id = i.Id, starts_at = i.StartsAt, applicant = i.JobApplication!.FullName,
                    round = en && i.InterviewRound!.NameEn != "" ? i.InterviewRound.NameEn : i.InterviewRound!.Name })
                .ToListAsync(ct),
        };
    }

    public async Task<object> LookupsAsync(CancellationToken ct = default)
    {
        var en = language.English;
        return new
        {
        departments = await db.Departments.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Name)
            .Select(d => new
            {
                id = d.Id, name = en ? d.Translations.Where(t => t.LanguageCode == En && t.Name != "").Select(t => t.Name).FirstOrDefault() ?? d.Name : d.Name,
                name_en = d.Translations.Where(t => t.LanguageCode == En).Select(t => t.Name).FirstOrDefault() ?? "",
            }).ToListAsync(ct),
        locations = await db.JobLocations.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .Select(d => new
            {
                id = d.Id, name = en ? d.Translations.Where(t => t.LanguageCode == En && t.Name != "").Select(t => t.Name).FirstOrDefault() ?? d.Name : d.Name,
                name_ar = d.Name, name_en = d.Translations.Where(t => t.LanguageCode == En).Select(t => t.Name).FirstOrDefault() ?? "",
            }).ToListAsync(ct),
        rounds = await db.InterviewRounds.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Name)
            .Select(r => new { id = r.Id, name = en && r.NameEn != "" ? r.NameEn : r.Name, name_ar = r.Name, name_en = r.NameEn }).ToListAsync(ct),
        staff = await db.StaffUsers.AsNoTracking().Where(u => u.IsEnabled).OrderBy(u => u.FullName).Select(u => new { id = u.Id, name = u.FullName, email = u.Email }).ToListAsync(ct),
        assessments = await db.Assessments.AsNoTracking().Where(a => a.Status == AssessmentStatus.Published).OrderBy(a => a.Title)
            .Select(a => new { id = a.Id, title = en ? a.Translations.Where(t => t.LanguageCode == En && t.Title != "").Select(t => t.Title).FirstOrDefault() ?? a.Title : a.Title }).ToListAsync(ct),
        screening_questions = await db.ScreeningQuestions.AsNoTracking().Where(q => q.IsActive).OrderBy(q => q.Text)
            .Select(q => new { id = q.Id, text = q.Text, answer_type = q.AnswerType, options = q.Options.OrderBy(o => o.SortOrder).Select(o => new { id = o.Id, text = o.Text }).ToList() })
            .ToListAsync(ct),
        categories = await db.Questions.AsNoTracking().Where(x => x.Category != "").Select(x => x.Category).Distinct().OrderBy(x => x).ToListAsync(ct),
        job_families = await db.Questions.AsNoTracking().Where(x => x.JobFamily != "").Select(x => x.JobFamily).Distinct().OrderBy(x => x).ToListAsync(ct),
        };
    }

    // ---------------- departments and interview rounds ----------------

    public Task<PageResult<object>> DepartmentsAsync(ListQuery lq, CancellationToken ct = default)
    {
        var q = db.Departments.AsNoTracking().AsQueryable();
        if (lq.Term.Length > 0) q = q.Where(d => d.Name.Contains(lq.Term) || d.Translations.Any(t => t.Name.Contains(lq.Term)));
        return q.PageAsync(lq, new Sorts<Department>("name", false).Add("name", d => d.Name).Add("active", d => d.IsActive), d => (object)new
        {
            id = d.Id, name = d.Name, name_en = d.Translations.Where(t => t.LanguageCode == En).Select(t => t.Name).FirstOrDefault() ?? "", is_active = d.IsActive,
        }, ct);
    }

    public async Task<object> SaveDepartmentAsync(int? id, NamedInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw DomainException.Invalid("name_required");
        Department d;
        if (id == null) db.Departments.Add(d = new Department { SourceLanguageCode = "ar" });
        else d = await db.Departments.Include(x => x.Translations).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        d.Name = TextUtil.Clip(input.Name.Trim(), 140);
        d.IsActive = input.IsActive;
        var t = d.Translations.FirstOrDefault(x => x.LanguageCode == En);
        if (string.IsNullOrWhiteSpace(input.NameEn)) { if (t != null) d.Translations.Remove(t); }
        else
        {
            if (t == null) d.Translations.Add(t = new DepartmentTranslation { LanguageCode = En });
            t.Name = TextUtil.Clip(input.NameEn.Trim(), 140);
        }
        await db.SaveChangesAsync(ct);
        return new { id = d.Id };
    }

    public Task<PageResult<object>> LocationsAsync(ListQuery lq, CancellationToken ct = default)
    {
        var q = db.JobLocations.AsNoTracking().AsQueryable();
        if (lq.Term.Length > 0) q = q.Where(d => d.Name.Contains(lq.Term) || d.Translations.Any(t => t.Name.Contains(lq.Term)));
        return q.PageAsync(lq, new Sorts<JobLocation>("order", false).Add("order", d => d.SortOrder).Add("name", d => d.Name).Add("active", d => d.IsActive), d => (object)new
        {
            id = d.Id, name = d.Name, name_en = d.Translations.Where(t => t.LanguageCode == En).Select(t => t.Name).FirstOrDefault() ?? "", is_active = d.IsActive,
            sort_order = d.SortOrder, jobs = db.JobOpenings.Count(j => j.LocationId == d.Id),
        }, ct);
    }

    public async Task<object> SaveLocationAsync(int? id, LocationInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw DomainException.Invalid("name_required");
        JobLocation d;
        if (id == null) db.JobLocations.Add(d = new JobLocation { SourceLanguageCode = "ar" });
        else d = await db.JobLocations.Include(x => x.Translations).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        d.Name = TextUtil.NoEmDash(TextUtil.Clip(input.Name.Trim(), 140));
        d.IsActive = input.IsActive;
        d.SortOrder = input.SortOrder;
        var t = d.Translations.FirstOrDefault(x => x.LanguageCode == En);
        if (string.IsNullOrWhiteSpace(input.NameEn)) { if (t != null) d.Translations.Remove(t); }
        else
        {
            if (t == null) d.Translations.Add(t = new JobLocationTranslation { LanguageCode = En });
            t.Name = TextUtil.NoEmDash(TextUtil.Clip(input.NameEn.Trim(), 140));
        }
        await db.SaveChangesAsync(ct);
        return new { id = d.Id };
    }

    // ---------------- countries ----------------

    public Task<PageResult<object>> CountriesAsync(ListQuery lq, bool? active, CancellationToken ct = default)
    {
        var q = db.Countries.AsNoTracking().AsQueryable();
        if (active != null) q = q.Where(c => c.IsActive == active);
        if (lq.Term.Length > 0) q = q.Where(c => c.NameEn.Contains(lq.Term) || c.NameAr.Contains(lq.Term) || c.Code == lq.Term.ToUpper());
        var sorts = new Sorts<Country>("order", false).Add("order", c => c.SortOrder).Add("name_en", c => c.NameEn).Add("name_ar", c => c.NameAr).Add("code", c => c.Code);
        return q.PageAsync(lq, sorts, c => (object)new
        {
            id = c.Id, code = c.Code, name_ar = c.NameAr, name_en = c.NameEn, is_active = c.IsActive, sort_order = c.SortOrder,
            applications = db.JobApplications.Count(a => a.Country == c.NameEn || a.Nationality == c.NameEn),
        }, ct);
    }

    public async Task<object> SaveCountryAsync(int? id, CountryInput input, CancellationToken ct = default)
    {
        var code = (input.Code ?? "").Trim().ToUpperInvariant();
        var ar = TextUtil.NoEmDash(TextUtil.Clip(input.NameAr?.Trim(), 140));
        var en = TextUtil.NoEmDash(TextUtil.Clip(input.NameEn?.Trim(), 140));
        if (code.Length != 2 || !code.All(char.IsAsciiLetterUpper)) throw DomainException.Invalid("invalid_value", new Dictionary<string, object?> { ["field"] = "code" });
        if (ar.Length == 0 || en.Length == 0) throw DomainException.Invalid("name_required");
        if (await db.Countries.AnyAsync(c => c.Id != (id ?? 0) && (c.Code == code || c.NameEn == en), ct)) throw DomainException.Conflict("country_exists");
        Country c;
        if (id == null) db.Countries.Add(c = new Country());
        else c = await db.Countries.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        var oldEn = c.NameEn;
        c.Code = code;
        c.NameAr = ar;
        c.NameEn = en;
        c.IsActive = input.IsActive;
        c.SortOrder = Math.Clamp(input.SortOrder, -1000, 1000);
        // Applications keep the English name: a renamed country is renamed on them too.
        if (id != null && oldEn.Length > 0 && oldEn != en)
        {
            await db.JobApplications.Where(a => a.Country == oldEn).ExecuteUpdateAsync(u => u.SetProperty(a => a.Country, en), ct);
            await db.JobApplications.Where(a => a.Nationality == oldEn).ExecuteUpdateAsync(u => u.SetProperty(a => a.Nationality, en), ct);
        }
        await db.SaveChangesAsync(ct);
        return new { id = c.Id };
    }

    public Task<PageResult<object>> RoundsAsync(ListQuery lq, CancellationToken ct = default)
    {
        var q = db.InterviewRounds.AsNoTracking().AsQueryable();
        if (lq.Term.Length > 0) q = q.Where(r => r.Name.Contains(lq.Term) || r.NameEn.Contains(lq.Term));
        return q.PageAsync(lq, new Sorts<InterviewRound>("name", false).Add("name", r => r.Name).Add("active", r => r.IsActive), r => (object)new
        {
            id = r.Id, name = r.Name, name_en = r.NameEn, description = r.Description, is_active = r.IsActive, skills = r.Skills.OrderBy(s => s.SortOrder).Select(s => s.Skill).ToList(),
        }, ct);
    }

    public async Task<object> SaveRoundAsync(int? id, NamedInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw DomainException.Invalid("name_required");
        InterviewRound r;
        if (id == null) db.InterviewRounds.Add(r = new InterviewRound());
        else r = await db.InterviewRounds.Include(x => x.Skills).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        r.Name = TextUtil.Clip(input.Name.Trim(), 140);
        r.NameEn = TextUtil.Clip(input.NameEn?.Trim(), 140);
        r.Description = TextUtil.Clip(input.Description?.Trim(), 1000);
        r.IsActive = input.IsActive;
        r.Skills.Clear();
        var n = 0;
        foreach (var s in (input.Skills ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct())
            r.Skills.Add(new InterviewRoundSkill { SortOrder = n++, Skill = TextUtil.Clip(s, 140) });
        await db.SaveChangesAsync(ct);
        return new { id = r.Id };
    }

    // ---------------- settings and portal wording ----------------

    public async Task<object> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.PortalSettings.AsNoTracking().FirstAsync(ct);
        return new
        {
            s.MainSiteUrl, s.PortalUrl, s.DefaultLanguageCode, s.AllowSignup, s.RequireEmailVerification, max_cv_mb = (int)s.MaxCvMb,
            s.AutoScoreApplications, has_logo = s.LogoFileId != null,
            time_zone = clock.Zone.Id, time_zone_setting = s.TimeZone,
            time_zones = TimeZones.Select(z => new { id = z, offset = Offset(z) }),
            languages = await db.Languages.AsNoTracking().OrderBy(l => l.SortOrder).Select(l => new { l.Code, l.NativeName, l.IsEnabled }).ToListAsync(ct),
        };
    }

    public async Task<object> SaveSettingsAsync(SettingsInput input, CancellationToken ct = default)
    {
        var s = await db.PortalSettings.FirstAsync(ct);
        if (!await db.Languages.AnyAsync(l => l.Code == input.DefaultLanguageCode && l.IsEnabled, ct)) throw DomainException.Invalid("invalid_value");
        s.MainSiteUrl = Url(input.MainSiteUrl);
        s.PortalUrl = Url(input.PortalUrl);
        s.DefaultLanguageCode = input.DefaultLanguageCode;
        s.AllowSignup = input.AllowSignup;
        s.RequireEmailVerification = input.RequireEmailVerification;
        s.MaxCvMb = (byte)Math.Clamp(input.MaxCvMb, 1, 50);
        s.AutoScoreApplications = input.AutoScoreApplications;
        if (input.TimeZone != null)
        {
            var zone = input.TimeZone.Trim();
            if (zone.Length > 0 && (zone.Length > 64 || !clock.UseZone(zone))) throw DomainException.Invalid("invalid_value", new Dictionary<string, object?> { ["field"] = "time_zone" });
            if (zone.Length == 0) clock.UseZone("");
            s.TimeZone = zone;
        }
        s.UpdatedAt = clock.UtcNow;
        s.UpdatedByStaffId = actor.Id;
        await db.SaveChangesAsync(ct);
        content.Invalidate();
        return new { ok = true };
    }

    /// <summary>Time zones offered in Settings: the Gulf and the places the ministry's offices and candidates are most often in.</summary>
    static readonly string[] TimeZones =
    [
        "Asia/Dubai", "Asia/Riyadh", "Asia/Qatar", "Asia/Bahrain", "Asia/Kuwait", "Asia/Muscat", "Africa/Cairo", "Asia/Amman", "Asia/Beirut",
        "Asia/Karachi", "Asia/Kolkata", "Asia/Singapore", "Asia/Shanghai", "Asia/Tokyo", "Europe/London", "Europe/Paris", "Europe/Berlin",
        "Europe/Istanbul", "America/New_York", "America/Los_Angeles", "Australia/Sydney", "UTC",
    ];

    static string Offset(string id)
    {
        try
        {
            var o = TimeZoneInfo.FindSystemTimeZoneById(id).GetUtcOffset(DateTime.UtcNow);
            return $"UTC{(o < TimeSpan.Zero ? "-" : "+")}{o.Duration():hh\\:mm}";
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return ""; }
    }

    static string Url(string? u)
    {
        u = (u ?? "").Trim().TrimEnd('/');
        if (u.Length == 0) return "";
        return Uri.TryCreate(u, UriKind.Absolute, out var x) && x.Scheme is "https" or "http" ? u : throw DomainException.Invalid("invalid_url");
    }

    public async Task<object> PortalTextAsync(string lang, ListQuery lq, string? changed, CancellationToken ct = default)
    {
        var shipped = catalog.Shipped(lang);
        var english = catalog.Shipped("en");
        var overrides = await db.PortalTexts.AsNoTracking().Where(t => t.LanguageCode == lang).ToListAsync(ct);
        // Email wording is edited on the Email Templates page (the email.* keys remain only as a fallback).
        var rows = english.Keys.Where(k => !k.StartsWith("email.", StringComparison.Ordinal)).Select(k =>
        {
            var o = overrides.FirstOrDefault(x => x.TextKey == k);
            return new PortalTextRow(k, english[k], shipped.TryGetValue(k, out var v) ? v : "", o?.Text ?? "", o?.ReviewStatus.ToString() ?? "", o?.ReviewStatus == ReviewStatus.Approved);
        });
        if (changed == "1") rows = rows.Where(r => r.Text.Length > 0);
        if (lq.Term.Length > 0)
            rows = rows.Where(r => r.Key.Contains(lq.Term, StringComparison.OrdinalIgnoreCase) || r.English.Contains(lq.Term, StringComparison.OrdinalIgnoreCase)
                                   || r.Shipped.Contains(lq.Term, StringComparison.OrdinalIgnoreCase) || r.Text.Contains(lq.Term, StringComparison.OrdinalIgnoreCase));
        return rows.Page(lq, new Dictionary<string, Func<PortalTextRow, object?>> { ["key"] = r => r.Key, ["status"] = r => r.ReviewStatus }, "key");
    }

    public sealed record PortalTextRow(string Key, string English, string Shipped, string Text, string ReviewStatus, bool Approved);

    public async Task<object> SavePortalTextAsync(PortalTextInput input, CancellationToken ct = default)
    {
        if (!catalog.Shipped("en").ContainsKey(input.Key)) throw DomainException.Invalid("unknown_key");
        if (!await db.Languages.AnyAsync(l => l.Code == input.Lang, ct)) throw DomainException.Invalid("unknown_language");
        var now = clock.UtcNow;
        var t = await db.PortalTexts.FirstOrDefaultAsync(x => x.LanguageCode == input.Lang && x.TextKey == input.Key, ct);
        if (string.IsNullOrWhiteSpace(input.Text))
        {
            if (t != null) db.PortalTexts.Remove(t);
        }
        else
        {
            if (t == null) db.PortalTexts.Add(t = new PortalText { LanguageCode = input.Lang, TextKey = input.Key, ReviewStatus = ReviewStatus.Suggested });
            t.Text = TextUtil.NoEmDash(input.Text.Trim());
            t.UpdatedAt = now;
            TranslationReview.Apply(t, input.Approved, false, TextUtil.ContentHash(t.Text), actor.Id, now);
        }
        await db.SaveChangesAsync(ct);
        content.Invalidate();
        return new { ok = true };
    }
}
