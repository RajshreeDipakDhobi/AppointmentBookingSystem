namespace AppointmentBooking.BusinessLayer.Features.Providers.DTOs;

public class ProviderServiceSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DurationMinutes { get; set; } = 30;
    public decimal Price { get; set; } = 0.00m;
}

