using AppointmentBooking.DomainLayer.Common;
using AppointmentBooking.DomainLayer.Enums;

namespace AppointmentBooking.DomainLayer.Entities;

public class ServiceProvider : BaseEntity
{
    public int TenantId { get; set; }

    // Linked User (Source of truth for Name, Email, Phone, Address, Avatar)
    public int UserId { get; set; }
    public virtual User User { get; set; } = null!;

    // Linked Master Service (Source of truth for Service Name, Duration, Price)
    public int? ServiceId { get; set; }
    public virtual Service? Service { get; set; }

    public string? Bio { get; set; }
    public ProviderStatus Status { get; set; } = ProviderStatus.Active;

    // Required Navigations
    public virtual ICollection<ProviderAvailability> Availabilities { get; set; } = new List<ProviderAvailability>();
    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
