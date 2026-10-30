namespace AppointmentBooking.BusinessLayer.Features.Services.DTOs;

public class ServiceDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public decimal Price { get; set; } = 0.00m;
    public bool IsActive { get; set; } = true;
    public int ProvidersCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

