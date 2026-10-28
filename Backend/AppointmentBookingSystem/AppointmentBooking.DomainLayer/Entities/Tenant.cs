using AppointmentBooking.DomainLayer.Common;
using AppointmentBooking.DomainLayer.Enums;

namespace AppointmentBooking.DomainLayer.Entities;

public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string? LogoUrl { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Active;

    // Navigations (Only Users navigation as required)
    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
