namespace AppointmentBooking.BusinessLayer.Features.Cart.DTOs;

public class CartDto
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string? GuestToken { get; set; }
    public int? TenantId { get; set; }
    public List<CartItemDto> Items { get; set; } = new();

    public int TotalItemsCount => Items.Count;
    public decimal TotalPrice => Items.Sum(i => i.Price);
    public int TotalDurationMinutes => Items.Sum(i => i.DurationMinutes);
}

