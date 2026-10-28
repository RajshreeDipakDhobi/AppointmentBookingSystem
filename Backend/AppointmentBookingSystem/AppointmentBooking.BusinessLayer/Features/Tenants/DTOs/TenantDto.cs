using AppointmentBooking.DomainLayer.Enums;

namespace AppointmentBooking.BusinessLayer.Features.Tenants.DTOs;

public class TenantDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string? LogoUrl { get; set; }
    public TenantStatus Status { get; set; }
    public string StatusName => Status.ToString();
    public DateTime CreatedAt { get; set; }
    public int TotalProvidersCount { get; set; }
    public int TotalAppointmentsCount { get; set; }
    public string? AdminFirstName { get; set; }
    public string? AdminLastName { get; set; }
    public string? AdminEmail { get; set; }
    public string? AdminFullName => string.IsNullOrWhiteSpace(AdminLastName) ? AdminFirstName : $"{AdminFirstName} {AdminLastName}".Trim();
    public double Rating { get; set; } = 4.8;
    public int TotalReviewsCount { get; set; } = 1200;
    public IReadOnlyList<string> ServiceTypes { get; set; } = new List<string>();
    public string Slug { get; set; } = string.Empty;
}
