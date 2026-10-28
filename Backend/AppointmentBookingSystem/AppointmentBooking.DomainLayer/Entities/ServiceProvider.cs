using System.ComponentModel.DataAnnotations.Schema;
using AppointmentBooking.DomainLayer.Common;
using AppointmentBooking.DomainLayer.Enums;

namespace AppointmentBooking.DomainLayer.Entities;

public class ServiceProvider : BaseEntity
{
    public int TenantId { get; set; }

    public int UserId { get; set; }
    public virtual User User { get; set; } = null!;

    // Professional & Catalog Domain Fields
    public string ServiceType { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public decimal ChargesAmount { get; set; } = 0.00m;
    public ProviderStatus Status { get; set; } = ProviderStatus.Active;

    // Zero Redundancy: Non-mapped convenience accessors to User entity
    [NotMapped]
    public string Name => User != null ? $"{User.FirstName} {User.LastName}".Trim() : string.Empty;

    [NotMapped]
    public string Email => User?.Email ?? string.Empty;

    [NotMapped]
    public string PhoneNumber => User?.PhoneNumber ?? string.Empty;

    [NotMapped]
    public string? Address => User?.Address;

    [NotMapped]
    public string? ProfilePictureUrl => User?.ProfilePictureUrl;

    // Required Navigations
    public virtual ICollection<ProviderAvailability> Availabilities { get; set; } = new List<ProviderAvailability>();
    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
