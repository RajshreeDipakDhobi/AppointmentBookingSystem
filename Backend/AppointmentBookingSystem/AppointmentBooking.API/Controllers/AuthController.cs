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
    private readonly IWebHostEnvironment _env;

    public AuthController(
        IAuthService authService, 
        ILogger<AuthController> logger,
        IWebHostEnvironment env)
    {
        _authService = authService;
        _logger = logger;
        _env = env;
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

    /// <summary>
    /// Update profile details (Name, Phone, Address, City, State, PostalCode) for the authenticated user
    /// </summary>
    [HttpPut("profile")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequestDto request, CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue)
        {
            return UnauthorizedResponse("User session context is invalid.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationResponse();
        }

        var result = await _authService.UpdateProfileAsync(CurrentUserId.Value, request, cancellationToken);
        if (!result.Success || result.Data == null)
        {
            return ErrorResponse(result.Message);
        }

        return OkResponse(result.Data, result.Message);
    }

    /// <summary>
    /// Change password for the authenticated user by verifying current password
    /// </summary>
    [HttpPut("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request, CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue)
        {
            return UnauthorizedResponse("User session context is invalid.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationResponse();
        }

        var result = await _authService.ChangePasswordAsync(CurrentUserId.Value, request, cancellationToken);
        if (!result.Success)
        {
            return ErrorResponse(result.Message);
        }

        return OkResponse(result.Data, result.Message);
    }

    /// <summary>
    /// Upload profile picture / avatar image for the authenticated user
    /// </summary>
    [HttpPost("upload-avatar")]
    [Authorize]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadAvatar(IFormFile? file, CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue)
        {
            return UnauthorizedResponse("User session context is invalid.");
        }

        if (file == null || file.Length == 0)
        {
            return ErrorResponse("No image file was provided for upload.");
        }

        const long maxSizeBytes = 5 * 1024 * 1024;
        if (file.Length > maxSizeBytes)
        {
            return ErrorResponse("Image size exceeds the maximum allowed limit of 5 MB.");
        }

        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif"
        };
        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))
        {
            return ErrorResponse("Invalid image format. Allowed formats: .jpg, .jpeg, .png, .webp, .gif");
        }

        var allowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/png", "image/webp", "image/gif", "image/pjpeg"
        };
        if (!allowedContentTypes.Contains(file.ContentType))
        {
            return ErrorResponse("Invalid file content type. Must be an image file.");
        }

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploadsDir = Path.Combine(webRoot, "uploads", "avatars");
        if (!Directory.Exists(uploadsDir))
        {
            Directory.CreateDirectory(uploadsDir);
        }

        var uniqueFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var filePath = Path.Combine(uploadsDir, uniqueFileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var relativePath = $"/uploads/avatars/{uniqueFileName}";
        var fullUrl = $"{Request.Scheme}://{Request.Host}{relativePath}";

        return OkResponse(new
        {
            url = fullUrl,
            relativeUrl = relativePath,
            fileName = uniqueFileName,
            size = file.Length
        }, "Avatar uploaded successfully.");
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
