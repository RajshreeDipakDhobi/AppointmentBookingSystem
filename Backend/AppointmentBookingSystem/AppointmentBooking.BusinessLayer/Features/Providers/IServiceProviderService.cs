using AppointmentBooking.BusinessLayer.Common.Models;
using AppointmentBooking.BusinessLayer.Features.Providers.DTOs;
using AppointmentBooking.DomainLayer.Enums;

namespace AppointmentBooking.BusinessLayer.Features.Providers;

public interface IServiceProviderService
{
    Task<IReadOnlyList<ServiceProviderDto>> GetProvidersByTenantAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<PagedResult<ServiceProviderDto>> GetProvidersPagedAsync(int tenantId, PaginationParams paginationParams, ProviderStatus? status = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceProviderDto>> GetActiveProvidersAsync(int? tenantId = null, CancellationToken cancellationToken = default);
    Task<ServiceProviderDto?> GetProviderByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default);
    Task<ApiResponse<ServiceProviderDto>> CreateProviderAsync(int tenantId, CreateServiceProviderRequestDto request, string createdBy, CancellationToken cancellationToken = default);
    Task<ServiceProviderDto?> UpdateProviderAsync(int id, int tenantId, UpdateServiceProviderRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> UpdateProviderStatusAsync(int id, int tenantId, ProviderStatus status, CancellationToken cancellationToken = default);
    Task<bool> DeleteProviderAsync(int id, int tenantId, CancellationToken cancellationToken = default);
}
