using AppointmentBooking.DomainLayer.Common;

namespace AppointmentBooking.DomainLayer.Entities;

public class AppointmentItem : BaseEntity
{
    public int AppointmentId { get; set; }
    public virtual Appointment Appointment { get; set; } = null!;

    public int ServiceId { get; set; }
    public virtual Service Service { get; set; } = null!;

    public string ServiceName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int DurationMinutes { get; set; }
}

