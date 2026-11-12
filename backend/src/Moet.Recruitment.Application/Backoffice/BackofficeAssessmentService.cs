using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Assessments;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Assessments;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Content;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record OptionInput(string Text, string? TextEn, bool Correct);

public sealed record QuestionInput(
    string QuestionType, string Text, string? TextEn, bool EnglishApproved, string? Category, string? JobFamily, string Difficulty,
    decimal Points, decimal? Tolerance, string? Rubric, int? MaxWords, bool IsActive, IReadOnlyList<OptionInput>? Options,
    IReadOnlyList<string>? AcceptedAnswers, string? Trait = null, bool ReverseKeyed = false);

public sealed record ItemInput(int? QuestionId, string? DrawCategory, string? DrawDifficulty, string? DrawJobFamily, int DrawCount, decimal? PointsOverride);

public sealed record SectionInput(int? Id, string Title, string? TitleEn, string? Instructions, string? InstructionsEn, int Minutes, decimal PassMark, IReadOnlyList<ItemInput>? Items);

public sealed record AssessmentInput(
    string Title, string? TitleEn, string? Instructions, string? InstructionsEn, bool EnglishApproved, string AssessmentType, string Status,
    decimal PassMark, int MaxAttempts, string ScorePolicy, bool ShuffleQuestions, bool ShuffleOptions, bool ShowScoreToCandidate,
    int DefaultDeadlineDays, IReadOnlyList<SectionInput>? Sections, string? PersonalityModel = null,
    IReadOnlyDictionary<string, TraitNorm>? TraitNorms = null);

public sealed record MarkInput(int QuestionId, decimal? Points, string? Comment);

/// <summary>Question bank, tests, invitations and marking for HR.</summary>
public sealed class BackofficeAssessmentService(IAppDbContext db, ICurrentActor actor, IClock clock, IHtmlCleaner html, AssessmentWorkflow workflow, JobQueue jobs)
{
    const string En = "en";

    // ---------------- question bank ----------------

    static readonly Sorts<Question> QuestionSorts = new Sorts<Question>("updated")
        .Add("updated", x => x.UpdatedAt).Add("text", x => x.Text).Add("type", x => x.QuestionType).Add("category", x => x.Category)
        .Add("difficulty", x => x.Difficulty).Add("points", x => x.Points);

