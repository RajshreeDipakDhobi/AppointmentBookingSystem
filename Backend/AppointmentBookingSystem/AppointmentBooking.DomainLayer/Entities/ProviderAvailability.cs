using AppointmentBooking.DomainLayer.Common;

namespace AppointmentBooking.DomainLayer.Entities;

public class ProviderAvailability : BaseEntity
{
    public int TenantId { get; set; }

    public int ServiceProviderId { get; set; }
    public virtual ServiceProvider? ServiceProvider { get; set; }

    // Day of the week (0 = Sunday, 1 = Monday, ..., 6 = Saturday)
    public DayOfWeek DayOfWeek { get; set; }

    // Optional specific calendar date for date-wise slot overrides (null for recurring weekly schedule)
    public DateTime? SpecificDate { get; set; }

    // Working hours (e.g. 10:00:00 to 17:00:00)
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    // Optional break time / lunch window (e.g. 13:00:00 to 14:00:00)
    public TimeSpan? BreakStartTime { get; set; }
    public TimeSpan? BreakEndTime { get; set; }

    // Slot duration in minutes (e.g. 30 minutes)
    public int SlotDurationMinutes { get; set; } = 30;

    // Comma-separated list of manually blocked slot start times (e.g. "11:30,15:00")
    public string? BlockedSlots { get; set; }

    // Toggle day active/inactive
    public bool IsAvailable { get; set; } = true;
}
