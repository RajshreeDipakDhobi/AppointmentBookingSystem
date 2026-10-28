using AppointmentBooking.DomainLayer.Constants;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AppointmentBooking.API.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// Authenticated User ID from JWT Token Claims
    /// </summary>
    protected int? CurrentUserId
    {
        get
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                       ?? User.FindFirst("sub")?.Value;

            return int.TryParse(idClaim, out int id) ? id : null;
        }
    }

    /// <summary>
    /// Tenant ID associated with the authenticated User's session
    /// </summary>
    protected int? CurrentTenantId
    {
        get
        {
            var tenantClaim = User.FindFirst("tenant_id")?.Value;
            return int.TryParse(tenantClaim, out int tenantId) ? tenantId : null;
        }
    }

    /// <summary>
    /// User Email from JWT Token Claims
    /// </summary>
    protected string? CurrentUserEmail =>
        User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;

    /// <summary>
    /// User Role from JWT Token Claims
    /// </summary>
    protected string? CurrentUserRole =>
        User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value;

    /// <summary>
    /// User Display Name from JWT Token Claims
    /// </summary>
    protected string? CurrentUserName
    {
        get
        {
            var givenName = User.FindFirst(ClaimTypes.GivenName)?.Value;
            var surname = User.FindFirst(ClaimTypes.Surname)?.Value;
            var fullName = $"{givenName} {surname}".Trim();
            if (!string.IsNullOrWhiteSpace(fullName)) return fullName;
            return User.FindFirst(ClaimTypes.Name)?.Value;
        }
    }

    protected bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    protected bool IsAdmin => User.IsInRole(AppRoles.Admin);
    protected bool IsServiceProvider => User.IsInRole(AppRoles.ServiceProvider);
    protected bool IsCustomer => User.IsInRole(AppRoles.Customer);

    /// <summary>
    /// Service Provider ID from JWT Token Claims (for logged-in Service Providers)
    /// </summary>
    protected int? CurrentProviderId
    {
        get
        {
            var providerClaim = User.FindFirst("provider_id")?.Value;
            return int.TryParse(providerClaim, out int providerId) ? providerId : null;
        }
    }

    /// <summary>
    /// <summary>
    /// Securely resolves the active Tenant ID.
    /// - Tenant Admins & Staff are strictly bound to their own Tenant ID from JWT claims.
    /// - SuperAdmins, Customers, and public requests can specify a target tenant ID (e.g., booking portal).
    /// </summary>
    protected int? ResolveTenantId(int? requestedTenantId = null)
    {
        // 1. Tenant Admin & Staff are strictly bound to their own Tenant ID from JWT
        if (CurrentTenantId.HasValue && CurrentTenantId.Value > 0 && !IsCustomer && !IsSuperAdmin)
        {
            return CurrentTenantId.Value;
        }

        // 2. SuperAdmin, Customer, or public anonymous requests honor requested tenant ID
        if (requestedTenantId.HasValue && requestedTenantId.Value > 0)
        {
            return requestedTenantId.Value;
        }

        // 3. Fallback to CurrentTenantId if present
        if (CurrentTenantId.HasValue && CurrentTenantId.Value > 0)
        {
            return CurrentTenantId.Value;
        }

        return null;
    }

    /// <summary>
    /// Resolves Customer ID for customer portal workflows
    /// </summary>
    protected int? ResolveCustomerId() => CurrentUserId;

    /// <summary>
    /// Standardized Error Response Helper: { success, message, data }
    /// </summary>
    protected IActionResult ErrorResponse(string message, int statusCode = StatusCodes.Status400BadRequest)
    {
        return StatusCode(statusCode, new { success = false, message, data = (object?)null });
    }

    /// <summary>
    /// Standardized Not Found Helper: { success, message, data }
    /// </summary>
    protected NotFoundObjectResult NotFoundResponse(string message = "The requested resource was not found.")
    {
        return NotFound(new { success = false, message, data = (object?)null });
    }

    /// <summary>
    /// Standardized Forbidden Access Helper: { success, message, data }
    /// </summary>
    protected ObjectResult ForbiddenResponse(string message = "You do not have permission to access or modify this resource.")
    {
        return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message, data = (object?)null });
    }

    /// <summary>
    /// Standardized Unauthorized Access Helper: { success, message, data }
    /// </summary>
    protected ObjectResult UnauthorizedResponse(string message = "Authenticated session is required.")
    {
        return StatusCode(StatusCodes.Status401Unauthorized, new { success = false, message, data = (object?)null });
    }

    /// <summary>
    /// Standardized Conflict (409) Helper: { success, message, data }
    /// </summary>
    protected ObjectResult ConflictResponse(string message = "A conflict occurred with the requested resource.")
    {
        return StatusCode(StatusCodes.Status409Conflict, new { success = false, message, data = (object?)null });
    }

    /// <summary>
    /// Standardized Success Response Helper: { success, message, data }
    /// </summary>
    protected OkObjectResult OkResponse<T>(T? data, string message = "Success")
    {
        return Ok(new { success = true, message, data });
    }

    /// <summary>
    /// Standardized Created (201) Response Helper: { success, message, data }
    /// </summary>
    protected CreatedAtActionResult CreatedResponse<T>(string actionName, object routeValues, T? data, string message = "Created successfully.")
    {
        return CreatedAtAction(actionName, routeValues, new { success = true, message, data });
    }

    /// <summary>
    /// Standardized Validation Error Helper: { success, message, data }
    /// </summary>
    protected IActionResult ValidationResponse()
    {
        var errors = ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();

        var message = errors.Count > 0 
            ? string.Join("; ", errors) 
            : "Validation failed.";

        return StatusCode(StatusCodes.Status400BadRequest, new 
        { 
            success = false, 
            message, 
            data = (object?)null 
        });
    }
}
