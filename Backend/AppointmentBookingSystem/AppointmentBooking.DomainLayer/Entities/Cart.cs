using AppointmentBooking.DomainLayer.Common;

namespace AppointmentBooking.DomainLayer.Entities;

public class Cart : BaseEntity
{
    public int? UserId { get; set; }
    public virtual User? User { get; set; }

    /// <summary>
    /// Anonymous token identifying an unauthenticated guest customer's cart
    /// </summary>
    public string? GuestToken { get; set; }

    public int? TenantId { get; set; }
    public virtual Tenant? Tenant { get; set; }

    public virtual ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}

