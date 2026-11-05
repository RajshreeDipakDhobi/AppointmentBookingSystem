using AppointmentBooking.BusinessLayer.Features.Cart.DTOs;
using AppointmentBooking.DomainLayer.Entities;
using AppointmentBooking.InfrastructureLayer.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CartEntity = AppointmentBooking.DomainLayer.Entities.Cart;

namespace AppointmentBooking.BusinessLayer.Features.Cart;

public class CartService : ICartService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CartService> _logger;

    public CartService(IUnitOfWork unitOfWork, ILogger<CartService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<CartDto> GetCartAsync(int? userId, string? guestToken, int? tenantId, CancellationToken cancellationToken = default)
    {
        // Auto-merge guest cart into user cart if both are present
        if (userId.HasValue && userId.Value > 0 && !string.IsNullOrWhiteSpace(guestToken))
        {
            try
            {
                var guestHasItems = await _unitOfWork.Repository<CartEntity>().Query()
                    .AnyAsync(c => c.GuestToken == guestToken && !c.IsDeleted && c.Items.Any(i => !i.IsDeleted), cancellationToken);

                if (guestHasItems)
                {
                    return await MergeGuestCartAsync(userId.Value, guestToken, tenantId, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-merge in GetCartAsync failed. Falling back to normal retrieval.");
            }
        }

        var cart = await GetCartEntityAsync(userId, guestToken, tenantId, trackChanges: false, cancellationToken);
        if (cart == null)
        {
            return new CartDto
            {
                UserId = userId,
                GuestToken = guestToken,
                TenantId = tenantId,
                Items = new List<CartItemDto>()
            };
        }

        return MapToDto(cart);
    }

    public async Task<CartDto> AddItemAsync(int? userId, AddToCartRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request.ServiceId <= 0)
        {
            throw new ArgumentException("Invalid service specified.");
        }

        // 1. Verify service exists and is active
        var service = await _unitOfWork.Repository<Service>().Query()
            .Include(s => s.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId && s.IsActive && !s.IsDeleted, cancellationToken);

        if (service == null)
        {
            throw new KeyNotFoundException($"Service with ID {request.ServiceId} not found or is currently inactive.");
        }

        var resolvedTenantId = request.TenantId ?? service.User?.TenantId ?? 1;

        // Auto-merge guest cart into user cart if user is authenticated
        if (userId.HasValue && userId.Value > 0 && !string.IsNullOrWhiteSpace(request.GuestToken))
        {
            try
            {
                var guestHasItems = await _unitOfWork.Repository<CartEntity>().Query()
                    .AnyAsync(c => c.GuestToken == request.GuestToken && !c.IsDeleted && c.Items.Any(i => !i.IsDeleted), cancellationToken);

                if (guestHasItems)
                {
                    await MergeGuestCartAsync(userId.Value, request.GuestToken, resolvedTenantId, cancellationToken);
                }
            }
            catch { }
        }

        // 2. Get or create cart
        var cart = await GetOrCreateCartEntityAsync(userId, request.GuestToken, resolvedTenantId, cancellationToken);

        // 3. Check if service is already in cart (Check ALL rows for this cart, including soft-deleted ones)
        var existingItem = await _unitOfWork.Repository<CartItem>().Query()
            .OrderByDescending(i => !i.IsDeleted)
            .ThenByDescending(i => i.Id)
            .FirstOrDefaultAsync(i => i.CartId == cart.Id && i.ServiceId == request.ServiceId, cancellationToken);

        if (existingItem != null)
        {
            if (existingItem.IsDeleted)
            {
                // Reactivate soft-deleted item
                existingItem.IsDeleted = false;
                existingItem.DeletedAt = null;
                existingItem.DeletedBy = null;
                existingItem.Quantity = 1;
                existingItem.UpdatedAt = DateTime.UtcNow;
                existingItem.UpdatedBy = userId.HasValue ? $"User_{userId}" : "Guest";
                cart.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            var cartItem = new CartItem
            {
                CartId = cart.Id,
                ServiceId = service.Id,
                Quantity = 1,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId.HasValue ? $"User_{userId}" : "Guest"
            };

            await _unitOfWork.Repository<CartItem>().AddAsync(cartItem);
            cart.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // Return updated cart
        var reloaded = await GetCartEntityAsync(userId, request.GuestToken, resolvedTenantId, trackChanges: false, cancellationToken);
        return MapToDto(reloaded ?? cart);
    }

    public async Task<CartDto> RemoveItemAsync(int? userId, string? guestToken, int serviceId, int? tenantId, CancellationToken cancellationToken = default)
    {
        var cart = await GetCartEntityAsync(userId, guestToken, tenantId, trackChanges: true, cancellationToken);
        if (cart != null)
        {
            var item = cart.Items.FirstOrDefault(i => i.ServiceId == serviceId && !i.IsDeleted);
            if (item != null)
            {
                item.IsDeleted = true;
                item.DeletedAt = DateTime.UtcNow;
                item.DeletedBy = userId.HasValue ? $"User_{userId.Value}" : "Guest";
                cart.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        var reloaded = await GetCartEntityAsync(userId, guestToken, tenantId, trackChanges: false, cancellationToken);
        return reloaded != null ? MapToDto(reloaded) : new CartDto { UserId = userId, GuestToken = guestToken, TenantId = tenantId };
    }

    public async Task<bool> ClearCartAsync(int? userId, string? guestToken, int? tenantId, CancellationToken cancellationToken = default)
    {
        var cart = await GetCartEntityAsync(userId, guestToken, tenantId, trackChanges: true, cancellationToken);
        if (cart == null) return true;

        foreach (var item in cart.Items.Where(i => !i.IsDeleted).ToList())
        {
            item.IsDeleted = true;
            item.DeletedAt = DateTime.UtcNow;
            item.DeletedBy = userId.HasValue ? $"User_{userId.Value}" : "Guest";
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CartDto> MergeGuestCartAsync(int userId, string guestToken, int? tenantId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(guestToken))
        {
            return await GetCartAsync(userId, null, tenantId, cancellationToken);
        }

        // 1. Find guest cart
        var guestCart = await _unitOfWork.Repository<CartEntity>().Query()
            .Include(c => c.Items.Where(i => !i.IsDeleted))
                .ThenInclude(i => i.Service)
            .FirstOrDefaultAsync(c => c.GuestToken == guestToken && !c.IsDeleted, cancellationToken);

        if (guestCart == null || guestCart.Items.Count == 0)
        {
            return await GetCartAsync(userId, null, tenantId, cancellationToken);
        }

        var targetTenantId = tenantId ?? guestCart.TenantId ?? 1;

        // 2. Find user cart
        var userCart = await GetCartEntityAsync(userId, null, targetTenantId, trackChanges: true, cancellationToken);

        if (userCart == null)
        {
            // Re-assign guest cart to authenticated user
            guestCart.UserId = userId;
            guestCart.GuestToken = null;
            guestCart.TenantId = targetTenantId;
            guestCart.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return MapToDto(guestCart);
        }

        // 3. Load all user cart items (including soft-deleted) to prevent any duplicate key errors
        var allUserItems = await _unitOfWork.Repository<CartItem>().Query()
            .Where(i => i.CartId == userCart.Id)
            .ToListAsync(cancellationToken);

        var userItemsByServiceId = allUserItems
            .GroupBy(i => i.ServiceId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => !i.IsDeleted).ThenByDescending(i => i.Id).First());

        foreach (var gItem in guestCart.Items.ToList())
        {
            if (userItemsByServiceId.TryGetValue(gItem.ServiceId, out var existingUserItem))
            {
                // Reactivate if previously soft-deleted
                if (existingUserItem.IsDeleted)
                {
                    existingUserItem.IsDeleted = false;
                    existingUserItem.DeletedAt = null;
                    existingUserItem.DeletedBy = null;
                    existingUserItem.Quantity = 1;
                    existingUserItem.UpdatedAt = DateTime.UtcNow;
                    existingUserItem.UpdatedBy = $"User_{userId}";
                }
            }
            else
            {
                var newItem = new CartItem
                {
                    CartId = userCart.Id,
                    ServiceId = gItem.ServiceId,
                    Quantity = 1,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = $"User_{userId}"
                };
                await _unitOfWork.Repository<CartItem>().AddAsync(newItem);
                userItemsByServiceId[gItem.ServiceId] = newItem;
            }

            // Soft-delete the guest item
            gItem.IsDeleted = true;
            gItem.DeletedAt = DateTime.UtcNow;
        }

        // 4. Soft-delete the empty guest cart and clear token
        guestCart.IsDeleted = true;
        guestCart.DeletedAt = DateTime.UtcNow;
        guestCart.GuestToken = null;
        userCart.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var merged = await GetCartEntityAsync(userId, null, targetTenantId, trackChanges: false, cancellationToken);
        return MapToDto(merged ?? userCart);
    }

    private async Task<CartEntity?> GetCartEntityAsync(int? userId, string? guestToken, int? tenantId, bool trackChanges, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Repository<CartEntity>().Query();
        if (!trackChanges) query = query.AsNoTracking();

        query = query
            .Include(c => c.Items.Where(i => !i.IsDeleted))
                .ThenInclude(i => i.Service);

        if (userId.HasValue && userId.Value > 0)
        {
            // 1. Try to find cart for this user matching tenant if specified
            var userCart = await query
                .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
                .FirstOrDefaultAsync(c =>
                    c.UserId == userId.Value &&
                    (!tenantId.HasValue || !c.TenantId.HasValue || c.TenantId == tenantId.Value) &&
                    !c.IsDeleted,
                    cancellationToken);

            if (userCart != null) return userCart;

            // 2. Fallback: Any active cart for this user
            return await query
                .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
                .FirstOrDefaultAsync(c => c.UserId == userId.Value && !c.IsDeleted, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(guestToken))
        {
            return await query
                .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
                .FirstOrDefaultAsync(c =>
                    c.GuestToken == guestToken &&
                    (!tenantId.HasValue || !c.TenantId.HasValue || c.TenantId == tenantId.Value) &&
                    !c.IsDeleted,
                    cancellationToken);
        }

        return null;
    }

    private async Task<CartEntity> GetOrCreateCartEntityAsync(int? userId, string? guestToken, int? tenantId, CancellationToken cancellationToken)
    {
        var existing = await GetCartEntityAsync(userId, guestToken, tenantId, trackChanges: true, cancellationToken);
        if (existing != null)
        {
            // If an anonymous guest cart is now being accessed with a logged-in userId, link it
            if (userId.HasValue && userId.Value > 0 && !existing.UserId.HasValue)
            {
                existing.UserId = userId.Value;
                existing.GuestToken = null;
                existing.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            return existing;
        }

        var newCart = new CartEntity
        {
            UserId = (userId.HasValue && userId.Value > 0) ? userId.Value : null,
            GuestToken = (userId.HasValue && userId.Value > 0) ? null : (!string.IsNullOrWhiteSpace(guestToken) ? guestToken.Trim() : Guid.NewGuid().ToString("N")),
            TenantId = tenantId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId.HasValue ? $"User_{userId.Value}" : "Guest"
        };

        await _unitOfWork.Repository<CartEntity>().AddAsync(newCart);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return newCart;
    }

    private static CartDto MapToDto(CartEntity cart)
    {
        return new CartDto
        {
            Id = cart.Id,
            UserId = cart.UserId,
            GuestToken = cart.GuestToken,
            TenantId = cart.TenantId,
            Items = cart.Items
                .Where(i => !i.IsDeleted && i.Service != null)
                .Select(i => new CartItemDto
                {
                    Id = i.Id,
                    ServiceId = i.ServiceId,
                    ServiceName = i.Service?.Name ?? string.Empty,
                    ServiceDescription = i.Service?.Description,
                    Price = i.Service?.Price ?? 0m,
                    DurationMinutes = i.Service?.DurationMinutes > 0 ? i.Service.DurationMinutes : 30,
                    Quantity = 1
                })
                .ToList()
        };
    }
}
