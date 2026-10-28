using AppointmentBooking.BusinessLayer.Common.Models;
using AppointmentBooking.BusinessLayer.Features.Tenants.DTOs;
using AppointmentBooking.DomainLayer.Enums;

namespace AppointmentBooking.BusinessLayer.Features.Tenants;

public interface ITenantService
{
    Task<IReadOnlyList<TenantDto>> GetAllTenantsAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<TenantDto>> GetTenantsPagedAsync(PaginationParams paginationParams, CancellationToken cancellationToken = default);
    Task<TenantDto?> GetTenantByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantDto>> CreateTenantAsync(CreateTenantRequestDto request, string createdBy, CancellationToken cancellationToken = default);
    Task<TenantDto?> UpdateTenantAsync(int id, UpdateTenantRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> UpdateTenantStatusAsync(int id, TenantStatus status, CancellationToken cancellationToken = default);
}
