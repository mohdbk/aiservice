using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Application.Backoffice;

/// <summary>
/// The hiring report: for the applications received in a period (optionally one job or department), how far they got (the
/// funnel), how long hiring took, test pass rates, where candidates dropped out, and the same by department and week.
/// </summary>
public sealed class ReportService(IAppDbContext db, IRequestLanguage language, IClock clock)
{
    const string En = "en";

    public async Task<object> HiringAsync(DateOnly? from, DateOnly? to, int? jobId, int? departmentId, CancellationToken ct = default)
    {
        var today = clock.Today();
        var end = to ?? today;
        var start = from ?? end.AddDays(-89);
        if (start > end) (start, end) = (end, start);
        var startAt = start.ToDateTime(TimeOnly.MinValue);
        var endAt = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var en = language.English;

        var q = db.JobApplications.AsNoTracking().Where(a => a.CreatedAt >= startAt && a.CreatedAt < endAt);
        if (jobId != null) q = q.Where(a => a.JobOpeningId == jobId);
        if (departmentId != null) q = q.Where(a => a.JobOpening!.DepartmentId == departmentId);
        // Bounded: a report covers at most 50,000 applications.
        var apps = await q.OrderBy(a => a.Id).Take(50000).Select(a => new
        {
            a.Id, a.CreatedAt, a.Status, a.ScreeningOutcome, a.JobOpening!.DepartmentId,
        }).ToListAsync(ct);
        var ids = apps.Select(a => a.Id).ToList();

        var invitations = await db.Invitations.AsNoTracking().Where(i => ids.Contains(i.JobApplicationId)).Select(i => new
        {
            i.JobApplicationId, i.AssessmentId, i.Status, i.Result, Personality = i.Assessment!.PersonalityModel != PersonalityModel.None,
            Title = en ? i.Assessment.Translations.Where(t => t.LanguageCode == En && t.Title != "").Select(t => t.Title).FirstOrDefault() ?? i.Assessment.Title : i.Assessment.Title,
        }).ToListAsync(ct);
        var interviews = await db.Interviews.AsNoTracking().Where(i => ids.Contains(i.JobApplicationId) && i.Status != InterviewStatus.Cancelled)
            .Select(i => new { i.JobApplicationId, i.StartsAt, i.Status }).ToListAsync(ct);
        var offers = await db.JobOffers.AsNoTracking().Where(o => ids.Contains(o.JobApplicationId))
            .Select(o => new { o.JobApplicationId, o.Status, o.CreatedAt, o.UpdatedAt }).ToListAsync(ct);
        var departments = await db.Departments.AsNoTracking().Select(d => new
        {
            d.Id, Name = en ? d.Translations.Where(t => t.LanguageCode == En && t.Name != "").Select(t => t.Name).FirstOrDefault() ?? d.Name : d.Name,
        }).ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        var created = apps.ToDictionary(a => a.Id, a => a.CreatedAt);
        var tested = invitations.Where(i => !i.Personality && i.Status is InvitationStatus.Submitted or InvitationStatus.Marked).Select(i => i.JobApplicationId).ToHashSet();
        var passed = invitations.Where(i => !i.Personality && i.Result == InvitationResult.Pass).Select(i => i.JobApplicationId).ToHashSet();
        var interviewed = interviews.Select(i => i.JobApplicationId).ToHashSet();
        var offered = offers.Where(o => o.Status != OfferStatus.Draft).Select(o => o.JobApplicationId).ToHashSet();
        var hired = offers.Where(o => o.Status == OfferStatus.Accepted).Select(o => o.JobApplicationId).ToHashSet();
        var screenedIn = apps.Where(a => a.ScreeningOutcome != ScreeningOutcome.DoesNotMeet).Select(a => a.Id).ToHashSet();

        var funnel = new[]
        {
            ("applied", apps.Count), ("screened_in", screenedIn.Count), ("tested", tested.Count), ("passed", passed.Count),
            ("interviewed", interviewed.Count), ("offered", offered.Count), ("hired", hired.Count),
        };

        // Days from applying to the offer being accepted (the offer's last change), and to the first interview.
        static double? Median(List<double> v)
        {
            if (v.Count == 0) return null;
            v.Sort();
            return Math.Round(v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2, 1);
        }
        var toHire = offers.Where(o => o.Status == OfferStatus.Accepted && created.ContainsKey(o.JobApplicationId))
            .GroupBy(o => o.JobApplicationId).Select(g => (g.Min(o => o.UpdatedAt) - created[g.Key]).TotalDays).ToList();
        var toInterview = interviews.Where(i => created.ContainsKey(i.JobApplicationId)).GroupBy(i => i.JobApplicationId)
            .Select(g => Math.Max(0, (g.Min(i => i.StartsAt) - created[g.Key]).TotalDays)).ToList();
        var toOffer = offers.Where(o => o.Status != OfferStatus.Draft && created.ContainsKey(o.JobApplicationId)).GroupBy(o => o.JobApplicationId)
            .Select(g => (g.Min(o => o.CreatedAt) - created[g.Key]).TotalDays).ToList();

        var tests = invitations.Where(i => !i.Personality).GroupBy(i => (i.AssessmentId, i.Title)).Select(g => new
        {
            assessment_id = g.Key.AssessmentId, title = g.Key.Title, invited = g.Count(),
            completed = g.Count(i => i.Status is InvitationStatus.Submitted or InvitationStatus.Marked),
            passed = g.Count(i => i.Result == InvitationResult.Pass), failed = g.Count(i => i.Result == InvitationResult.Fail),
            expired = g.Count(i => i.Status == InvitationStatus.Expired),
        }).OrderByDescending(x => x.invited).Take(20).ToList();

        var dropOff = new
        {
            screened_out = apps.Count(a => a.ScreeningOutcome == ScreeningOutcome.DoesNotMeet),
            tests_not_taken = invitations.Count(i => i.Status == InvitationStatus.Expired),
            tests_failed = invitations.Count(i => !i.Personality && i.Result == InvitationResult.Fail),
            interview_rejected = interviews.Count(i => i.Status == InterviewStatus.Rejected),
            offers_declined = offers.Count(o => o.Status == OfferStatus.Declined),
            offers_withdrawn = offers.Count(o => o.Status == OfferStatus.Withdrawn),
            not_selected = apps.Count(a => a.Status == ApplicationStatus.Rejected),
        };

        var byDepartment = apps.GroupBy(a => a.DepartmentId).Select(g => new
        {
            department_id = g.Key, name = g.Key is { } d && departments.TryGetValue(d, out var n) ? n : "",
            applied = g.Count(), interviewed = g.Count(a => interviewed.Contains(a.Id)), offered = g.Count(a => offered.Contains(a.Id)),
            hired = g.Count(a => hired.Contains(a.Id)),
        }).OrderByDescending(x => x.applied).ToList();

        // Applications per week (weeks start on Monday), every week of the period present.
        var firstMonday = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));
        var weeks = new List<object>();
        for (var w = firstMonday; w <= end; w = w.AddDays(7))
        {
            var ws = w.ToDateTime(TimeOnly.MinValue);
            var we = w.AddDays(7).ToDateTime(TimeOnly.MinValue);
            weeks.Add(new { week = w.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), applied = apps.Count(a => a.CreatedAt >= ws && a.CreatedAt < we) });
        }

        return new
        {
            from = start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            to = end.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            truncated = apps.Count == 50000,
            funnel = funnel.Select(f => new { stage = f.Item1, count = f.Item2 }),
            time = new
            {
                to_interview_days = Median(toInterview), to_offer_days = Median(toOffer), to_hire_days = Median(toHire), hires = toHire.Count,
            },
            tests, drop_off = dropOff, departments = byDepartment, weeks,
        };
    }
}
