using AppointmentBooking.BusinessLayer.Features.Calendar;
using AppointmentBooking.BusinessLayer.Features.Calendar.DTOs;
using AppointmentBooking.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppointmentBooking.API.Controllers;

[Route("api/[controller]")]
[Authorize(Roles = AppRoles.AllStaff)]
public class CalendarController : BaseApiController
{
    private readonly ICalendarService _calendarService;
    private readonly ILogger<CalendarController> _logger;

    public CalendarController(ICalendarService calendarService, ILogger<CalendarController> logger)
    {
        _calendarService = calendarService;
        _logger = logger;
    }

    /// <summary>
    /// Independent Calendar Schedule Feed:
    /// Returns unified timetable, provider working hours/breaks, appointments, and role permissions in a single round-trip.
    /// </summary>
    [HttpGet("schedule")]
    [ProducesResponseType(typeof(CalendarScheduleResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCalendarSchedule(
        [FromQuery] CalendarFilterDto filter,
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
            var schedule = await _calendarService.GetCalendarScheduleAsync(
                activeTenantId.Value,
                filter,
                CurrentUserId,
                CurrentUserRole,
                CurrentProviderId,
                cancellationToken);

            return OkResponse(schedule);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving calendar schedule for tenant {TenantId}", activeTenantId);
            return ErrorResponse(ex.Message);
        }
    }
}

