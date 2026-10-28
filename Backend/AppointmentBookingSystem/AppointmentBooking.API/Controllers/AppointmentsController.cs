using AppointmentBooking.BusinessLayer.Common.Models;
using AppointmentBooking.BusinessLayer.Features.Appointments;
using AppointmentBooking.BusinessLayer.Features.Appointments.DTOs;
using AppointmentBooking.DomainLayer.Constants;
using AppointmentBooking.DomainLayer.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppointmentBooking.API.Controllers;

[Route("api/[controller]")]
public class AppointmentsController : BaseApiController
{
    private readonly IAppointmentService _appointmentService;
    private readonly ILogger<AppointmentsController> _logger;

    public AppointmentsController(IAppointmentService appointmentService, ILogger<AppointmentsController> logger)
    {
        _appointmentService = appointmentService;
        _logger = logger;
    }

    /// <summary>
    /// Module 3: View Available Time Slots for a Provider on a Specific Date (Public)
    /// </summary>
    [HttpGet("available-slots")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProviderDailyAvailabilityResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailableSlots(
        [FromQuery] int providerId, 
        [FromQuery] DateTime date)
    {
        if (providerId <= 0)
        {
            return ErrorResponse("Valid Service Provider ID must be specified.");
        }

        try
        {
            var result = await _appointmentService.GetAvailableSlotsAsync(providerId, date);
            if (result == null)
            {
                return NotFoundResponse($"Service provider with ID {providerId} was not found.");
            }

            return OkResponse(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available slots for provider ID {ProviderId}", providerId);
            return ErrorResponse(ex.Message);
        }
    }

    /// <summary>
    /// Module 4: Book an Appointment (Supports Logged-in Customer & Admin Walk-in Booking)
    /// </summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<AppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BookAppointment(
        [FromBody] BookAppointmentRequestDto request, 
        [FromQuery] int? tenantId)
    {
        if (!ModelState.IsValid)
        {
            return ValidationResponse();
        }

        var activeTenantId = ResolveTenantId(tenantId);
        // If Admin, SuperAdmin, or ServiceProvider is booking, they are booking on behalf of a customer, so customerId is null
        var customerId = (IsAdmin || IsSuperAdmin || IsServiceProvider) ? null : ResolveCustomerId();

        var result = await _appointmentService.BookAppointmentAsync(activeTenantId, customerId, request);

        if (!result.Success)
        {
            int statusCode = (result.Message.Contains("already been booked", StringComparison.OrdinalIgnoreCase) ||
                              result.Message.Contains("slot is already booked", StringComparison.OrdinalIgnoreCase) ||
                              result.Message.Contains("different slot", StringComparison.OrdinalIgnoreCase) ||
                              result.Message.Contains("already booked", StringComparison.OrdinalIgnoreCase))
                             ? StatusCodes.Status409Conflict 
                             : StatusCodes.Status400BadRequest;

            return StatusCode(statusCode, result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Admin & Service Provider: Lookup existing customer by phone number or email for booking
    /// </summary>
    [HttpGet("lookup-customer")]
    [Authorize]
    [ProducesResponseType(typeof(AppointmentBooking.BusinessLayer.Features.Auth.DTOs.UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LookupCustomer(
        [FromQuery] string query, 
        [FromQuery] int? tenantId)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ErrorResponse("Query parameter is required.");
        }

        var activeTenantId = ResolveTenantId(tenantId);
        var customer = await _appointmentService.LookupCustomerAsync(activeTenantId, query);
        if (customer == null)
        {
            return NotFoundResponse("Customer not found.");
        }

        return OkResponse(customer);
    }

    /// <summary>
    /// Admin & Service Provider: Live multi-match customer search by Name, Phone, or Email
    /// </summary>
    [HttpGet("search-customers")]
    [Authorize]
    [ProducesResponseType(typeof(List<AppointmentBooking.BusinessLayer.Features.Auth.DTOs.UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchCustomers(
        [FromQuery] string query, 
        [FromQuery] int? tenantId)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return OkResponse(new List<AppointmentBooking.BusinessLayer.Features.Auth.DTOs.UserDto>());
        }

        var activeTenantId = ResolveTenantId(tenantId);
        var customers = await _appointmentService.SearchCustomersAsync(activeTenantId, query);
        return OkResponse(customers);
    }


    /// <summary>
    /// Customer Portal: Get all appointments for the logged-in customer with server-side pagination
    /// </summary>
    [HttpGet("my-appointments")]
    [Authorize]
    [ProducesResponseType(typeof(PagedResult<AppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyAppointments(
        [FromQuery] PaginationParams paginationParams,
        [FromQuery] AppointmentStatus? status)
    {
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return UnauthorizedResponse("Authenticated customer session is required.");
        }

        var result = await _appointmentService.GetMyAppointmentsPagedAsync(customerId.Value, paginationParams, status);
        return OkResponse(result);
    }

    /// <summary>
    /// Customer Portal: Get aggregate appointment statistics for logged-in customer
    /// </summary>
    [HttpGet("my-stats")]
    [Authorize]
    [ProducesResponseType(typeof(AppointmentStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyStats()
    {
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return UnauthorizedResponse("Authenticated customer session is required.");
        }

        var stats = await _appointmentService.GetCustomerStatsAsync(customerId.Value);
        return OkResponse(stats);
    }

    /// <summary>
    /// Module 7: Get All Appointments with Filters and Server-side Pagination (Staff, Admin, SuperAdmin)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = AppRoles.AllStaff)]
    [ProducesResponseType(typeof(PagedResult<AppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAppointments(
        [FromQuery] int? providerId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] AppointmentStatus? status,
        [FromQuery] PaginationParams paginationParams,
        [FromQuery] int? tenantId)
    {
        var activeTenantId = ResolveTenantId(tenantId);
        if (!activeTenantId.HasValue)
        {
            return ErrorResponse("Tenant context is required.");
        }

        // Service providers are strictly scoped to their own appointments
        var targetProviderId = IsServiceProvider ? CurrentProviderId : providerId;

        var result = await _appointmentService.GetAppointmentsPagedAsync(
            activeTenantId.Value, 
            targetProviderId, 
            startDate, 
            endDate, 
            status, 
            paginationParams);

        return OkResponse(result);
    }

    /// <summary>
    /// Get single appointment details by Appointment ID
    /// </summary>
    [HttpGet("{appointmentId:int}")]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAppointmentById(
        int appointmentId, 
        [FromQuery] int? tenantId)
    {
        var activeTenantId = ResolveTenantId(tenantId);
        if (!activeTenantId.HasValue)
        {
            return ErrorResponse("Tenant context is required.");
        }

        var appointment = await _appointmentService.GetAppointmentByIdAsync(appointmentId, activeTenantId.Value);
        if (appointment == null)
        {
            return NotFoundResponse($"Appointment with ID {appointmentId} was not found.");
        }

        return OkResponse(appointment);
    }

    /// <summary>
    /// Module 5: Reschedule an Appointment (Supports Customer & Admin)
    /// </summary>
    [HttpPost("{appointmentId:int}/reschedule")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RescheduleAppointment(
        int appointmentId, 
        [FromBody] RescheduleAppointmentRequestDto request, 
        [FromQuery] int? tenantId)
    {
        if (!ModelState.IsValid)
        {
            return ValidationResponse();
        }

        // Tenant Admin cannot directly reschedule
        if (IsAdmin && !IsSuperAdmin)
        {
            return ForbiddenResponse("Tenant Admins cannot directly reschedule appointments. Direct rescheduling is reserved for customers, while service providers can propose a new time.");
        }

        var activeTenantId = ResolveTenantId(tenantId);
        var customerId = ResolveCustomerId();
        bool isAdmin = IsAdmin || IsSuperAdmin;

        var result = await _appointmentService.RescheduleAppointmentAsync(
            appointmentId, 
            activeTenantId, 
            customerId, 
            isAdmin, 
            request);

        if (!result.Success)
        {
            int statusCode = (result.Message.Contains("already booked", StringComparison.OrdinalIgnoreCase) ||
                              result.Message.Contains("different slot", StringComparison.OrdinalIgnoreCase))
                             ? StatusCodes.Status409Conflict 
                             : StatusCodes.Status400BadRequest;

            return StatusCode(statusCode, result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Module 5: Cancel an Appointment (Supports Customer & Admin)
    /// </summary>
    [HttpPost("{appointmentId:int}/cancel")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CancelAppointment(
        int appointmentId, 
        [FromBody] CancelAppointmentRequestDto request, 
        [FromQuery] int? tenantId)
    {
        if (!ModelState.IsValid)
        {
            return ValidationResponse();
        }

        var activeTenantId = ResolveTenantId(tenantId);
        var customerId = ResolveCustomerId();
        bool isAdmin = IsAdmin || IsSuperAdmin;

        var result = await _appointmentService.CancelAppointmentAsync(
            appointmentId, 
            activeTenantId, 
            customerId, 
            isAdmin, 
            request,
            CurrentUserName);

        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status400BadRequest, result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Update appointment lifecycle status (Approve, Reject, Complete, Cancel)
    /// Unified single endpoint for Service Providers, Admins and SuperAdmins
    /// </summary>
    [HttpPatch("{appointmentId:int}/status")]
    [Authorize(Roles = AppRoles.AllStaff)]
    [ProducesResponseType(typeof(ApiResponse<AppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateStatus(
        int appointmentId, 
        [FromBody] UpdateAppointmentStatusDto request, 
        [FromQuery] int? tenantId)
    {
        if (!ModelState.IsValid)
        {
            return ValidationResponse();
        }

        var activeTenantId = ResolveTenantId(tenantId);
        var providerId = CurrentProviderId;
        var currentUserId = CurrentUserId;
        bool isAdmin = IsAdmin || IsSuperAdmin;

        var result = await _appointmentService.UpdateStatusAsync(
            appointmentId, 
            activeTenantId, 
            providerId, 
            currentUserId, 
            isAdmin, 
            request,
            CurrentUserName);

        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status400BadRequest, result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Customer: Accept or decline a provider-initiated reschedule proposal
    /// </summary>
    [HttpPost("{appointmentId:int}/respond-proposal")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<AppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RespondToRescheduleProposal(
        int appointmentId,
        [FromQuery] bool accept)
    {
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return UnauthorizedResponse("Authenticated customer session is required.");
        }

        var result = await _appointmentService.RespondToRescheduleProposalAsync(appointmentId, customerId.Value, accept);
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status400BadRequest, result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Module 7: Get Appointment Summary Metrics for Staff (Admin & Service Provider)
    /// </summary>
    [HttpGet("stats")]
    [Authorize(Roles = AppRoles.AllStaff)]
    [ProducesResponseType(typeof(AppointmentStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStats([FromQuery] int? tenantId)
    {
        var activeTenantId = ResolveTenantId(tenantId);
        if (!activeTenantId.HasValue)
        {
            return ErrorResponse("Tenant context is required.");
        }

        var targetProviderId = IsServiceProvider ? CurrentProviderId : (int?)null;
        var stats = await _appointmentService.GetStatsAsync(activeTenantId.Value, targetProviderId);
        return OkResponse(stats);
    }
}
