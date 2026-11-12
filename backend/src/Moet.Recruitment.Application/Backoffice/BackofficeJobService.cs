using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Content;
using Moet.Recruitment.Domain.Hiring;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record TranslationInput(string Lang, string Title, string Description, bool Approved, string? Location = null);

public sealed record JobScreeningInput(int QuestionId, bool Required, string? Rule, string? Expected);

public sealed record JobStageInput(int AssessmentId, string SendWhen, int DeadlineDays);

public sealed record JobInput(
    string Title, string Description, int? DepartmentId, string Location, string EmploymentType, string Status, bool IsPublished,
    string? PostedOn, string? ClosesOn, decimal? SalaryMin, decimal? SalaryMax, string? SalaryCurrency, string? SalaryPeriod, bool ShowSalary,
    IReadOnlyList<TranslationInput>? Translations, IReadOnlyList<JobScreeningInput>? Screening, IReadOnlyList<JobStageInput>? Stages,
    string? RowVersion, int? RoleProfileId = null, SelectionWeightsInput? Weights = null, int? LocationId = null);

/// <summary>Combined selection score weights (percent, adding up to 100).</summary>
public sealed record SelectionWeightsInput(int Cv, int Tests, int Interview, int Personality);

public sealed class BackofficeJobService(IAppDbContext db, ICodeGenerator codes, ICurrentActor actor, IRequestLanguage language, IClock clock, IHtmlCleaner html, JobQueue jobs, ApprovalService approvals)
{
    static readonly Sorts<JobOpening> JobSorts = new Sorts<JobOpening>("created")
        .Add("created", j => j.CreatedAt).Add("code", j => j.Code).Add("title", j => j.Title).Add("status", j => j.Status)
        .Add("posted", j => j.PostedOn).Add("closes", j => j.ClosesOn).Add("department", j => j.Department!.Name)
        .Add("applicants", j => j.Id);

    public async Task<object> ListAsync(ListQuery lq, string? status, bool? published, int? departmentId, string? english, CancellationToken ct = default)
    {
        var query = db.JobOpenings.AsNoTracking();
        if (Enum.TryParse<JobStatus>(status, out var st)) query = query.Where(j => j.Status == st);
        if (published != null) query = query.Where(j => j.IsPublished == published);
        if (departmentId != null) query = query.Where(j => j.DepartmentId == departmentId);
        if (english == "Missing") query = query.Where(j => !j.Translations.Any(t => t.LanguageCode == "en"));
        else if (Enum.TryParse<ReviewStatus>(english, out var rs)) query = query.Where(j => j.Translations.Any(t => t.LanguageCode == "en" && t.ReviewStatus == rs));
        if (lq.Term.Length > 0)
        {
            var t = lq.Term;
            query = query.Where(j => j.Title.Contains(t) || j.Code.Contains(t) || j.Location.Contains(t) || j.Translations.Any(x => x.Title.Contains(t)));
        }
        var sorts = lq.Sort == "applicants"
            ? new Sorts<JobOpening>("applicants").Add("applicants", j => db.JobApplications.Count(a => a.JobOpeningId == j.Id))
            : JobSorts;
        var en = language.English;
        return await query.PageAsync(lq, sorts, j => new
        {
            id = j.Id, code = j.Code, title = j.Title,
            title_en = j.Translations.Where(t => t.LanguageCode == "en").Select(t => t.Title).FirstOrDefault() ?? "",
            department = j.Department == null ? "" : en ? j.Department.Translations.Where(t => t.LanguageCode == "en" && t.Name != "").Select(t => t.Name).FirstOrDefault() ?? j.Department.Name : j.Department.Name, status = j.Status, is_published = j.IsPublished,
            posted_on = j.PostedOn, closes_on = j.ClosesOn,
            applicants = db.JobApplications.Count(a => a.JobOpeningId == j.Id),
            new_applicants = db.JobApplications.Count(a => a.JobOpeningId == j.Id && a.Status == ApplicationStatus.Open),
            english = j.Translations.Where(t => t.LanguageCode == "en").Select(t => (ReviewStatus?)t.ReviewStatus).FirstOrDefault(),
        }, ct);
    }

    static string EnglishState(ITranslation? t) => t == null ? "Missing" : t.ReviewStatus.ToString();

    public async Task<object> GetAsync(int id, CancellationToken ct = default)
    {
        var j = await Load(id).AsNoTracking().FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
        return View(j);
    }

    IQueryable<JobOpening> Load(int id) => db.JobOpenings.Where(j => j.Id == id)
        .Include(j => j.Department).Include(j => j.JobLocation).Include(j => j.Translations).Include(j => j.Criteria)
        .Include(j => j.ScreeningQuestions).ThenInclude(s => s.ScreeningQuestion)
        .Include(j => j.AssessmentStages)
        .AsSplitQuery();