    public async Task<object> QuestionsAsync(ListQuery lq, string? type, string? category, string? difficulty, string? jobFamily, bool? active, string? english, CancellationToken ct = default)
    {
        var query = db.Questions.AsNoTracking().AsQueryable();
        if (lq.Term.Length > 0) query = query.Where(x => x.Text.Contains(lq.Term) || x.Category.Contains(lq.Term));
        if (Enum.TryParse<QuestionType>(type, out var qt)) query = query.Where(x => x.QuestionType == qt);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category == category);
        if (Enum.TryParse<Difficulty>(difficulty, out var d)) query = query.Where(x => x.Difficulty == d);
        if (!string.IsNullOrWhiteSpace(jobFamily)) query = query.Where(x => x.JobFamily == jobFamily);
        if (active != null) query = query.Where(x => x.IsActive == active);
        if (english == "Missing") query = query.Where(x => !x.Translations.Any(t => t.LanguageCode == En));
        else if (Enum.TryParse<ReviewStatus>(english, out var rs)) query = query.Where(x => x.Translations.Any(t => t.LanguageCode == En && t.ReviewStatus == rs));
        return await query.PageAsync(lq, QuestionSorts, x => new
        {
            id = x.Id, text = x.Text.Length > 400 ? x.Text.Substring(0, 400) : x.Text, question_type = x.QuestionType, category = x.Category,
            job_family = x.JobFamily, difficulty = x.Difficulty, points = x.Points, is_active = x.IsActive, trait = x.Trait, reverse_keyed = x.ReverseKeyed,
            english = x.Translations.Where(t => t.LanguageCode == En).Select(t => (ReviewStatus?)t.ReviewStatus).FirstOrDefault(),
        }, ct);
    }

    public async Task<object> QuestionAsync(int id, CancellationToken ct = default)
    {
        var x = await LoadQuestion(id).AsNoTracking().FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
        var tr = x.Translations.FirstOrDefault(t => t.LanguageCode == En);
        return new
        {
            id = x.Id, question_type = x.QuestionType.ToString(), text = x.Text, text_en = tr?.Text ?? "",
            english_status = tr?.ReviewStatus.ToString() ?? "Missing", english_approved = tr?.ReviewStatus == ReviewStatus.Approved,
            category = x.Category, job_family = x.JobFamily, difficulty = x.Difficulty.ToString(), points = x.Points, tolerance = x.Tolerance,
            rubric = x.Rubric, max_words = x.MaxWords, is_active = x.IsActive, trait = x.Trait, reverse_keyed = x.ReverseKeyed,
            options = x.Options.OrderBy(o => o.SortOrder).Select(o => new
            {
                text = o.Text, text_en = o.Translations.FirstOrDefault(t => t.LanguageCode == En)?.Text ?? "", correct = o.IsCorrect,
            }).ToList(),
            accepted_answers = x.AcceptedAnswers.OrderBy(a => a.SortOrder).Select(a => a.Answer).ToList(),
        };
    }

    IQueryable<Question> LoadQuestion(int id) => db.Questions.Where(x => x.Id == id)
        .Include(x => x.Options).ThenInclude(o => o.Translations).Include(x => x.AcceptedAnswers).Include(x => x.Translations).AsSplitQuery();

    public async Task<object> SaveQuestionAsync(int? id, QuestionInput input, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        Question x;
        if (id == null)
        {
            x = new Question { CreatedAt = now, SourceLanguageCode = "ar" };
            db.Questions.Add(x);
        }
        else x = await LoadQuestion(id.Value).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
        x.QuestionType = BackofficeJobService.Parse<QuestionType>(input.QuestionType, "question_type");
        x.Text = html.Clean(input.Text);
        x.Category = TextUtil.Clip(input.Category?.Trim(), 100);
        x.JobFamily = TextUtil.Clip(input.JobFamily?.Trim(), 100);
        x.Difficulty = BackofficeJobService.Parse<Difficulty>(input.Difficulty, "difficulty");
        x.Points = input.Points <= 0 ? 1 : Math.Min(input.Points, 1000);
        x.Tolerance = Math.Max(0, input.Tolerance ?? 0);
        x.Rubric = TextUtil.Clip(input.Rubric, 8000);
        x.MaxWords = Math.Clamp(input.MaxWords ?? 0, 0, 10000);
        x.IsActive = input.IsActive;
        x.Trait = input.Trait ?? "";
        x.ReverseKeyed = input.ReverseKeyed;
        x.UpdatedAt = now;

        var opts = (input.Options ?? []).Where(o => o.Text.Trim().Length > 0).ToList();
        if (x.QuestionType == QuestionType.TrueFalse && opts.Count == 0)
            opts = [new OptionInput("صح", "True", true), new OptionInput("خطأ", "False", false)];
        // Options keep their rows (and ids) by position, so attempts that reference the question stay readable.
        while (x.Options.Count > opts.Count) x.Options.Remove(x.Options.OrderBy(o => o.SortOrder).Last());
        var existing = x.Options.OrderBy(o => o.SortOrder).ToList();
        for (var i = 0; i < opts.Count; i++)
        {
            var o = i < existing.Count ? existing[i] : new QuestionOption();
            if (i >= existing.Count) x.Options.Add(o);
            o.SortOrder = i;
            o.Text = TextUtil.Clip(opts[i].Text.Trim(), 500);
            o.IsCorrect = opts[i].Correct;
            SetOptionTranslation(o.Translations, opts[i].TextEn);
        }
        x.AcceptedAnswers.Clear();
        var n = 0;
        foreach (var a in (input.AcceptedAnswers ?? []).Select(a => a.Trim()).Where(a => a.Length > 0).Distinct())
            x.AcceptedAnswers.Add(new QuestionAcceptedAnswer { SortOrder = n++, Answer = TextUtil.Clip(a, 500) });
        x.Validate();

        var tr = x.Translations.FirstOrDefault(t => t.LanguageCode == En);
        var textEn = html.Clean(input.TextEn);
        if (!TranslationReview.IsFilled(textEn))
        {
            if (tr != null) x.Translations.Remove(tr);
            foreach (var o in x.Options) o.Translations.RemoveAll(t => t.LanguageCode == En);
        }
        else
        {
            if (tr == null) x.Translations.Add(tr = new QuestionTranslation { LanguageCode = En, ReviewStatus = ReviewStatus.Suggested });
            tr.Text = textEn;
            var hash = TextUtil.ContentHash(textEn, string.Join("\n", x.Options.OrderBy(o => o.SortOrder).Select(o => o.Translations.FirstOrDefault(t => t.LanguageCode == En)?.Text ?? "")));
            TranslationReview.Apply(tr, input.EnglishApproved, false, hash, actor.Id, now);
        }
        await db.SaveChangesAsync(ct);
        return new { id = x.Id };
    }

    static void SetOptionTranslation(List<QuestionOptionTranslation> list, string? text)
    {
        var t = list.FirstOrDefault(x => x.LanguageCode == En);
        if (string.IsNullOrWhiteSpace(text))
        {
            if (t != null) list.Remove(t);
            return;
        }
        if (t == null) list.Add(t = new QuestionOptionTranslation { LanguageCode = En });
        t.Text = TextUtil.Clip(text.Trim(), 500);
    }

    // ---------------- tests ----------------

    static readonly Sorts<Assessment> TestSorts = new Sorts<Assessment>("updated")
        .Add("updated", a => a.UpdatedAt).Add("title", a => a.Title).Add("type", a => a.AssessmentType).Add("status", a => a.Status)
        .Add("pass_mark", a => a.PassMark);

    public async Task<object> AssessmentsAsync(ListQuery lq, string? status, string? type, CancellationToken ct = default)
    {
        var q = db.Assessments.AsNoTracking().AsQueryable();
        if (Enum.TryParse<AssessmentStatus>(status, out var st)) q = q.Where(a => a.Status == st);
        if (Enum.TryParse<AssessmentType>(type, out var ty)) q = q.Where(a => a.AssessmentType == ty);
        if (lq.Term.Length > 0) q = q.Where(a => a.Title.Contains(lq.Term) || a.Translations.Any(t => t.Title.Contains(lq.Term)));
        return await q.PageAsync(lq, TestSorts, a => new
        {
            id = a.Id, title = a.Title, assessment_type = a.AssessmentType, status = a.Status, pass_mark = a.PassMark,
            sections = a.Sections.Count, minutes = a.Sections.Sum(s => s.Minutes),
            invitations = db.Invitations.Count(i => i.AssessmentId == a.Id),
            english = a.Translations.Where(t => t.LanguageCode == En).Select(t => (ReviewStatus?)t.ReviewStatus).FirstOrDefault(),
        }, ct);
    }

    IQueryable<Assessment> LoadAssessment(int id) => db.Assessments.Where(a => a.Id == id)
        .Include(a => a.Translations).Include(a => a.Sections).ThenInclude(s => s.Items).Include(a => a.Sections).ThenInclude(s => s.Translations)
        .AsSplitQuery();

    public async Task<object> AssessmentAsync(int id, CancellationToken ct = default)
    {
        var a = await LoadAssessment(id).AsNoTracking().FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
        var tr = a.Translations.FirstOrDefault(t => t.LanguageCode == En);
        var bank = await workflow.BankAsync(ct);
        return new
        {
            id = a.Id, title = a.Title, title_en = tr?.Title ?? "", instructions = a.Instructions, instructions_en = tr?.Instructions ?? "",
            english_status = tr?.ReviewStatus.ToString() ?? "Missing", english_approved = tr?.ReviewStatus == ReviewStatus.Approved,
            assessment_type = a.AssessmentType.ToString(), status = a.Status.ToString(), pass_mark = a.PassMark, max_attempts = (int)a.MaxAttempts,
            score_policy = a.ScorePolicy.ToString(), shuffle_questions = a.ShuffleQuestions, shuffle_options = a.ShuffleOptions,
            show_score_to_candidate = a.ShowScoreToCandidate, default_deadline_days = (int)a.DefaultDeadlineDays,
            personality_model = a.PersonalityModel.ToString(), trait_norms = Personality.ParseNorms(a.TraitNorms),
            traits = Personality.Traits(a.PersonalityModel == PersonalityModel.None ? PersonalityModel.BigFive : a.PersonalityModel)
                .Select(t => new { code = t.Code, name_ar = t.NameAr, name_en = t.NameEn }).ToList(),
            question_count = AttemptBuilder.CountQuestions(a, bank),
            sections = a.OrderedSections.Select(s => new
            {
                id = s.Id, title = s.Title, title_en = s.Translations.FirstOrDefault(t => t.LanguageCode == En)?.Title ?? "",
                instructions = s.Instructions, instructions_en = s.Translations.FirstOrDefault(t => t.LanguageCode == En)?.Instructions ?? "",
                minutes = s.Minutes, pass_mark = s.PassMark,
                items = s.Items.OrderBy(i => i.SortOrder).Select(i => new
                {
                    question_id = i.QuestionId, question = i.QuestionId == null ? "" : TextUtil.Summary(bank.FirstOrDefault(q => q.Id == i.QuestionId)?.Text, 120),
                    draw_category = i.DrawCategory, draw_difficulty = i.DrawDifficulty, draw_job_family = i.DrawJobFamily, draw_count = i.DrawCount,
                    points_override = i.PointsOverride,
                }).ToList(),
            }).ToList(),
        };
    }

    public async Task<object> SaveAssessmentAsync(int? id, AssessmentInput input, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        Assessment a;
        if (id == null)
        {
            a = new Assessment { CreatedAt = now, SourceLanguageCode = "ar" };
            db.Assessments.Add(a);
        }
        else a = await LoadAssessment(id.Value).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
        a.Title = TextUtil.Clip(input.Title?.Trim(), 140);
        a.Instructions = html.Clean(input.Instructions);
        a.AssessmentType = BackofficeJobService.Parse<AssessmentType>(input.AssessmentType, "assessment_type");
        a.Status = BackofficeJobService.Parse<AssessmentStatus>(input.Status, "status");
        a.PassMark = Math.Clamp(input.PassMark, 0, 100);
        a.MaxAttempts = (byte)Math.Clamp(input.MaxAttempts, 1, 10);
        a.ScorePolicy = BackofficeJobService.Parse<ScorePolicy>(input.ScorePolicy, "score_policy");
        a.ShuffleQuestions = input.ShuffleQuestions;
        a.ShuffleOptions = input.ShuffleOptions;
        a.ShowScoreToCandidate = input.ShowScoreToCandidate;
        a.DefaultDeadlineDays = (byte)Math.Clamp(input.DefaultDeadlineDays, 1, 90);
        a.PersonalityModel = Enum.TryParse<PersonalityModel>(input.PersonalityModel, out var pm) ? pm : PersonalityModel.None;
        a.TraitNorms = System.Text.Json.JsonSerializer.Serialize((input.TraitNorms ?? new Dictionary<string, TraitNorm>())
            .ToDictionary(x => x.Key, x => new { mean = x.Value.Mean, sd = x.Value.Sd }));
        a.UpdatedAt = now;

        var keep = (input.Sections ?? []).Where(s => s.Id != null).Select(s => s.Id!.Value).ToHashSet();
        a.Sections.RemoveAll(s => s.Id != 0 && !keep.Contains(s.Id));
        var order = 0;
        foreach (var si in input.Sections ?? [])
        {
            var s = si.Id == null ? null : a.Sections.FirstOrDefault(x => x.Id == si.Id);
            if (s == null) a.Sections.Add(s = new Section());
            s.SortOrder = order++;
            s.Title = TextUtil.Clip(si.Title?.Trim(), 140);
            s.Instructions = TextUtil.Clip(si.Instructions?.Trim(), 2000);
            s.Minutes = Math.Clamp(si.Minutes, 0, 600);
            s.PassMark = Math.Clamp(si.PassMark, 0, 100);
            s.Items.Clear();
            var n = 0;
            foreach (var it in si.Items ?? [])
                s.Items.Add(new SectionItem
                {
                    SortOrder = n++, QuestionId = it.QuestionId, DrawCategory = TextUtil.Clip(it.DrawCategory?.Trim(), 100),
                    DrawDifficulty = it.DrawDifficulty is "Easy" or "Medium" or "Hard" ? it.DrawDifficulty : "",
                    DrawJobFamily = TextUtil.Clip(it.DrawJobFamily?.Trim(), 100), DrawCount = it.QuestionId == null ? Math.Clamp(it.DrawCount, 0, 200) : 0,
                    PointsOverride = it.PointsOverride is > 0 ? it.PointsOverride : null,
                });
            var st = s.Translations.FirstOrDefault(t => t.LanguageCode == En);
            if (string.IsNullOrWhiteSpace(si.TitleEn) && string.IsNullOrWhiteSpace(si.InstructionsEn))
            {
                if (st != null) s.Translations.Remove(st);
            }
            else
            {
                if (st == null) s.Translations.Add(st = new SectionTranslation { LanguageCode = En });
                st.Title = TextUtil.Clip(si.TitleEn?.Trim(), 140);
                st.Instructions = TextUtil.Clip(si.InstructionsEn?.Trim(), 2000);
            }
        }
        a.Validate();
        if (a.Status == AssessmentStatus.Published && AttemptBuilder.CountQuestions(a, await workflow.BankAsync(ct)) == 0)
            throw DomainException.Invalid("no_questions");

        var tr = a.Translations.FirstOrDefault(t => t.LanguageCode == En);
        var insEn = html.Clean(input.InstructionsEn);
        if (!TranslationReview.IsFilled(input.TitleEn, insEn) && a.Sections.All(s => s.Translations.Count == 0))
        {
            if (tr != null) a.Translations.Remove(tr);
        }
        else
        {
            if (tr == null) a.Translations.Add(tr = new AssessmentTranslation { LanguageCode = En, ReviewStatus = ReviewStatus.Suggested });
            tr.Title = TextUtil.Clip(input.TitleEn?.Trim(), 140);
            tr.Instructions = insEn;
            var hash = TextUtil.ContentHash(tr.Title, tr.Instructions, string.Join("\n", a.OrderedSections.Select(s =>
                (s.Translations.FirstOrDefault(t => t.LanguageCode == En)?.Title ?? "") + "|" + (s.Translations.FirstOrDefault(t => t.LanguageCode == En)?.Instructions ?? ""))));
            TranslationReview.Apply(tr, input.EnglishApproved, false, hash, actor.Id, now);
        }
        await db.SaveChangesAsync(ct);
        return new { id = a.Id };
    }

    // ---------------- invitations and marking ----------------

    static readonly Sorts<Invitation> InvitationSorts = new Sorts<Invitation>("updated")
        .Add("updated", i => i.UpdatedAt).Add("deadline", i => i.DeadlineAt).Add("score", i => i.Score).Add("status", i => i.Status)
        .Add("applicant", i => i.JobApplication!.FullName).Add("test", i => i.Assessment!.Title);

    public async Task<object> InvitationsAsync(ListQuery lq, string? status, int? assessmentId, string? result, int? jobId, CancellationToken ct = default)
    {
        var q = db.Invitations.AsNoTracking().AsQueryable();
        if (Enum.TryParse<InvitationStatus>(status, out var st)) q = q.Where(i => i.Status == st);
        if (assessmentId != null) q = q.Where(i => i.AssessmentId == assessmentId);
        if (Enum.TryParse<InvitationResult>(result, out var r)) q = q.Where(i => i.Result == r);
        if (jobId != null) q = q.Where(i => i.JobApplication!.JobOpeningId == jobId);
        if (lq.Term.Length > 0) q = q.Where(i => i.JobApplication!.FullName.Contains(lq.Term) || i.Code.Contains(lq.Term));
        return await q.PageAsync(lq, InvitationSorts, i => new
        {
            id = i.Id, code = i.Code, status = i.Status, deadline_at = i.DeadlineAt, score = i.Score, result = i.Result,
            attempts_used = (int)i.AttemptsUsed, attempts_allowed = (int)i.AttemptsAllowed,
            assessment = i.Assessment!.Title, applicant_id = i.JobApplicationId, applicant = i.JobApplication!.FullName,
            job_title = i.JobApplication.JobOpening!.Title,
            to_mark = i.Attempts.Where(t => t.Status == AttemptStatus.Submitted).Select(t => (int?)t.Id).FirstOrDefault(),
            last_attempt = i.Attempts.OrderByDescending(t => t.AttemptNo).Select(t => (int?)t.Id).FirstOrDefault(),
        }, ct);
    }

    public async Task<object> ReexamAsync(int id, int extraAttempts, int deadlineDays, bool notify, CancellationToken ct = default)
    {
        var inv = await db.Invitations.Include(i => i.Assessment).Include(i => i.JobApplication).FirstOrDefaultAsync(i => i.Id == id, ct)
                  ?? throw DomainException.NotFound();
        inv.AllowReexam(extraAttempts, deadlineDays, clock.UtcNow);
        if (notify) await workflow.EmailAsync(inv, inv.Assessment!, inv.JobApplication!, "email.reexam_subject", "email.reexam_body", ct);
        await db.SaveChangesAsync(ct);
        return new { attempts_allowed = (int)inv.AttemptsAllowed, deadline_at = inv.DeadlineAt };
    }

    public async Task<object> CancelInvitationAsync(int id, CancellationToken ct = default)
    {
        var inv = await db.Invitations.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw DomainException.NotFound();
        inv.Cancel(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return new { status = inv.Status.ToString() };
    }

    async Task<Attempt> LoadAttemptAsync(int id, CancellationToken ct) =>
        await db.Attempts.Where(a => a.Id == id)
            .Include(a => a.Invitation!).ThenInclude(i => i.Assessment)
            .Include(a => a.Invitation!).ThenInclude(i => i.Attempts)
            .Include(a => a.Invitation!).ThenInclude(i => i.JobApplication)
            .Include(a => a.Sections).ThenInclude(s => s.Questions).ThenInclude(q => q.Options)
            .Include(a => a.Sections).ThenInclude(s => s.Questions).ThenInclude(q => q.AcceptedAnswers)
            .AsSplitQuery().FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();

    public async Task<object> AttemptAsync(int id, CancellationToken ct = default)
    {
        var a = await LoadAttemptAsync(id, ct);
        var inv = a.Invitation!;
        var roles = inv.Assessment!.IsPersonality ? await db.RoleProfiles.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.SortOrder).ToListAsync(ct) : [];
        return new
        {
            id = a.Id, attempt_no = (int)a.AttemptNo, status = a.Status.ToString(), percent = a.Percent, earned = a.Earned, total = a.Total,
            passed = a.Passed, started_at = a.StartedAt, trait_profile = TraitProfile(a.TraitProfile),
            personality_model = inv.Assessment!.PersonalityModel.ToString(),
            personality = RoleProfileService.Summary(inv.Assessment.PersonalityModel, a.TraitProfile, roles), submitted_at = a.SubmittedAt, tab_switches = a.TabSwitches, pastes = a.Pastes,
            invitation = new { id = inv.Id, code = inv.Code, assessment = inv.Assessment!.Title, pass_mark = inv.Assessment.PassMark },
            applicant = new { id = inv.JobApplicationId, name = inv.JobApplication!.FullName },
            sections = a.SectionScores().Select(s => new { s.SectionNo, s.Title, s.Earned, s.Total, s.Percent, s.Passed }).ToList(),
            questions = a.AllQuestions.Select(q => new
            {
                id = q.Id, key = q.QuestionKey, question_type = q.QuestionType.ToString(), text = q.Text, points = q.Points,
                rubric = q.Rubric, answer = q.AnswerDisplay(), file_url = q.AnswerFileId == null ? null : $"/api/backoffice/attempts/{a.Id}/files/{q.Id}",
                options = q.OrderedOptions.Select(o => new { key = o.OptionKey, text = o.Text, correct = o.IsCorrect, selected = o.IsSelected }).ToList(),
                accepted = q.AcceptedAnswers.Select(x => x.Answer).ToList(),
                auto_points = q.AutoPoints, needs_marking = q.NeedsMarking, marker_points = q.MarkerPoints, marker_comment = q.MarkerComment,
                suggested_points = q.SuggestedPoints, suggestion_reason = q.SuggestionReason,
            }).ToList(),
        };
    }

    internal static IReadOnlyList<TraitScore>? TraitProfile(string? json) =>
        string.IsNullOrEmpty(json) ? null : System.Text.Json.JsonSerializer.Deserialize<List<TraitScore>>(json);

    public async Task<object> SaveMarksAsync(int id, IReadOnlyList<MarkInput> marks, CancellationToken ct = default)
    {
        var a = await LoadAttemptAsync(id, ct);
        if (a.Status == AttemptStatus.InProgress) throw DomainException.Conflict("attempt_in_progress");
        var now = clock.UtcNow;
        foreach (var m in marks)
        {
            var q = a.AllQuestions.FirstOrDefault(x => x.Id == m.QuestionId) ?? throw DomainException.Invalid("question_not_found");
            q.Mark(m.Points, m.Comment ?? "", actor.Id ?? 0, now);
        }
        a.Score();
        await workflow.FinaliseAsync(a.Invitation!, a, ct);
        await db.SaveChangesAsync(ct);
        return new { status = a.Status.ToString(), percent = a.Percent, still_to_mark = a.AllQuestions.Count(q => q.NeedsMarking && q.MarkerPoints == null) };
    }

    public async Task<object> SuggestMarksAsync(int id, CancellationToken ct = default)
    {
        if (!await db.Attempts.AnyAsync(a => a.Id == id, ct)) throw DomainException.NotFound();
        await jobs.EnqueueAsync(JobTypes.SuggestMarks, id, ct);
        await db.SaveChangesAsync(ct);
        return new { queued = true };
    }

    public async Task<(byte[] Content, string ContentType, string FileName)> AnswerFileAsync(int attemptId, int questionId, CancellationToken ct = default)
    {
        var fileId = await db.AttemptQuestions.Where(q => q.Id == questionId && db.Attempts.Any(a => a.Id == attemptId && a.Sections.Any(s => s.Id == q.AttemptSectionId)))
            .Select(q => q.AnswerFileId).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
        var f = await db.StoredFiles.AsNoTracking().FirstAsync(x => x.Id == fileId, ct);
        return (f.Content, f.ContentType, f.FileName);
    }
}
