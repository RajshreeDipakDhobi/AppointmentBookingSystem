namespace AppointmentBooking.BusinessLayer.Features.Availability.DTOs;

public class DayScheduleSummaryDto
{
    public DayOfWeek DayOfWeek { get; set; }
    public string DayName => DayOfWeek.ToString();
    public string ShortName => DayOfWeek switch
    {
        DayOfWeek.Monday => "Mon",
        DayOfWeek.Tuesday => "Tue",
        DayOfWeek.Wednesday => "Wed",
        DayOfWeek.Thursday => "Thu",
        DayOfWeek.Friday => "Fri",
        DayOfWeek.Saturday => "Sat",
        DayOfWeek.Sunday => "Sun",
        _ => DayOfWeek.ToString().Substring(0, 3)
    };
    public bool IsAvailable { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? BreakStartTime { get; set; }
    public string? BreakEndTime { get; set; }
    public int SlotDurationMinutes { get; set; } = 30;
    public string? BlockedSlots { get; set; }
    public List<string>? BlockedSlotList { get; set; }
    public int TotalCalculatedSlots { get; set; }
    public string TimeDisplay { get; set; } = "Off";
    public string BreakDisplay { get; set; } = "None";
}

public class ProviderScheduleOverviewItemDto
{
    public int ProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string? Specialization { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public bool IsConfigured { get; set; }
    public int ActiveDaysCount { get; set; }
    public string ActiveDaysSummary { get; set; } = "Not Configured";
    public string WorkingHoursSummary { get; set; } = "Not Set";
    public string BreakSummary { get; set; } = "None";
    public int SlotDurationMinutes { get; set; } = 30;
    public int TotalWeeklySlots { get; set; }
    public List<DayScheduleSummaryDto> Days { get; set; } = new();
}

public class ProviderAvailabilityOverviewResponseDto
{
    public int TotalProviders { get; set; }
    public int ConfiguredProvidersCount { get; set; }
    public int TotalWeeklySlots { get; set; }
    public int ActiveDaysTotal { get; set; }
    public List<ProviderScheduleOverviewItemDto> Providers { get; set; } = new();
}
