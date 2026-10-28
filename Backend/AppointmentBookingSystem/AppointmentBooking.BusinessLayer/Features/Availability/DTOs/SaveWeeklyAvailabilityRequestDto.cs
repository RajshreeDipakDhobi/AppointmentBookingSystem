using System.ComponentModel.DataAnnotations;

namespace AppointmentBooking.BusinessLayer.Features.Availability.DTOs;

public class SaveProviderAvailabilityDto
{
    [Required]
    public DayOfWeek DayOfWeek { get; set; }

    [Required]
    [RegularExpression(@"^([01]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "StartTime must be in HH:mm format (e.g. 10:00)")]
    public string StartTime { get; set; } = "10:00";

    [Required]
    [RegularExpression(@"^([01]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "EndTime must be in HH:mm format (e.g. 17:00)")]
    public string EndTime { get; set; } = "17:00";

    [Range(10, 240, ErrorMessage = "SlotDurationMinutes must be between 10 and 240 minutes")]
    public int SlotDurationMinutes { get; set; } = 30;

    public string? BreakStartTime { get; set; }
    public string? BreakEndTime { get; set; }

    // List of manually blocked slot start times (e.g. ["11:30", "15:00"])
    public List<string>? BlockedSlots { get; set; }

    public bool IsAvailable { get; set; } = true;
}

public class SaveWeeklyAvailabilityRequestDto
{
    [Required]
    public int ServiceProviderId { get; set; }

    [Required]
    public List<SaveProviderAvailabilityDto> Schedules { get; set; } = new();
}

public class SaveDateSpecificAvailabilityRequestDto
{
    [Required]
    public int ServiceProviderId { get; set; }

    [Required]
    public DateTime Date { get; set; }

    [RegularExpression(@"^([01]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "StartTime must be in HH:mm format (e.g. 10:00)")]
    public string StartTime { get; set; } = "10:00";

    [RegularExpression(@"^([01]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "EndTime must be in HH:mm format (e.g. 17:00)")]
    public string EndTime { get; set; } = "17:00";

    [Range(10, 240, ErrorMessage = "SlotDurationMinutes must be between 10 and 240 minutes")]
    public int SlotDurationMinutes { get; set; } = 30;

    public string? BreakStartTime { get; set; }
    public string? BreakEndTime { get; set; }

    // List of manually blocked slot start times on this specific date
    public List<string>? BlockedSlots { get; set; }

    // false = whole day blocked / day off
    public bool IsAvailable { get; set; } = true;
}
