namespace AppointmentBooking.BusinessLayer.Features.Cart.DTOs;

public class AddToCartRequestDto
{
    public int ServiceId { get; set; }
    public int? TenantId { get; set; }
    public string? GuestToken { get; set; }
}

public class MergeCartRequestDto
{
    public string GuestToken { get; set; } = string.Empty;
    public int? TenantId { get; set; }
}

