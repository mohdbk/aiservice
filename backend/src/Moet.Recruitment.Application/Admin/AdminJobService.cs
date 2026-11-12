using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Content;
using Moet.Recruitment.Domain.Hiring;

namespace Moet.Recruitment.Application.Admin;

public sealed record TranslationInput(string Lang, string Title, string Description, bool Approved);

public sealed record JobScreeningInput(int QuestionId, bool Required, string? Rule, string? Expected);

public sealed record JobStageInput(int AssessmentId, string SendWhen, int DeadlineDays);

public sealed record JobInput(
    string Title, string Description, int? DepartmentId, string Location, string EmploymentType, string Status, bool IsPublished,
    string? PostedOn, string? ClosesOn, decimal? SalaryMin, decimal? SalaryMax, string? SalaryCurrency, string? SalaryPeriod, bool ShowSalary,
    IReadOnlyList<TranslationInput>? Translations, IReadOnlyList<JobScreeningInput>? Screening, IReadOnlyList<JobStageInput>? Stages,
    string? RowVersion);

public sealed class AdminJobService(IAppDbContext db, ICodeGenerator codes, ICurrentActor actor, IClock clock, IHtmlCleaner html, JobQueue jobs)
{
    public async Task<object> ListAsync(string? status, string? q, CancellationToken ct = default)
    {
        var query = db.JobOpenings.AsNoTracking().Include(j => j.Department).Include(j => j.Translations).AsQueryable();
        if (Enum.TryParse<JobStatus>(status, out var st)) query = query.Where(j => j.Status == st);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            query = query.Where(j => j.Title.Contains(t) || j.Code.Contains(t) || j.Translations.Any(x => x.Title.Contains(t)));
        }
        var rows = await query.OrderByDescending(j => j.CreatedAt).Take(300).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.JobApplications.AsNoTracking().Where(a => ids.Contains(a.JobOpeningId))
            .GroupBy(a => new { a.JobOpeningId, a.Status }).Select(g => new { g.Key.JobOpeningId, g.Key.Status, n = g.Count() }).ToListAsync(ct);
        return new
        {
            jobs = rows.Select(j => new
            {
                id = j.Id, code = j.Code, title = j.Title, title_en = j.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Title ?? "",
                department = j.Department?.Name ?? "", status = j.Status.ToString(), is_published = j.IsPublished,
                posted_on = AppTime.Date(j.PostedOn), closes_on = AppTime.Date(j.ClosesOn),
                applicants = counts.Where(c => c.JobOpeningId == j.Id).Sum(c => c.n),
                new_applicants = counts.Where(c => c.JobOpeningId == j.Id && c.Status == ApplicationStatus.Open).Sum(c => c.n),
                english = EnglishState(j.Translations.FirstOrDefault(t => t.LanguageCode == "en")),
            }).ToList(),
        };
    }

    static string EnglishState(ITranslation? t) => t == null ? "Missing" : t.ReviewStatus.ToString();

    public async Task<object> GetAsync(int id, CancellationToken ct = default)
    {
        var j = await Load(id).AsNoTracking().FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
        return View(j);
    }

    IQueryable<JobOpening> Load(int id) => db.JobOpenings.Where(j => j.Id == id)
        .Include(j => j.Department).Include(j => j.Translations).Include(j => j.Criteria)
        .Include(j => j.ScreeningQuestions).ThenInclude(s => s.ScreeningQuestion)
        .Include(j => j.AssessmentStages)
        .AsSplitQuery();

    static object View(JobOpening j) => new
    {
        id = j.Id, code = j.Code, title = j.Title, description = j.Description, source_language = j.SourceLanguageCode,
        department_id = j.DepartmentId, department = j.Department?.Name ?? "", location = j.Location,
        employment_type = j.EmploymentType.ToString(), status = j.Status.ToString(), is_published = j.IsPublished,
        posted_on = AppTime.Date(j.PostedOn), closes_on = AppTime.Date(j.ClosesOn),
        salary_min = j.SalaryMin, salary_max = j.SalaryMax, salary_currency = j.SalaryCurrency, salary_period = j.SalaryPeriod.ToString(),
        show_salary = j.ShowSalary,
        translations = j.Translations.Select(t => new
        {
            lang = t.LanguageCode, title = t.Title, description = t.Description, review_status = t.ReviewStatus.ToString(),
            approved = t.ReviewStatus == ReviewStatus.Approved,
        }).ToList(),
        criteria = j.Criteria.OrderBy(c => c.SortOrder).Select(c => new { c.Criterion, c.Weight, c.IsMustHave }).ToList(),
        criteria_current = j.CriteriaVersion != null && j.CriteriaVersion == j.DescriptionVersion(),
        screening = j.ScreeningQuestions.OrderBy(s => s.SortOrder).Select(s => new
        {
            question_id = s.ScreeningQuestionId, text = s.ScreeningQuestion?.Text ?? "", answer_type = s.ScreeningQuestion?.AnswerType.ToString() ?? "",
            required = s.IsRequired, rule = s.RequirementRule?.ToString(), expected = s.ExpectedValue,
        }).ToList(),
        stages = j.AssessmentStages.OrderBy(s => s.SortOrder).Select(s => new
        {
            assessment_id = s.AssessmentId, send_when = s.SendWhen.ToString(), deadline_days = (int)s.DeadlineDays,
        }).ToList(),
        row_version = Convert.ToBase64String(j.RowVersion),
    };

    public async Task<object> SaveAsync(int? id, JobInput input, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        JobOpening j;
        if (id == null)
        {
            j = new JobOpening
            {
                Code = await codes.NextAsync(CodeKind.JobOpening, ct), SourceLanguageCode = "ar", CreatedAt = now, CreatedByStaffId = actor.Id,
            };
            db.JobOpenings.Add(j);
        }
        else
        {
            j = await Load(id.Value).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
            if (!string.IsNullOrEmpty(input.RowVersion) && input.RowVersion != Convert.ToBase64String(j.RowVersion))
                throw DomainException.Conflict("changed_elsewhere");
        }
        j.Title = TextUtil.Clip(input.Title?.Trim(), 140);
        j.Description = html.Clean(input.Description);
        j.DepartmentId = input.DepartmentId;
        j.Location = TextUtil.Clip(input.Location?.Trim(), 140);
        j.EmploymentType = Parse<EmploymentType>(input.EmploymentType, "employment_type");
        j.Status = Parse<JobStatus>(input.Status, "status");
        j.IsPublished = input.IsPublished;
        j.PostedOn = ParseDate(input.PostedOn) ?? (j.Status == JobStatus.Open ? j.PostedOn ?? clock.Today() : j.PostedOn);
        j.ClosesOn = ParseDate(input.ClosesOn);
        j.SalaryMin = input.SalaryMin;
        j.SalaryMax = input.SalaryMax;
        j.SalaryCurrency = string.IsNullOrWhiteSpace(input.SalaryCurrency) ? "AED" : input.SalaryCurrency.Trim().ToUpperInvariant()[..Math.Min(3, input.SalaryCurrency.Trim().Length)];
        j.SalaryPeriod = Parse<SalaryPeriod>(input.SalaryPeriod ?? "Month", "salary_period");
        j.ShowSalary = input.ShowSalary;
        j.UpdatedAt = now;
        j.UpdatedByStaffId = actor.Id;

        foreach (var t in input.Translations ?? [])
            ApplyTranslation(j, t, suggested: false, now);

        j.ScreeningQuestions.Clear();
        var order = 0;
        foreach (var s in (input.Screening ?? []).DistinctBy(s => s.QuestionId))
        {
            RequirementRule? rule = string.IsNullOrEmpty(s.Rule) ? null : Parse<RequirementRule>(s.Rule, "rule");
            j.ScreeningQuestions.Add(new JobScreeningQuestion
            {
                ScreeningQuestionId = s.QuestionId, SortOrder = order++, IsRequired = s.Required, RequirementRule = rule,
                ExpectedValue = rule == null ? null : TextUtil.Clip(s.Expected?.Trim(), 300),
            });
        }
        j.AssessmentStages.Clear();
        order = 0;
        foreach (var s in (input.Stages ?? []).DistinctBy(s => s.AssessmentId))
            j.AssessmentStages.Add(new JobAssessmentStage
            {
                AssessmentId = s.AssessmentId, SortOrder = order++, SendWhen = Parse<SendWhen>(s.SendWhen, "send_when"),
                DeadlineDays = (byte)Math.Clamp(s.DeadlineDays, 1, 90),
            });
        j.Validate();
        await db.SaveChangesAsync(ct);
        return new { id = j.Id, code = j.Code };
    }

    /// <summary>Saves the job's text in another language. Writing or editing it approves it; a fresh draft waits for review.</summary>
    public void ApplyTranslation(JobOpening j, TranslationInput t, bool suggested, DateTime now)
    {
        if (t.Lang == j.SourceLanguageCode || !TextUtil.ValidLang(t.Lang)) return;
        var existing = j.Translations.FirstOrDefault(x => x.LanguageCode == t.Lang);
        var desc = html.Clean(t.Description);
        if (!TranslationReview.IsFilled(t.Title, desc))
        {
            if (existing != null) j.Translations.Remove(existing);
            return;
        }
        if (existing == null)
        {
            existing = new JobOpeningTranslation { LanguageCode = t.Lang, ReviewStatus = ReviewStatus.Suggested };
            j.Translations.Add(existing);
        }
        existing.Title = TextUtil.Clip(t.Title.Trim(), 140);
        existing.Description = desc;
        TranslationReview.Apply(existing, t.Approved, suggested, TextUtil.ContentHash(existing.Title, existing.Description), actor.Id, now);
    }

    public async Task<object> SuggestTranslationAsync(int id, CancellationToken ct = default)
    {
        var j = await db.JobOpenings.Include(x => x.Translations).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        var en = j.Translations.FirstOrDefault(t => t.LanguageCode == "en");
        if (en != null && TranslationReview.IsFilled(en.Title, en.Description)) throw DomainException.Conflict("translation_exists");
        await jobs.EnqueueAsync(JobTypes.SuggestJobTranslation, j.Id, ct);
        await db.SaveChangesAsync(ct);
        return new { queued = true };
    }

    internal static T Parse<T>(string? value, string field) where T : struct, Enum =>
        Enum.TryParse<T>(value, out var v) && Enum.IsDefined(v) ? v : throw DomainException.Invalid("invalid_value", new Dictionary<string, object?> { ["field"] = field });

    internal static DateOnly? ParseDate(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null
        : DateOnly.TryParseExact(s, "yyyy-MM-dd", out var d) ? d : throw DomainException.Invalid("invalid_date");
}
