using AppointmentBooking.BusinessLayer.Common.Models;
using AppointmentBooking.BusinessLayer.Features.Services.DTOs;
using AppointmentBooking.DomainLayer.Entities;
using AppointmentBooking.DomainLayer.Enums;
using AppointmentBooking.InfrastructureLayer.Repositories;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AppointmentBooking.BusinessLayer.Features.Services;

public class ServiceCatalogService : IServiceCatalogService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<ServiceCatalogService> _logger;

    public ServiceCatalogService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ILogger<ServiceCatalogService> logger)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ServiceDto>> GetAllServicesAsync(int? tenantId = null, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Service>()
            .Query()
            .AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.ServiceProviders)
            .Where(s => !s.IsDeleted);

        if (tenantId.HasValue && tenantId.Value > 0)
        {
            query = query.Where(s => s.User.TenantId == tenantId.Value);
        }

        var services = await query
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return _mapper.Map<IReadOnlyList<ServiceDto>>(services);
    }

    public async Task<IReadOnlyList<ServiceDto>> GetActiveServicesAsync(int? tenantId = null, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Service>()
            .Query()
            .AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.ServiceProviders)
            .Where(s => s.IsActive && !s.IsDeleted);

        if (tenantId.HasValue && tenantId.Value > 0)
        {
            query = query.Where(s => s.User.TenantId == tenantId.Value);
        }

        var services = await query
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return _mapper.Map<IReadOnlyList<ServiceDto>>(services);
    }

    public async Task<ServiceDto?> GetServiceByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var service = await _unitOfWork.Repository<Service>()
            .Query()
            .AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.ServiceProviders)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, cancellationToken);

        return service != null ? _mapper.Map<ServiceDto>(service) : null;
    }

    public async Task<ApiResponse<ServiceDto>> CreateServiceAsync(
        int userId, 
        CreateServiceRequestDto request, 
        string createdBy, 
        CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.Repository<User>()
            .Query()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);

        if (user == null)
        {
            return ApiResponse<ServiceDto>.Fail("Associated User does not exist.");
        }

        // Check duplicate name within the same organization
        var nameExists = await _unitOfWork.Repository<Service>()
            .Query()
            .AnyAsync(s => s.User.TenantId == user.TenantId 
                        && s.Name.ToLower() == request.Name.Trim().ToLower() 
                        && !s.IsDeleted, cancellationToken);

        if (nameExists)
        {
            return ApiResponse<ServiceDto>.Fail($"A service named '{request.Name.Trim()}' already exists in your organization.");
        }

        var service = new Service
        {
            UserId = userId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            DurationMinutes = request.DurationMinutes,
            Price = request.Price,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        await _unitOfWork.Repository<Service>().AddAsync(service, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = _mapper.Map<ServiceDto>(service);
        dto.CreatedByName = $"{user.FirstName} {user.LastName}".Trim();
        return ApiResponse<ServiceDto>.Ok(dto, "Service created successfully.");
    }

    public async Task<ServiceDto?> UpdateServiceAsync(
        int id, 
        UpdateServiceRequestDto request, 
        string updatedBy, 
        CancellationToken cancellationToken = default)
    {
        var service = await _unitOfWork.Repository<Service>()
            .Query()
            .Include(s => s.User)
            .Include(s => s.ServiceProviders)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, cancellationToken);

        if (service == null) return null;

        // Check duplicate name within the same organization
        var duplicate = await _unitOfWork.Repository<Service>()
            .Query()
            .AnyAsync(s => s.Id != id 
                        && s.User.TenantId == service.User.TenantId 
                        && s.Name.ToLower() == request.Name.Trim().ToLower() 
                        && !s.IsDeleted, cancellationToken);

        if (duplicate)
        {
            throw new InvalidOperationException($"Another service named '{request.Name.Trim()}' already exists.");
        }

        bool durationChanged = service.DurationMinutes != request.DurationMinutes;
        service.Name = request.Name.Trim();
        service.Description = request.Description?.Trim();
        service.DurationMinutes = request.DurationMinutes;
        service.Price = request.Price;
        service.IsActive = request.IsActive;
        service.UpdatedAt = DateTime.UtcNow;
        service.UpdatedBy = updatedBy;

        _unitOfWork.Repository<Service>().Update(service);

        if (durationChanged)
        {
            var providerIds = await _unitOfWork.Repository<ServiceProvider>()
                .Query()
                .Where(sp => sp.ServiceId == id && !sp.IsDeleted)
                .Select(sp => sp.Id)
                .ToListAsync(cancellationToken);

            if (providerIds.Count > 0)
            {
                var availabilities = await _unitOfWork.Repository<ProviderAvailability>()
                    .Query()
                    .Where(pa => providerIds.Contains(pa.ServiceProviderId))
                    .ToListAsync(cancellationToken);

                foreach (var pa in availabilities)
                {
                    pa.SlotDurationMinutes = request.DurationMinutes;
                    pa.UpdatedAt = DateTime.UtcNow;
                    _unitOfWork.Repository<ProviderAvailability>().Update(pa);
                }
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<ServiceDto>(service);
    }

    public async Task<bool> ToggleServiceStatusAsync(int id, string updatedBy, CancellationToken cancellationToken = default)
    {
        var service = await _unitOfWork.Repository<Service>()
            .Query()
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, cancellationToken);

        if (service == null) return false;

        service.IsActive = !service.IsActive;
        service.UpdatedAt = DateTime.UtcNow;
        service.UpdatedBy = updatedBy;

        _unitOfWork.Repository<Service>().Update(service);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteServiceAsync(int id, string deletedBy, CancellationToken cancellationToken = default)
    {
        var service = await _unitOfWork.Repository<Service>()
            .Query()
            .Include(s => s.ServiceProviders)
            .Include(s => s.Appointments)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, cancellationToken);

        if (service == null) return false;

        // Check if active appointments exist
        var hasActiveAppointments = service.Appointments.Any(a => !a.IsDeleted && 
            (a.Status == AppointmentStatus.Booked || 
             a.Status == AppointmentStatus.Approved || 
             a.Status == AppointmentStatus.RescheduleProposed || 
             a.Status == AppointmentStatus.Rescheduled));

        if (hasActiveAppointments)
        {
            throw new InvalidOperationException("Cannot delete service with active appointments. Deactivate it instead.");
        }

        service.IsDeleted = true;
        service.DeletedAt = DateTime.UtcNow;
        service.DeletedBy = deletedBy;

        _unitOfWork.Repository<Service>().Update(service);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
