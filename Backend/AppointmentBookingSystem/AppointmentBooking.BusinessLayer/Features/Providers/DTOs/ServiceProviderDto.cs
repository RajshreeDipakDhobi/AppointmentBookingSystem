using AppointmentBooking.DomainLayer.Enums;

namespace AppointmentBooking.BusinessLayer.Features.Providers.DTOs;

public class ServiceProviderDto
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int? ServiceId { get; set; }
    public string? ServiceName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string? Bio { get; set; }
    public decimal ChargesAmount { get; set; }
    public ProviderStatus Status { get; set; }
    public string StatusName => Status.ToString();
    public DateTime CreatedAt { get; set; }
    public int TotalAppointmentsCount { get; set; }
}
