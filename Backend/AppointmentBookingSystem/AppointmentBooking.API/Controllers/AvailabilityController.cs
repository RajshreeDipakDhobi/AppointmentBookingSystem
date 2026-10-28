using AppointmentBooking.BusinessLayer.Features.Availability;
using AppointmentBooking.BusinessLayer.Features.Availability.DTOs;
using AppointmentBooking.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppointmentBooking.API.Controllers;

[Route("api/[controller]")]
[Authorize(Roles = AppRoles.AdminOrSuperAdmin)]
public class AvailabilityController : BaseApiController
{
    private readonly IAvailabilityService _availabilityService;
    private readonly ILogger<AvailabilityController> _logger;

    public AvailabilityController(IAvailabilityService availabilityService, ILogger<AvailabilityController> logger)
    {
        _availabilityService = availabilityService;
        _logger = logger;
    }

    /// <summary>
    /// Get availability overview and weekly schedule summary for all providers in the organization
    /// </summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(ProviderAvailabilityOverviewResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAvailabilityOverview(
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
            var overview = await _availabilityService.GetAvailabilityOverviewAsync(activeTenantId.Value, cancellationToken);
            return OkResponse(overview);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving availability overview.");
            return ErrorResponse(ex.Message);
        }
    }

    /// <summary>
    /// Get weekly availability schedule for a service provider
    /// </summary>
    [HttpGet("providers/{providerId:int}")]
    [ProducesResponseType(typeof(IReadOnlyList<ProviderAvailabilityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWeeklyAvailability(
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
            var schedule = await _availabilityService.GetProviderWeeklyAvailabilityAsync(providerId, activeTenantId.Value, cancellationToken);
            if (schedule == null)
            {
                return NotFoundResponse($"Service provider with ID {providerId} was not found.");
            }
            return OkResponse(schedule);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving weekly availability for provider {ProviderId}", providerId);
            return ErrorResponse(ex.Message);
        }
    }

    /// <summary>
    /// Save or update weekly availability schedule for a service provider
    /// </summary>
    [HttpPost("providers/{providerId:int}")]
    [HttpPut("providers/{providerId:int}")]
    [ProducesResponseType(typeof(IReadOnlyList<ProviderAvailabilityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveWeeklyAvailability(
        int providerId, 
        [FromBody] SaveWeeklyAvailabilityRequestDto request, 
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

        request.ServiceProviderId = providerId;
        var userEmail = CurrentUserEmail ?? "system";

        try
        {
            var updated = await _availabilityService.SaveWeeklyAvailabilityAsync(
                activeTenantId.Value, 
                request, 
                userEmail, 
                cancellationToken);

            if (updated == null)
            {
                return NotFoundResponse($"Service provider with ID {providerId} was not found.");
            }

            return OkResponse(updated, "Weekly availability schedule saved successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving weekly availability for provider {ProviderId}", providerId);
            return ErrorResponse(ex.Message);
        }
    }

    /// <summary>
    /// Get all specific date configurations / blocks for a provider
    /// </summary>
    [HttpGet("providers/{providerId:int}/specific-dates")]
    [ProducesResponseType(typeof(IReadOnlyList<ProviderAvailabilityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSpecificDates(
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
            var dates = await _availabilityService.GetProviderSpecificDatesAsync(providerId, activeTenantId.Value, cancellationToken);
            if (dates == null)
            {
                return NotFoundResponse($"Service provider with ID {providerId} was not found.");
            }
            return OkResponse(dates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving specific dates for provider {ProviderId}", providerId);
            return ErrorResponse(ex.Message);
        }
    }

    /// <summary>
    /// Get provider availability and calculated slots for a specific calendar date (checking date-override then fallback to weekly)
    /// </summary>
    [HttpGet("providers/{providerId:int}/date-schedule")]
    [ProducesResponseType(typeof(ProviderAvailabilityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailabilityForDate(
        int providerId, 
        [FromQuery] DateTime date, 
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
            var schedule = await _availabilityService.GetProviderAvailabilityForDateAsync(providerId, activeTenantId.Value, date, cancellationToken);
            if (schedule == null)
            {
                return NotFoundResponse("Provider availability schedule was not found.");
            }

            return OkResponse(schedule);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving date schedule for provider {ProviderId}", providerId);
            return ErrorResponse(ex.Message);
        }
    }

    /// <summary>
    /// Save date-specific availability schedule or full-day block for a provider
    /// </summary>
    [HttpPost("providers/{providerId:int}/date-schedule")]
    [ProducesResponseType(typeof(ProviderAvailabilityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveDateSpecificAvailability(
        int providerId, 
        [FromBody] SaveDateSpecificAvailabilityRequestDto request, 
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

        request.ServiceProviderId = providerId;
        var userEmail = CurrentUserEmail ?? "system";

        try
        {
            var result = await _availabilityService.SaveDateSpecificAvailabilityAsync(
                activeTenantId.Value, 
                request, 
                userEmail, 
                cancellationToken);

            if (result == null)
            {
                return NotFoundResponse($"Service provider with ID {providerId} was not found.");
            }

            return OkResponse(result, "Date-specific availability schedule saved successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving date specific availability for provider {ProviderId}", providerId);
            return ErrorResponse(ex.Message);
        }
    }

    /// <summary>
    /// Preview calculated time slots for a given time window, duration, break, and blocked slots
    /// </summary>
    [HttpGet("preview-slots")]
    [ProducesResponseType(typeof(IReadOnlyList<TimeSlotDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult PreviewSlots(
        [FromQuery] string startTime = "10:00", 
        [FromQuery] string endTime = "17:00", 
        [FromQuery] int slotDurationMinutes = 30,
        [FromQuery] string? breakStartTime = null,
        [FromQuery] string? breakEndTime = null,
        [FromQuery] string? blockedSlots = null)
    {
        if (!TimeSpan.TryParse(startTime, out var start) || !TimeSpan.TryParse(endTime, out var end))
        {
            return ErrorResponse("Invalid time format. Use HH:mm (e.g. 10:00).");
        }

        if (end <= start)
        {
            return ErrorResponse("End time must be after start time.");
        }

        if (slotDurationMinutes < 10 || slotDurationMinutes > 240)
        {
            return ErrorResponse("Slot duration must be between 10 and 240 minutes.");
        }

        TimeSpan? bStart = null;
        if (!string.IsNullOrWhiteSpace(breakStartTime) && TimeSpan.TryParse(breakStartTime, out var parsedBStart))
        {
            bStart = parsedBStart;
        }

        TimeSpan? bEnd = null;
        if (!string.IsNullOrWhiteSpace(breakEndTime) && TimeSpan.TryParse(breakEndTime, out var parsedBEnd))
        {
            bEnd = parsedBEnd;
        }

        var blockedList = !string.IsNullOrWhiteSpace(blockedSlots)
            ? blockedSlots.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;

        try
        {
            var slots = _availabilityService.CalculateSlots(start, end, slotDurationMinutes, bStart, bEnd, blockedList);
            return OkResponse(slots);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating preview slots.");
            return ErrorResponse(ex.Message);
        }
    }

    /// <summary>
    /// Remove availability schedule record (weekly or specific date) by Availability ID
    /// </summary>
    [HttpDelete("{availabilityId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAvailability(
        int availabilityId, 
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
            var success = await _availabilityService.RemoveAvailabilityAsync(availabilityId, activeTenantId.Value, cancellationToken);
            if (!success)
            {
                return NotFoundResponse($"Availability record with ID {availabilityId} was not found.");
            }

            return OkResponse<object?>(
                new { availabilityId }, 
                "Availability schedule removed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing availability record {AvailabilityId}", availabilityId);
            return ErrorResponse(ex.Message);
        }
    }
}
