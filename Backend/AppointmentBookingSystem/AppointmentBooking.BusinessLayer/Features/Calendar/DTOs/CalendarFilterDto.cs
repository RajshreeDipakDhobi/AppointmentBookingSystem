namespace AppointmentBooking.BusinessLayer.Features.Calendar.DTOs;

public class CalendarFilterDto
{
    public string View { get; set; } = "day"; // "day", "week", "month"
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? ProviderIds { get; set; } // Comma-separated IDs e.g. "1,2,5"
    public int? Status { get; set; } // Optional AppointmentStatus enum value
    public string? SearchQuery { get; set; }
}

