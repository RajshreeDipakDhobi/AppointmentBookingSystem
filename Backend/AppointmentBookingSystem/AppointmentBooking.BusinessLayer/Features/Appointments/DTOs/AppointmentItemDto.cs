namespace AppointmentBooking.BusinessLayer.Features.Appointments.DTOs;

public class AppointmentItemDto
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int DurationMinutes { get; set; }
}