    static object View(JobOpening j) => new
    {
        id = j.Id, code = j.Code, title = j.Title, description = j.Description, source_language = j.SourceLanguageCode,
        department_id = j.DepartmentId, department = j.Department?.Name ?? "", location = j.Location, location_id = j.LocationId,
        employment_type = j.EmploymentType.ToString(), status = j.Status.ToString(), is_published = j.IsPublished,
        posted_on = AppTime.Date(j.PostedOn), closes_on = AppTime.Date(j.ClosesOn),
        salary_min = j.SalaryMin, salary_max = j.SalaryMax, salary_currency = j.SalaryCurrency, salary_period = j.SalaryPeriod.ToString(),
        show_salary = j.ShowSalary,
        role_profile_id = j.RoleProfileId,
        weights = new { cv = (int)j.WeightCv, tests = (int)j.WeightTests, interview = (int)j.WeightInterview, personality = (int)j.WeightPersonality },
        translations = j.Translations.Select(t => new
        {
            lang = t.LanguageCode, title = t.Title, description = t.Description, location = t.Location, review_status = t.ReviewStatus.ToString(),
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
        // A location from the list; text alone (from the assistant or an older client) is matched to the list by name in any language.
        var locationText = TextUtil.NoEmDash(TextUtil.Clip(input.Location?.Trim(), 140));
        var loc = input.LocationId is > 0 and var lid
            ? await db.JobLocations.Include(l => l.Translations).FirstOrDefaultAsync(l => l.Id == lid, ct) ?? throw DomainException.Invalid("invalid_value", new Dictionary<string, object?> { ["field"] = "location_id" })
            : locationText.Length > 0 ? await db.JobLocations.Include(l => l.Translations).FirstOrDefaultAsync(l => l.Name == locationText || l.Translations.Any(t => t.Name == locationText), ct) : null;
        j.LocationId = loc?.Id;
        j.Location = loc?.Name ?? locationText;
        j.EmploymentType = Parse<EmploymentType>(input.EmploymentType, "employment_type");
        j.Status = Parse<JobStatus>(input.Status, "status");
        // Publishing may need a recruitment admin's approval: the job stays unpublished and a request goes to the inbox.
        var approvalNeeded = input.IsPublished && !j.IsPublished && await approvals.NeededAsync(ApprovalKind.JobPublish, ct);
        j.IsPublished = input.IsPublished && !approvalNeeded;
        j.PostedOn = ParseDate(input.PostedOn) ?? (j.Status == JobStatus.Open ? j.PostedOn ?? clock.Today() : j.PostedOn);
        j.ClosesOn = ParseDate(input.ClosesOn);
        j.SalaryMin = input.SalaryMin;
        j.SalaryMax = input.SalaryMax;
        j.SalaryCurrency = string.IsNullOrWhiteSpace(input.SalaryCurrency) ? "AED" : input.SalaryCurrency.Trim().ToUpperInvariant()[..Math.Min(3, input.SalaryCurrency.Trim().Length)];
        j.SalaryPeriod = Parse<SalaryPeriod>(input.SalaryPeriod ?? "Month", "salary_period");
        j.ShowSalary = input.ShowSalary;
        // Null keeps the current choice (the assistant's job drafts do not set it); 0 clears it.
        if (input.RoleProfileId is { } rp)
        {
            if (rp > 0 && !await db.RoleProfiles.AnyAsync(r => r.Id == rp, ct)) throw DomainException.Invalid("invalid_value", new Dictionary<string, object?> { ["field"] = "role_profile_id" });
            j.RoleProfileId = rp > 0 ? rp : null;
        }
        if (input.Weights is { } w)
        {
            new SelectionWeights(w.Cv, w.Tests, w.Interview, w.Personality).Validate();
            (j.WeightCv, j.WeightTests, j.WeightInterview, j.WeightPersonality) = ((byte)w.Cv, (byte)w.Tests, (byte)w.Interview, (byte)w.Personality);
        }
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
        if (approvalNeeded)
        {
            await approvals.RequestAsync(ApprovalKind.JobPublish, j.Id, $"{j.Code} {j.Title}", "", ct);
            await db.SaveChangesAsync(ct);
        }
        return new { id = j.Id, code = j.Code, approval_pending = approvalNeeded };
    }

    /// <summary>The job's selection settings only: the role profile (0 for none) and the weights of the combined score.</summary>
    public async Task<object> SetSelectionAsync(int id, int roleProfileId, SelectionWeights w, CancellationToken ct = default)
    {
        var j = await db.JobOpenings.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        w.Validate();
        if (roleProfileId > 0 && !await db.RoleProfiles.AnyAsync(r => r.Id == roleProfileId, ct)) throw DomainException.Invalid("invalid_value");
        j.RoleProfileId = roleProfileId > 0 ? roleProfileId : null;
        (j.WeightCv, j.WeightTests, j.WeightInterview, j.WeightPersonality) = ((byte)w.Cv, (byte)w.Tests, (byte)w.Interview, (byte)w.Personality);
        j.UpdatedAt = clock.UtcNow;
        j.UpdatedByStaffId = actor.Id;
        await db.SaveChangesAsync(ct);
        return new { id = j.Id };
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
        if (t.Location != null) existing.Location = TextUtil.NoEmDash(TextUtil.Clip(t.Location.Trim(), 140));
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
