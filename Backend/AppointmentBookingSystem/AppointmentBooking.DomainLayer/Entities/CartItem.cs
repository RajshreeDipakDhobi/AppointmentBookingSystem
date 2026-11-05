using AppointmentBooking.DomainLayer.Common;

namespace AppointmentBooking.DomainLayer.Entities;

public class CartItem : BaseEntity
{
    public int CartId { get; set; }
    public virtual Cart Cart { get; set; } = null!;

    public int ServiceId { get; set; }
    public virtual Service Service { get; set; } = null!;

    /// <summary>
    /// Guaranteed 1 unit per service
    /// </summary>
    public int Quantity { get; set; } = 1;
}

