namespace AppointmentBooking.BusinessLayer.Features.Calendar.DTOs;

public class CalendarScheduleResponseDto
{
    public string SelectedDate { get; set; } = string.Empty; // YYYY-MM-DD
    public string View { get; set; } = "day"; // "day", "week", "month"
    public string RangeStartDate { get; set; } = string.Empty;
    public string RangeEndDate { get; set; } = string.Empty;
    public int TenantId { get; set; }

    // Dynamic grid boundaries based on active working hours
    public int GridStartHour { get; set; } = 10;
    public int GridEndHour { get; set; } = 18;

    // Resource columns / providers
    public List<CalendarProviderScheduleDto> Providers { get; set; } = new();

    // Flattened appointments across all providers (useful for FullCalendar Week and Month views)
    public List<CalendarAppointmentItemDto> AllAppointments { get; set; } = new();

    // Role-specific action permissions for the requesting user
    public CalendarPermissionsDto Permissions { get; set; } = new();
}

