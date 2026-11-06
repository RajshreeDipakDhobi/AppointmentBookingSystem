namespace AppointmentBooking.BusinessLayer.Features.Calendar.DTOs;

public class CalendarDayScheduleDto
{
    public int DayOfWeek { get; set; } // 0 = Sunday, 1 = Monday, ..., 6 = Saturday
    public string DayName { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? BreakStartTime { get; set; }
    public string? BreakEndTime { get; set; }
    public int SlotDurationMinutes { get; set; } = 30;
}

public class CalendarProviderScheduleDto
{
    public int ProviderId { get; set; }
    public int? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string? SpecialistType { get; set; }
    public int Status { get; set; }
    public List<int> ServiceIds { get; set; } = new();

    // Day Schedule Information for Selected Date
    public bool IsAvailableToday { get; set; }
    public string WorkingHoursText { get; set; } = "—";
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? BreakStartTime { get; set; }
    public string? BreakEndTime { get; set; }
    public int StartMinutes { get; set; }
    public int EndMinutes { get; set; }
    public int BreakStartMinutes { get; set; } = -1;
    public int BreakEndMinutes { get; set; } = -1;

    // Full 7-day schedule for this provider
    public List<CalendarDayScheduleDto> Days { get; set; } = new();

    // Appointments scheduled for this provider within the requested view range
    public List<CalendarAppointmentItemDto> Appointments { get; set; } = new();
}

