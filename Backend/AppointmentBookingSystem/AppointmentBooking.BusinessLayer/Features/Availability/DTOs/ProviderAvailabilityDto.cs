namespace AppointmentBooking.BusinessLayer.Features.Availability.DTOs;

public class ProviderAvailabilityDto
{
    public int Id { get; set; }
    public int ServiceProviderId { get; set; }
    public string ServiceProviderName { get; set; } = string.Empty;
    public DayOfWeek DayOfWeek { get; set; }
    public string DayName => DayOfWeek.ToString();
    public DateTime? SpecificDate { get; set; }
    public string? FormattedSpecificDate => SpecificDate?.ToString("yyyy-MM-dd");
    public string? StartTime { get; set; } // HH:mm format
    public string? EndTime { get; set; }   // HH:mm format
    public string? BreakStartTime { get; set; }
    public string? BreakEndTime { get; set; }
    public int SlotDurationMinutes { get; set; } = 30;
    public string? BlockedSlots { get; set; }
    public List<string> BlockedSlotList => string.IsNullOrWhiteSpace(BlockedSlots)
        ? new List<string>()
        : BlockedSlots.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    public bool IsAvailable { get; set; } = false;
    public int TotalCalculatedSlots { get; set; }
}
