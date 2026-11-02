namespace AppointmentBooking.BusinessLayer.Features.Services.DTOs;

public class ProviderServicesOverviewDto
{
    public int ProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
    public string Specialization { get; set; } = string.Empty;
    public decimal ChargesAmount { get; set; }

    // Today's Work Shift & Status
    public string TodayDateFormatted { get; set; } = string.Empty;
    public string DayName { get; set; } = string.Empty;
    public bool IsWorkingToday { get; set; }
    public string WorkingHours { get; set; } = "Closed";
    public string? BreakHours { get; set; }
    public string CurrentWorkStatus { get; set; } = "Available"; // "Available", "Busy", "OnBreak", "ShiftEnded", "OffToday"
    public string CurrentStatusNote { get; set; } = string.Empty;

    // Today's Workload Metrics
    public int TodayAppointmentsCount { get; set; }
    public int CompletedTodayCount { get; set; }
    public int RemainingTodayCount { get; set; }

    // Today's Live Busy Timeline
    public List<ProviderTodayBookingDto> TodayTimeline { get; set; } = new();

    // Assigned Services
    public int TotalServicesCount { get; set; }
    public List<ProviderAssignedServiceItemDto> Services { get; set; } = new();
}

public class ProviderTodayBookingDto
{
    public int AppointmentId { get; set; }
    public string AppointmentNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty; // "10:00 AM"
    public string EndTime { get; set; } = string.Empty;   // "11:30 AM"
    public string TimeRange { get; set; } = string.Empty; // "10:00 AM – 11:30 AM"
    public int DurationMinutes { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsOngoingNow { get; set; }
    public bool IsUpcoming { get; set; }
    public bool IsCompleted { get; set; }
    public string ProgressOrRemainingNote { get; set; } = string.Empty;
}

public class ProviderAssignedServiceItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public int TodayBookingsCount { get; set; }
    public int AllTimeBookingsCount { get; set; }
}

