using AppointmentBooking.BusinessLayer.Common.Models;
using AppointmentBooking.BusinessLayer.Features.Tenants;
using AppointmentBooking.BusinessLayer.Features.Tenants.DTOs;
using AppointmentBooking.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppointmentBooking.API.Controllers;

[Route("api/[controller]")]
[Authorize(Roles = AppRoles.SuperAdmin)]
public class TenantsController : BaseApiController
{
    private readonly ITenantService _tenantService;
    private readonly ILogger<TenantsController> _logger;

    public TenantsController(ITenantService tenantService, ILogger<TenantsController> logger)
    {
        _tenantService = tenantService;
        _logger = logger;
    }

    /// <summary>
    /// Public directory of active tenant organizations for customer booking portal
    /// </summary>
    [HttpGet("active")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<TenantDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveTenants(CancellationToken cancellationToken)
    {
        var tenants = await _tenantService.GetAllTenantsAsync(cancellationToken);
        var activeTenants = tenants
            .Where(t => t.Status == DomainLayer.Enums.TenantStatus.Active)
            .OrderBy(t => t.Name)
            .ToList();
        return OkResponse(activeTenants);
    }

    /// <summary>
    /// Get all tenants registered on the platform with server-side pagination (SuperAdmin only)
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TenantDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllTenants(
        [FromQuery] PaginationParams paginationParams, 
        CancellationToken cancellationToken)
    {
        var result = await _tenantService.GetTenantsPagedAsync(paginationParams, cancellationToken);
        return OkResponse(result);
    }


    /// <summary>
    /// Get tenant organization details by Tenant ID (Public for customer portal & admin)
    /// </summary>
    [HttpGet("{tenantId:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TenantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenantById(int tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var tenant = await _tenantService.GetTenantByIdAsync(tenantId, cancellationToken);
            if (tenant == null)
            {
                return NotFoundResponse($"Tenant with ID {tenantId} was not found.");
            }
            return OkResponse(tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with ID {TenantId}", tenantId);
            return ErrorResponse(ex.Message);
        }
    }

    /// <summary>
    /// Register a new tenant organization and provision its initial Tenant Admin user
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<TenantDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTenant(
        [FromBody] CreateTenantRequestDto request, 
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationResponse();
        }

        var creatorEmail = CurrentUserEmail ?? "superadmin@platform.com";
        var result = await _tenantService.CreateTenantAsync(request, creatorEmail, cancellationToken);

        if (!result.Success || result.Data == null)
        {
            return ConflictResponse(result.Message);
        }

        return CreatedResponse(
            nameof(GetTenantById), 
            new { tenantId = result.Data.Id }, 
            result.Data,
            result.Message);
    }

    /// <summary>
    /// Update tenant organization profile (Name, Contact Email, Phone, Address, Logo)
    /// </summary>
    [HttpPut("{tenantId:int}")]
    [ProducesResponseType(typeof(TenantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTenant(
        int tenantId, 
        [FromBody] UpdateTenantRequestDto request, 
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationResponse();
        }

        try
        {
            var updatedTenant = await _tenantService.UpdateTenantAsync(tenantId, request, cancellationToken);
            if (updatedTenant == null)
            {
                return NotFoundResponse($"Tenant with ID {tenantId} was not found.");
            }

            return OkResponse(updatedTenant, "Tenant profile updated successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update tenant profile for ID {TenantId}", tenantId);
            return ErrorResponse(ex.Message);
        }
    }

    /// <summary>
    /// Update tenant operational status (Active, Suspended, Inactive)
    /// </summary>
    [HttpPatch("{tenantId:int}/status")]
    [HttpPut("{tenantId:int}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTenantStatus(
        int tenantId, 
        [FromBody] UpdateTenantStatusDto request, 
        CancellationToken cancellationToken)
    {
        try
        {
            var success = await _tenantService.UpdateTenantStatusAsync(tenantId, request.Status, cancellationToken);
            if (!success)
            {
                return NotFoundResponse($"Tenant with ID {tenantId} was not found.");
            }

            return OkResponse<object?>(
                new { tenantId, status = request.Status }, 
                $"Tenant status updated to {request.Status} successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update status for tenant ID {TenantId}", tenantId);
            return ErrorResponse(ex.Message);
        }
    }
}
