namespace AppointmentBooking.BusinessLayer.Features.Cart.DTOs;

public class CartItemDto
{
    public int Id { get; set; }
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string? ServiceDescription { get; set; }
    public decimal Price { get; set; }
    public int DurationMinutes { get; set; }
    public int Quantity { get; set; } = 1;
}

