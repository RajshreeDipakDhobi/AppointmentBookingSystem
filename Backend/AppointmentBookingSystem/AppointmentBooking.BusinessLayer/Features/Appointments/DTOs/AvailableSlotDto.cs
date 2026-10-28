namespace AppointmentBooking.BusinessLayer.Features.Appointments.DTOs;

public class AvailableSlotDto
{
    public string StartTime { get; set; } = string.Empty; // "10:00"
    public string EndTime { get; set; } = string.Empty;   // "10:30"
    public string DisplayText { get; set; } = string.Empty; // "10:00 AM – 10:30 AM"
    public bool IsAvailable { get; set; }
    public string Status { get; set; } = "Available"; // "Available", "Booked", "Past"
    public string? Reason { get; set; }
}

public class ProviderDailyAvailabilityResponseDto
{
    public int ServiceProviderId { get; set; }
    public string ServiceProviderName { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string DayName { get; set; } = string.Empty;
    public bool IsWorkingDay { get; set; }
    public string WorkingHours { get; set; } = string.Empty;
    public int SlotDurationMinutes { get; set; }
    public List<AvailableSlotDto> Slots { get; set; } = new();
}
