using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Moet.Recruitment.Application.Common;
using Moet.Recruitment.Domain;
using Moet.Recruitment.Domain.Common;
using Moet.Recruitment.Domain.Hiring;
using Moet.Recruitment.Domain.Identity;

namespace Moet.Recruitment.Application.Backoffice;

public sealed record BookingSlotInput(string? Date, string? From, string? To);

public sealed record BookingInput(int ApplicationId, int RoundId, string? Location, IReadOnlyList<int>? Panelists, IReadOnlyList<BookingSlotInput>? Slots);

/// <summary>Interview times offered to a candidate to choose from in the portal; the chosen one becomes a scheduled interview.</summary>
public sealed class BookingService(IAppDbContext db, ICodeGenerator codes, ICurrentActor actor, IClock clock, PortalMailer mailer, ContentService content,
    IRequestLanguage language)
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<object> CreateAsync(BookingInput input, CancellationToken ct = default)
    {
        var app = await db.JobApplications.Include(a => a.JobOpening!).ThenInclude(j => j.Translations).FirstOrDefaultAsync(a => a.Id == input.ApplicationId, ct)
            ?? throw DomainException.NotFound();
        if (app.AnonymisedAt != null) throw DomainException.Conflict("applicant_anonymised");
        var round = await db.InterviewRounds.AsNoTracking().FirstOrDefaultAsync(r => r.Id == input.RoundId, ct) ?? throw DomainException.Invalid("round_required");
        var now = clock.UtcNow;
        var panel = await db.StaffUsers.Where(u => (input.Panelists ?? Array.Empty<int>()).Contains(u.Id) && u.IsEnabled).Select(u => u.Id).ToListAsync(ct);
        var b = new InterviewBooking
        {
            JobApplicationId = app.Id, InterviewRoundId = round.Id, Location = TextUtil.Clip(input.Location?.Trim(), 300),
            PanelStaffIds = string.Join(",", panel), CreatedAt = now, CreatedByStaffId = actor.Id, UpdatedAt = now,
        };
        foreach (var s in input.Slots ?? [])
        {
            if (!DateOnly.TryParseExact(s.Date, "yyyy-MM-dd", out var day) || !TimeOnly.TryParse(s.From, Inv, out var from) || !TimeOnly.TryParse(s.To, Inv, out var to))
                throw DomainException.Invalid("invalid_date");
            b.Slots.Add(new InterviewBookingSlot { StartsAt = clock.ToUtc(day, from), EndsAt = clock.ToUtc(day, to) });
        }
        b.Slots = b.Slots.DistinctBy(s => s.StartsAt).OrderBy(s => s.StartsAt).ToList();
        b.Validate(now);
        // A new set of times replaces any the candidate has not picked from yet.
        foreach (var old in await db.InterviewBookings.Where(x => x.JobApplicationId == app.Id && x.Status == BookingStatus.Open).ToListAsync(ct))
        {
            old.Status = BookingStatus.Cancelled;
            old.UpdatedAt = now;
        }
        db.InterviewBookings.Add(b);
        await db.SaveChangesAsync(ct);
        var lang = await content.PortalLanguageAsync(app.LanguageCode, ct);
        var link = await mailer.LinkAsync(lang, $"/applications/{app.Code}", ct);
        var emailed = await mailer.QueueAsync(app.Email, app.FullName, lang, "interview_booking", link, new Dictionary<string, string>
        {
            ["job_title"] = app.JobOpening!.In(lang).Title, ["round"] = lang == "ar" || round.NameEn.Length == 0 ? round.Name : round.NameEn,
        }, ct);
        if (emailed) await db.SaveChangesAsync(ct);
        return new { id = b.Id, emailed };
    }

    public async Task<object> CancelAsync(int id, CancellationToken ct = default)
    {
        var b = await db.InterviewBookings.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        if (b.Status != BookingStatus.Open) throw DomainException.Conflict("booking_closed");
        b.Status = BookingStatus.Cancelled;
        b.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    /// <summary>The applicant's offered times, for the backoffice applicant page.</summary>
    public async Task<object> ForApplicantAsync(int appId, CancellationToken ct = default)
    {
        var en = language.English;
        return await db.InterviewBookings.AsNoTracking().Where(b => b.JobApplicationId == appId).OrderByDescending(b => b.CreatedAt).Take(5)
            .Select(b => new
            {
                id = b.Id, status = b.Status.ToString(), location = b.Location, interview_id = b.InterviewId, created_at = b.CreatedAt,
                round = db.InterviewRounds.Where(r => r.Id == b.InterviewRoundId).Select(r => en && r.NameEn != "" ? r.NameEn : r.Name).FirstOrDefault() ?? "",
                slots = b.Slots.OrderBy(s => s.StartsAt).Select(s => new { id = s.Id, starts_at = s.StartsAt, ends_at = s.EndsAt }).ToList(),
            }).ToListAsync(ct);
    }

    // ---------------- portal ----------------

    /// <summary>The open times for a candidate's application (future ones only), or null.</summary>
    public async Task<object?> OpenForCandidateAsync(int appId, string lang, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var b = await db.InterviewBookings.AsNoTracking().Include(x => x.Slots).Where(x => x.JobApplicationId == appId && x.Status == BookingStatus.Open)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (b == null) return null;
        var slots = b.Slots.Where(s => s.StartsAt > now).OrderBy(s => s.StartsAt).ToList();
        if (slots.Count == 0) return null;
        var round = await db.InterviewRounds.AsNoTracking().FirstAsync(r => r.Id == b.InterviewRoundId, ct);
        return new
        {
            id = b.Id, round = lang == "ar" || round.NameEn.Length == 0 ? round.Name : round.NameEn, location = b.Location,
            slots = slots.Select(s => new
            {
                id = s.Id, date = clock.Local(s.StartsAt).ToString("yyyy-MM-dd", Inv),
                from = clock.Local(s.StartsAt).ToString("HH:mm", Inv), to = clock.Local(s.EndsAt).ToString("HH:mm", Inv),
            }),
        };
    }

    /// <summary>The candidate's pick: schedules the interview with the round, place and panel HR chose.</summary>
    public async Task<object> PickAsync(ApplicantAccount user, string? code, int slotId, CancellationToken ct = default)
    {
        var app = await db.JobApplications.Include(a => a.JobOpening!).ThenInclude(j => j.Translations)
            .FirstOrDefaultAsync(a => a.Code == code && a.ApplicantAccountId == user.Id, ct) ?? throw DomainException.NotFound("application_not_found");
        var b = await db.InterviewBookings.Include(x => x.Slots).Where(x => x.JobApplicationId == app.Id && x.Status == BookingStatus.Open)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct) ?? throw DomainException.Conflict("booking_closed");
        var now = clock.UtcNow;
        var slot = b.Pick(slotId, now);
        var i = new Interview
        {
            Code = await codes.NextAsync(CodeKind.Interview, ct), JobApplicationId = app.Id, InterviewRoundId = b.InterviewRoundId,
            StartsAt = slot.StartsAt, EndsAt = slot.EndsAt, Location = b.Location, CreatedAt = now, CreatedByStaffId = b.CreatedByStaffId, UpdatedAt = now,
        };
        foreach (var sid in b.Panel()) i.Panelists.Add(new InterviewPanelist { StaffUserId = sid });
        i.Validate();
        db.Interviews.Add(i);
        db.ApplicationNotes.Add(new ApplicationNote
        {
            JobApplicationId = app.Id, Kind = NoteKind.System, AuthorApplicantId = user.Id, CreatedAt = now,
            Text = $"Interview time chosen in the career portal: {clock.Local(slot.StartsAt).ToString("yyyy-MM-dd HH:mm", Inv)}.",
        });
        await db.SaveChangesAsync(ct);
        b.InterviewId = i.Id;
        var lang = await content.PortalLanguageAsync(app.LanguageCode, ct);
        var round = await db.InterviewRounds.AsNoTracking().FirstAsync(r => r.Id == b.InterviewRoundId, ct);
        await mailer.QueueAsync(app.Email, app.FullName, lang, "interview_scheduled", "", new Dictionary<string, string>
        {
            ["job_title"] = app.JobOpening!.In(lang).Title, ["round"] = lang == "ar" || round.NameEn.Length == 0 ? round.Name : round.NameEn,
            ["date"] = clock.Local(slot.StartsAt).ToString("yyyy-MM-dd", Inv),
            ["time"] = $"{clock.Local(slot.StartsAt):HH\\:mm} - {clock.Local(slot.EndsAt):HH\\:mm}", ["location"] = b.Location,
        }, ct);
        await db.SaveChangesAsync(ct);
        return new
        {
            booked = true, date = clock.Local(slot.StartsAt).ToString("yyyy-MM-dd", Inv), from = clock.Local(slot.StartsAt).ToString("HH:mm", Inv),
            to = clock.Local(slot.EndsAt).ToString("HH:mm", Inv),
        };
    }
}
