using AppointmentBooking.DomainLayer.Common;

namespace AppointmentBooking.DomainLayer.Entities;

public class ProviderServiceLink : BaseEntity
{
    public int ServiceProviderId { get; set; }
    public virtual ServiceProvider ServiceProvider { get; set; } = null!;

    public int ServiceId { get; set; }
    public virtual Service Service { get; set; } = null!;
}

