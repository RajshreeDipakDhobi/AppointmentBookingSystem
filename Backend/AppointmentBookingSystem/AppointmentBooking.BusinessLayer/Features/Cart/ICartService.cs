using AppointmentBooking.BusinessLayer.Features.Cart.DTOs;

namespace AppointmentBooking.BusinessLayer.Features.Cart;

public interface ICartService
{
    Task<CartDto> GetCartAsync(int? userId, string? guestToken, int? tenantId, CancellationToken cancellationToken = default);
    Task<CartDto> AddItemAsync(int? userId, AddToCartRequestDto request, CancellationToken cancellationToken = default);
    Task<CartDto> RemoveItemAsync(int? userId, string? guestToken, int serviceId, int? tenantId, CancellationToken cancellationToken = default);
    Task<bool> ClearCartAsync(int? userId, string? guestToken, int? tenantId, CancellationToken cancellationToken = default);
    Task<CartDto> MergeGuestCartAsync(int userId, string guestToken, int? tenantId, CancellationToken cancellationToken = default);
}

