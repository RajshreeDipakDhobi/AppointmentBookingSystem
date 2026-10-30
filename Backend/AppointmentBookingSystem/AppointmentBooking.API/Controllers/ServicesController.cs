using AppointmentBooking.BusinessLayer.Common.Models;
using AppointmentBooking.BusinessLayer.Features.Services;
using AppointmentBooking.BusinessLayer.Features.Services.DTOs;
using AppointmentBooking.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppointmentBooking.API.Controllers;

[Route("api/[controller]")]
public class ServicesController : BaseApiController
{
    private readonly IServiceCatalogService _serviceCatalogService;
    private readonly ILogger<ServicesController> _logger;

    public ServicesController(
        IServiceCatalogService serviceCatalogService,
        ILogger<ServicesController> logger)
    {
        _serviceCatalogService = serviceCatalogService;
        _logger = logger;
    }

    /// <summary>
    /// Public endpoint for Customer Portal & Booking Modal: Get all active services
    /// </summary>
    [HttpGet("active")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveServices(
        [FromQuery] int? tenantId,
        CancellationToken cancellationToken)
    {
        var resolvedTenantId = (tenantId.HasValue && tenantId.Value > 0)
            ? tenantId.Value
            : ResolveTenantId(tenantId);

        var services = await _serviceCatalogService.GetActiveServicesAsync(resolvedTenantId, cancellationToken);
        return OkResponse(services);
    }

    /// <summary>
    /// Get all services (active and inactive) for administration
    /// </summary>
    [HttpGet]
    [Authorize(Roles = AppRoles.AdminOrSuperAdmin)]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllServices(
        [FromQuery] int? tenantId,
        CancellationToken cancellationToken)
    {
        var resolvedTenantId = ResolveTenantId(tenantId);
        var services = await _serviceCatalogService.GetAllServicesAsync(resolvedTenantId, cancellationToken);
        return OkResponse(services);
    }

    /// <summary>
    /// Get service details by ID
    /// </summary>
    [HttpGet("{id:int}")]
    [Authorize]
    [ProducesResponseType(typeof(ServiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetServiceById(int id, CancellationToken cancellationToken)
    {
        var service = await _serviceCatalogService.GetServiceByIdAsync(id, cancellationToken);
        if (service == null)
        {
            return ErrorResponse($"Service with ID {id} was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(service);
    }

    /// <summary>
    /// Create a new service under the active organization
    /// </summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.AdminOrSuperAdmin)]
    [ProducesResponseType(typeof(ServiceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateService(
        [FromBody] CreateServiceRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var userId = CurrentUserId;
        if (!userId.HasValue || userId.Value <= 0)
        {
            return ErrorResponse("Unable to identify current authenticated user.", StatusCodes.Status401Unauthorized);
        }

        var result = await _serviceCatalogService.CreateServiceAsync(
            userId.Value,
            request,
            CurrentUserName ?? CurrentUserEmail ?? "System",
            cancellationToken);

        if (!result.Success)
        {
            return ErrorResponse(result.Message, StatusCodes.Status400BadRequest);
        }

        return CreatedAtAction(nameof(GetServiceById), new { id = result.Data!.Id }, result.Data);
    }

    /// <summary>
    /// Update existing service details
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.AdminOrSuperAdmin)]
    [ProducesResponseType(typeof(ServiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateService(
        int id,
        [FromBody] UpdateServiceRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var updated = await _serviceCatalogService.UpdateServiceAsync(
                id,
                request,
                CurrentUserName ?? CurrentUserEmail ?? "System",
                cancellationToken);

            if (updated == null)
            {
                return ErrorResponse($"Service with ID {id} was not found.", StatusCodes.Status404NotFound);
            }

            return OkResponse(updated, "Service updated successfully.");
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResponse(ex.Message, StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>
    /// Toggle active / inactive status of a service
    /// </summary>
    [HttpPatch("{id:int}/toggle-status")]
    [Authorize(Roles = AppRoles.AdminOrSuperAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleStatus(int id, CancellationToken cancellationToken)
    {
        var success = await _serviceCatalogService.ToggleServiceStatusAsync(
            id,
            CurrentUserName ?? CurrentUserEmail ?? "System",
            cancellationToken);

        if (!success)
        {
            return ErrorResponse($"Service with ID {id} was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(new { id, message = "Service status toggled successfully." });
    }

    /// <summary>
    /// Soft delete a service (only if no active appointments are linked)
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.AdminOrSuperAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteService(int id, CancellationToken cancellationToken)
    {
        try
        {
            var success = await _serviceCatalogService.DeleteServiceAsync(
                id,
                CurrentUserName ?? CurrentUserEmail ?? "System",
                cancellationToken);

            if (!success)
            {
                return ErrorResponse($"Service with ID {id} was not found.", StatusCodes.Status404NotFound);
            }

            return OkResponse(new { id, message = "Service deleted successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResponse(ex.Message, StatusCodes.Status400BadRequest);
        }
    }
}

