namespace AppointmentBooking.BusinessLayer.Features.Availability.DTOs;

public class TimeSlotDto
{
    public string StartTime { get; set; } = string.Empty; // e.g. "10:00"
    public string EndTime { get; set; } = string.Empty;   // e.g. "10:30"
    public string DisplayText => $"{StartTime} – {EndTime}"; // e.g. "10:00 – 10:30"
    public bool IsAvailable { get; set; } = true;
    public bool IsBreak { get; set; } = false;
    public bool IsBlocked { get; set; } = false;
    public string? Reason { get; set; }
}
