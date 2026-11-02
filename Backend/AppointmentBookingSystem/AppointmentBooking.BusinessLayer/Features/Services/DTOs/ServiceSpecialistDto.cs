namespace AppointmentBooking.BusinessLayer.Features.Services.DTOs;

public class ServiceSpecialistDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
}

