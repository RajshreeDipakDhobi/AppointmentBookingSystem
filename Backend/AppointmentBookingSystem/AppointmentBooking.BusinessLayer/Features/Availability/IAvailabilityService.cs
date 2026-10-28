using AppointmentBooking.BusinessLayer.Features.Availability.DTOs;

namespace AppointmentBooking.BusinessLayer.Features.Availability;

public interface IAvailabilityService
{
    Task<ProviderAvailabilityOverviewResponseDto> GetAvailabilityOverviewAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderAvailabilityDto>?> GetProviderWeeklyAvailabilityAsync(int providerId, int tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderAvailabilityDto>?> SaveWeeklyAvailabilityAsync(int tenantId, SaveWeeklyAvailabilityRequestDto request, string userEmail, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderAvailabilityDto>?> GetProviderSpecificDatesAsync(int providerId, int tenantId, CancellationToken cancellationToken = default);
    Task<ProviderAvailabilityDto?> SaveDateSpecificAvailabilityAsync(int tenantId, SaveDateSpecificAvailabilityRequestDto request, string userEmail, CancellationToken cancellationToken = default);
    Task<ProviderAvailabilityDto?> GetProviderAvailabilityForDateAsync(int providerId, int tenantId, DateTime date, CancellationToken cancellationToken = default);
    IReadOnlyList<TimeSlotDto> CalculateSlots(TimeSpan startTime, TimeSpan endTime, int slotDurationMinutes, TimeSpan? breakStartTime = null, TimeSpan? breakEndTime = null, IEnumerable<string>? blockedSlots = null);
    Task<bool> RemoveAvailabilityAsync(int availabilityId, int tenantId, CancellationToken cancellationToken = default);
}
