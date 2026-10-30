using AppointmentBooking.BusinessLayer.Common.Models;
using AppointmentBooking.BusinessLayer.Features.Providers;
using AppointmentBooking.BusinessLayer.Features.Providers.DTOs;
using AppointmentBooking.DomainLayer.Constants;
using AppointmentBooking.DomainLayer.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppointmentBooking.API.Controllers;

[Route("api/[controller]")]
[Authorize(Roles = AppRoles.AdminOrSuperAdmin)]
public class ProvidersController : BaseApiController
{
    private readonly IServiceProviderService _providerService;
    private readonly ILogger<ProvidersController> _logger;
    private readonly IWebHostEnvironment _env;

    public ProvidersController(
        IServiceProviderService providerService, 
        ILogger<ProvidersController> logger,
        IWebHostEnvironment env)
    {
        _providerService = providerService;
        _logger = logger;
        _env = env;
    }

    /// <summary>
    /// Public endpoint for Customer Portal: Get all active specialists for booking
    /// </summary>
    [HttpGet("active")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceProviderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveProviders(
        [FromQuery] int? tenantId, 
        CancellationToken cancellationToken)
    {
        var resolvedTenantId = (tenantId.HasValue && tenantId.Value > 0)
            ? tenantId.Value
            : ResolveTenantId(tenantId);

        var providers = await _providerService.GetActiveProvidersAsync(resolvedTenantId, cancellationToken);
        return OkResponse(providers);
    }

    /// <summary>
    /// Get all service providers for the current tenant clinic with server-side pagination
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ServiceProviderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetProviders(
        [FromQuery] int? tenantId, 
        [FromQuery] PaginationParams paginationParams,
        [FromQuery] ProviderStatus? status,
        CancellationToken cancellationToken)
    {
        var activeTenantId = ResolveTenantId(tenantId);
        if (!activeTenantId.HasValue)
        {
            return ErrorResponse("Tenant context is required.");
        }

        var result = await _providerService.GetProvidersPagedAsync(
            activeTenantId.Value, 
            paginationParams, 
            status, 
            cancellationToken);

        return OkResponse(result);
    }

    /// <summary>
    /// Get service provider details by Provider ID
    /// </summary>
    [HttpGet("{providerId:int}")]
    [ProducesResponseType(typeof(ServiceProviderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProviderById(
        int providerId, 
        [FromQuery] int? tenantId, 
        CancellationToken cancellationToken)
    {
        var activeTenantId = ResolveTenantId(tenantId);
        if (!activeTenantId.HasValue)
        {
            return ErrorResponse("Tenant context is required.");
        }

        var provider = await _providerService.GetProviderByIdAsync(providerId, activeTenantId.Value, cancellationToken);
        if (provider == null)
        {
            return NotFoundResponse($"Provider with ID {providerId} was not found.");
        }

        return OkResponse(provider);
    }

    /// <summary>
    /// Create and onboard a new service provider in the current tenant clinic
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ServiceProviderDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateProvider(
        [FromBody] CreateServiceProviderRequestDto request, 
        [FromQuery] int? tenantId, 
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationResponse();
        }

        var activeTenantId = ResolveTenantId(tenantId);
        if (!activeTenantId.HasValue)
        {
            return ErrorResponse("Tenant context is required.");
        }

        var creatorEmail = CurrentUserEmail ?? "system";
        var result = await _providerService.CreateProviderAsync(
            activeTenantId.Value, 
            request, 
            creatorEmail, 
            cancellationToken);

        if (!result.Success || result.Data == null)
        {
            return ConflictResponse(result.Message);
        }

        return CreatedResponse(
            nameof(GetProviderById), 
            new { providerId = result.Data.Id }, 
            result.Data,
            result.Message);
    }

    /// <summary>
    /// Update existing service provider profile (Name, Specialty, Bio, Phone, Email, Photo)
    /// </summary>
    [HttpPut("{providerId:int}")]
    [ProducesResponseType(typeof(ServiceProviderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProvider(
        int providerId, 
        [FromBody] UpdateServiceProviderRequestDto request, 
        [FromQuery] int? tenantId, 
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationResponse();
        }

        var activeTenantId = ResolveTenantId(tenantId);
        if (!activeTenantId.HasValue)
        {
            return ErrorResponse("Tenant context is required.");
        }

        try
        {
            var updated = await _providerService.UpdateProviderAsync(
                providerId, 
                activeTenantId.Value, 
                request, 
                cancellationToken);

            if (updated == null)
            {
                return NotFoundResponse($"Provider with ID {providerId} was not found.");
            }

            return OkResponse(updated, "Provider profile updated successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update service provider ID {ProviderId}", providerId);
            return ErrorResponse(ex.Message);
        }
    }

    /// <summary>
    /// Update provider lifecycle status (Active, Inactive, OnLeave)
    /// </summary>
    [HttpPatch("{providerId:int}/status")]
    [HttpPut("{providerId:int}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        int providerId, 
        [FromBody] ProviderStatus status, 
        [FromQuery] int? tenantId, 
        CancellationToken cancellationToken)
    {
        var activeTenantId = ResolveTenantId(tenantId);
        if (!activeTenantId.HasValue)
        {
            return ErrorResponse("Tenant context is required.");
        }

        try
        {
            var success = await _providerService.UpdateProviderStatusAsync(
                providerId, 
                activeTenantId.Value, 
                status, 
                cancellationToken);

            if (!success)
            {
                return NotFoundResponse($"Provider with ID {providerId} was not found.");
            }

            return OkResponse<object?>(
                new { providerId, status }, 
                $"Provider status updated to {status} successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update status for provider ID {ProviderId}", providerId);
            return ErrorResponse(ex.Message);
        }
    }

    /// <summary>
    /// Deactivate or delete service provider from the clinic roster
    /// </summary>
    [HttpDelete("{providerId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProvider(
        int providerId, 
        [FromQuery] int? tenantId, 
        CancellationToken cancellationToken)
    {
        var activeTenantId = ResolveTenantId(tenantId);
        if (!activeTenantId.HasValue)
        {
            return ErrorResponse("Tenant context is required.");
        }

        try
        {
            var success = await _providerService.DeleteProviderAsync(
                providerId, 
                activeTenantId.Value, 
                cancellationToken);

            if (!success)
            {
                return NotFoundResponse($"Provider with ID {providerId} was not found.");
            }

            return OkResponse<object?>(
                new { providerId }, 
                $"Provider with ID {providerId} has been deleted successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete service provider ID {ProviderId}", providerId);
            return ErrorResponse(ex.Message);
        }
    }

    /// <summary>
    /// Upload provider profile picture / avatar image to server storage
    /// </summary>
    [HttpPost("upload-avatar")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAvatar(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return ErrorResponse("No image file was provided for upload.");
        }

        // Limit size to 5 MB
        const long maxSizeBytes = 5 * 1024 * 1024;
        if (file.Length > maxSizeBytes)
        {
            return ErrorResponse("Image size exceeds the maximum allowed limit of 5 MB.");
        }

        // Validate allowed extensions
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif"
        };
        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))
        {
            return ErrorResponse("Invalid image format. Allowed formats: .jpg, .jpeg, .png, .webp, .gif");
        }

        // Validate content types
        var allowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/png", "image/webp", "image/gif", "image/pjpeg"
        };
        if (!allowedContentTypes.Contains(file.ContentType))
        {
            return ErrorResponse("Invalid file content type. Must be an image file.");
        }

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploadsDir = Path.Combine(webRoot, "uploads", "providers");
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

        var relativePath = $"/uploads/providers/{uniqueFileName}";
        var fullUrl = $"{Request.Scheme}://{Request.Host}{relativePath}";

        _logger.LogInformation("Provider profile picture uploaded successfully: {FileName} ({Bytes} bytes)", uniqueFileName, file.Length);

        return OkResponse(new
        {
            url = fullUrl,
            relativeUrl = relativePath,
            fileName = uniqueFileName,
            size = file.Length
        }, "Profile picture uploaded successfully.");
    }

    /// <summary>
    /// Upload and update profile picture directly for an existing service provider
    /// </summary>
    [HttpPost("{providerId:int}/avatar")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ServiceProviderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadProviderAvatar(
        int providerId,
        IFormFile? file,
        [FromQuery] int? tenantId,
        CancellationToken cancellationToken)
    {
        var activeTenantId = ResolveTenantId(tenantId);
        if (!activeTenantId.HasValue)
        {
            return ErrorResponse("Tenant context is required.");
        }

        var provider = await _providerService.GetProviderByIdAsync(providerId, activeTenantId.Value, cancellationToken);
        if (provider == null)
        {
            return NotFoundResponse($"Provider with ID {providerId} was not found.");
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
        var uploadsDir = Path.Combine(webRoot, "uploads", "providers");
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

        var fullUrl = $"{Request.Scheme}://{Request.Host}/uploads/providers/{uniqueFileName}";

        var updateDto = new UpdateServiceProviderRequestDto
        {
            Name = provider.Name,
            ServiceId = provider.ServiceId,
            ServiceType = provider.ServiceType,
            Email = provider.Email,
            PhoneNumber = provider.PhoneNumber,
            Address = provider.Address,
            Bio = provider.Bio,
            ProfilePictureUrl = fullUrl,
            Status = provider.Status
        };

        var updated = await _providerService.UpdateProviderAsync(providerId, activeTenantId.Value, updateDto, cancellationToken);
        _logger.LogInformation("Provider {ProviderId} avatar updated successfully to {FileName}", providerId, uniqueFileName);
        return OkResponse(updated, "Provider profile picture updated successfully.");
    }
}
