using AppointmentBooking.BusinessLayer.Features.Calendar.DTOs;

namespace AppointmentBooking.BusinessLayer.Features.Calendar;

public interface ICalendarService
{
    Task<CalendarScheduleResponseDto> GetCalendarScheduleAsync(
        int tenantId,
        CalendarFilterDto filter,
        int? currentUserId,
        string? currentUserRole,
        int? currentProviderId,
        CancellationToken cancellationToken = default);
}

