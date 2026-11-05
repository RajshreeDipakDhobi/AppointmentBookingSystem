using AppointmentBooking.BusinessLayer.Features.Cart;
using AppointmentBooking.BusinessLayer.Features.Cart.DTOs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppointmentBooking.API.Controllers;

[Route("api/[controller]")]
public class CartController : BaseApiController
{
    private readonly ICartService _cartService;
    private readonly ILogger<CartController> _logger;

    public CartController(ICartService cartService, ILogger<CartController> logger)
    {
        _cartService = cartService;
        _logger = logger;
    }

    /// <summary>
    /// Explicitly authenticates optional JWT Bearer / accessToken cookie for anonymous endpoints
    /// </summary>
    private async Task<int?> GetEffectiveUserIdAsync()
    {
        if (CurrentUserId.HasValue && CurrentUserId.Value > 0)
        {
            return CurrentUserId.Value;
        }

        try
        {
            var authResult = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            if (authResult.Succeeded && authResult.Principal != null)
            {
                HttpContext.User = authResult.Principal;
                return CurrentUserId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Optional JWT authentication evaluation did not yield a user session.");
        }

        return null;
    }

    private string? ResolveGuestToken(string? fromParam)
    {
        if (!string.IsNullOrWhiteSpace(fromParam)) return fromParam.Trim();
        if (Request.Headers.TryGetValue("X-Guest-Cart-Token", out var headerVal))
        {
            var token = headerVal.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(token)) return token;
        }
        return null;
    }

    /// <summary>
    /// Get the current user or guest's persistent cart
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCart(
        [FromQuery] int? tenantId,
        [FromQuery] string? guestToken,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetEffectiveUserIdAsync();
        var resolvedTenantId = (tenantId.HasValue && tenantId.Value > 0) ? tenantId.Value : CurrentTenantId;
        var token = ResolveGuestToken(guestToken);

        var cart = await _cartService.GetCartAsync(userId, token, resolvedTenantId, cancellationToken);
        return Ok(cart);
    }

    /// <summary>
    /// Add a single service to the cart (enforcing 1 item per service)
    /// </summary>
    [HttpPost("items")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddItem(
        [FromBody] AddToCartRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request == null || request.ServiceId <= 0)
        {
            return BadRequest("Invalid service item.");
        }

        var userId = await GetEffectiveUserIdAsync();
        request.GuestToken = ResolveGuestToken(request.GuestToken);
        request.TenantId ??= CurrentTenantId;

        try
        {
            var cart = await _cartService.AddItemAsync(userId, request, cancellationToken);
            return Ok(cart);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Remove a service from the cart
    /// </summary>
    [HttpDelete("items/{serviceId:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveItem(
        [FromRoute] int serviceId,
        [FromQuery] int? tenantId,
        [FromQuery] string? guestToken,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetEffectiveUserIdAsync();
        var resolvedTenantId = (tenantId.HasValue && tenantId.Value > 0) ? tenantId.Value : CurrentTenantId;
        var token = ResolveGuestToken(guestToken);

        var cart = await _cartService.RemoveItemAsync(userId, token, serviceId, resolvedTenantId, cancellationToken);
        return Ok(cart);
    }

    /// <summary>
    /// Clear all items from current cart
    /// </summary>
    [HttpDelete]
    [AllowAnonymous]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearCart(
        [FromQuery] int? tenantId,
        [FromQuery] string? guestToken,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetEffectiveUserIdAsync();
        var resolvedTenantId = (tenantId.HasValue && tenantId.Value > 0) ? tenantId.Value : CurrentTenantId;
        var token = ResolveGuestToken(guestToken);

        var result = await _cartService.ClearCartAsync(userId, token, resolvedTenantId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Merge guest cart items into authenticated user account upon sign-in
    /// </summary>
    [HttpPost("merge")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> MergeGuestCart(
        [FromBody] MergeCartRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetEffectiveUserIdAsync();
        if (!userId.HasValue)
        {
            return Unauthorized("User is not authenticated.");
        }

        var token = ResolveGuestToken(request.GuestToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            var currentCart = await _cartService.GetCartAsync(userId.Value, null, request.TenantId ?? CurrentTenantId, cancellationToken);
            return Ok(currentCart);
        }

        var resolvedTenantId = request.TenantId ?? CurrentTenantId;
        var cart = await _cartService.MergeGuestCartAsync(userId.Value, token, resolvedTenantId, cancellationToken);
        return Ok(cart);
    }
}
