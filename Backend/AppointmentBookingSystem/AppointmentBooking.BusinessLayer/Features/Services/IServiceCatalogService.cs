using AppointmentBooking.BusinessLayer.Common.Models;
using AppointmentBooking.BusinessLayer.Features.Services.DTOs;

namespace AppointmentBooking.BusinessLayer.Features.Services;

public interface IServiceCatalogService
{
    Task<IReadOnlyList<ServiceDto>> GetAllServicesAsync(int? tenantId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceDto>> GetActiveServicesAsync(int? tenantId = null, CancellationToken cancellationToken = default);
    Task<ServiceDto?> GetServiceByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ApiResponse<ServiceDto>> CreateServiceAsync(int userId, CreateServiceRequestDto request, string createdBy, CancellationToken cancellationToken = default);
    Task<ServiceDto?> UpdateServiceAsync(int id, UpdateServiceRequestDto request, string updatedBy, CancellationToken cancellationToken = default);
    Task<bool> ToggleServiceStatusAsync(int id, string updatedBy, CancellationToken cancellationToken = default);
    Task<bool> DeleteServiceAsync(int id, string deletedBy, CancellationToken cancellationToken = default);
}

