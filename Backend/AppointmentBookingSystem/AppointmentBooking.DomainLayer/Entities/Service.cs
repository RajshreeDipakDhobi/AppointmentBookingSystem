using AppointmentBooking.DomainLayer.Common;

namespace AppointmentBooking.DomainLayer.Entities;

public class Service : BaseEntity
{
    // Owner / Creator User link (Single source of truth)
    public int UserId { get; set; }
    public virtual User User { get; set; } = null!;

    // Catalog details
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public decimal Price { get; set; } = 0.00m;
    public bool IsActive { get; set; } = true;

    // Required Navigations
    public virtual ICollection<ServiceProvider> ServiceProviders { get; set; } = new List<ServiceProvider>();
    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}

