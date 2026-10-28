using AppointmentBooking.BusinessLayer.Features.Auth;
using AppointmentBooking.BusinessLayer.Features.Auth.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppointmentBooking.API.Controllers;

[Route("api/[controller]")]
public class AuthController : BaseApiController
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Authenticate a user with email and password and store tokens in HttpOnly Secure Cookies
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationResponse();
        }

        var result = await _authService.LoginAsync(request, cancellationToken);
        if (!result.Success || result.Data == null)
        {
            return UnauthorizedResponse(result.Message);
        }

        SetTokenCookies(result.Data.AccessToken, result.Data.RefreshToken, result.Data.ExpiresAt);
        return OkResponse(result.Data, result.Message);
    }

    /// <summary>
    /// Register a new Customer account and issue authenticated session
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterCustomerRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationResponse();
        }

        var result = await _authService.RegisterCustomerAsync(request, cancellationToken);
        if (!result.Success || result.Data == null)
        {
            return ConflictResponse(result.Message);
        }

        SetTokenCookies(result.Data.AccessToken, result.Data.RefreshToken, result.Data.ExpiresAt);
        return StatusCode(StatusCodes.Status201Created, new { success = true, message = result.Message, data = result.Data });
    }

    /// <summary>
    /// Refresh access token using HttpOnly refresh token cookie
    /// </summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RefreshTokenRequestDto? request, 
        CancellationToken cancellationToken)
    {
        var rawRefreshToken = Request.Cookies["refreshToken"] ?? request?.RefreshToken;

        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return UnauthorizedResponse("No refresh token provided in cookie or request payload.");
        }

        var result = await _authService.RefreshTokenAsync(
            new RefreshTokenRequestDto { RefreshToken = rawRefreshToken }, 
            cancellationToken);

        if (!result.Success || result.Data == null)
        {
            ClearTokenCookies();
            return UnauthorizedResponse(result.Message);
        }

        SetTokenCookies(result.Data.AccessToken, result.Data.RefreshToken, result.Data.ExpiresAt);
        return OkResponse(result.Data, result.Message);
    }

    /// <summary>
    /// Logout: Revoke refresh token and clear HttpOnly cookies
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RefreshTokenRequestDto? request, 
        CancellationToken cancellationToken)
    {
        var rawRefreshToken = Request.Cookies["refreshToken"] ?? request?.RefreshToken;

        if (!string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            await _authService.RevokeTokenAsync(rawRefreshToken, cancellationToken);
        }

        ClearTokenCookies();
        return OkResponse<object?>(null, "Successfully logged out.");
    }

    /// <summary>
    /// Get the profile details of the currently authenticated user
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue)
        {
            return UnauthorizedResponse("User session context is invalid.");
        }

        var result = await _authService.GetCurrentUserAsync(CurrentUserId.Value, cancellationToken);
        if (!result.Success || result.Data == null)
        {
            return NotFoundResponse(result.Message);
        }

        return OkResponse(result.Data, result.Message);
    }

    private void SetTokenCookies(string accessToken, string refreshToken, DateTime accessExpiresAt)
    {
        var isHttps = Request.IsHttps;

        var accessCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Expires = accessExpiresAt,
            Path = "/"
        };

        var refreshCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Expires = DateTime.UtcNow.AddDays(7),
            Path = "/"
        };

        Response.Cookies.Append("accessToken", accessToken, accessCookieOptions);
        Response.Cookies.Append("refreshToken", refreshToken, refreshCookieOptions);
    }

    private void ClearTokenCookies()
    {
        var isHttps = Request.IsHttps;
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Expires = DateTime.UtcNow.AddDays(-1),
            Path = "/"
        };

        Response.Cookies.Delete("accessToken", options);
        Response.Cookies.Delete("refreshToken", options);
    }
}
