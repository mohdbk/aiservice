using Moet.Recruitment.Domain.Common;

namespace Moet.Recruitment.Domain.Hiring;

public enum BookingStatus { Open, Booked, Cancelled }

/// <summary>
/// Interview times offered to a candidate, who picks one in the portal; the pick becomes a scheduled interview with the round,
/// place and panel set here, and the other times are released.
/// </summary>
public class InterviewBooking
{
    public int Id { get; set; }
    public int JobApplicationId { get; set; }
    public int InterviewRoundId { get; set; }
    public string Location { get; set; } = "";
    /// <summary>Comma-separated staff ids of the panel.</summary>
    public string PanelStaffIds { get; set; } = "";
    public BookingStatus Status { get; set; } = BookingStatus.Open;
    public int? InterviewId { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByStaffId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<InterviewBookingSlot> Slots { get; set; } = [];

    public List<int> Panel() => PanelStaffIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).Distinct().ToList();

    public void Validate(DateTime now)
    {
        if (Slots.Count is < 1 or > 10) throw DomainException.Invalid("booking_slots");
        if (Slots.Any(s => s.EndsAt <= s.StartsAt)) throw DomainException.Invalid("interview_times");
        if (Slots.Any(s => s.StartsAt < now)) throw DomainException.Invalid("booking_slot_past");
    }

    /// <summary>The candidate's pick: only an open booking and one of its future times.</summary>
    public InterviewBookingSlot Pick(int slotId, DateTime now)
    {
        if (Status != BookingStatus.Open) throw DomainException.Conflict("booking_closed");
        var slot = Slots.FirstOrDefault(s => s.Id == slotId) ?? throw DomainException.Invalid("booking_slot_unknown");
        if (slot.StartsAt <= now) throw DomainException.Conflict("booking_slot_past");
        Status = BookingStatus.Booked;
        UpdatedAt = now;
        return slot;
    }
}

public class InterviewBookingSlot
{
    public int Id { get; set; }
    public int InterviewBookingId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
}
