using AppointmentBooking.BusinessLayer.Common.Interfaces;
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
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<ServiceCatalogService> _logger;

    public ServiceCatalogService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        IDateTimeProvider dateTimeProvider,
        ILogger<ServiceCatalogService> logger)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ServiceDto>> GetAllServicesAsync(int? tenantId = null, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Service>()
            .Query()
            .AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.ServiceProviders)
                .ThenInclude(sp => sp.User)
            .Include(s => s.ProviderServiceLinks)
                .ThenInclude(l => l.ServiceProvider)
                    .ThenInclude(sp => sp.User)
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
                .ThenInclude(sp => sp.User)
            .Include(s => s.ProviderServiceLinks)
                .ThenInclude(l => l.ServiceProvider)
                    .ThenInclude(sp => sp.User)
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
            .Include(s => s.ProviderServiceLinks)
                .ThenInclude(l => l.ServiceProvider)
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

        // Link selected providers to this newly created service
        if (request.ProviderIds != null && request.ProviderIds.Count > 0)
        {
            var targetProviderIds = request.ProviderIds.Distinct().ToList();
            var providers = await _unitOfWork.Repository<ServiceProvider>()
                .Query()
                .Where(sp => targetProviderIds.Contains(sp.Id) && sp.TenantId == user.TenantId && !sp.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var p in providers)
            {
                if (!p.ServiceId.HasValue)
                {
                    p.ServiceId = service.Id;
                    p.UpdatedAt = DateTime.UtcNow;
                    p.UpdatedBy = createdBy;
                    _unitOfWork.Repository<ServiceProvider>().Update(p);
                }

                await _unitOfWork.Repository<ProviderServiceLink>().AddAsync(new ProviderServiceLink
                {
                    ServiceProviderId = p.Id,
                    ServiceId = service.Id,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                }, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var createdService = await _unitOfWork.Repository<Service>()
            .Query()
            .AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.ServiceProviders)
            .Include(s => s.ProviderServiceLinks)
                .ThenInclude(l => l.ServiceProvider)
            .FirstOrDefaultAsync(s => s.Id == service.Id && !s.IsDeleted, cancellationToken);

        var dto = _mapper.Map<ServiceDto>(createdService ?? service);
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

        // Update assigned providers if ProviderIds is supplied
        if (request.ProviderIds != null)
        {
            var targetProviderIds = request.ProviderIds.Distinct().ToList();

            // Synchronize ProviderServiceLinks (Many-to-Many join table)
            var existingLinks = await _unitOfWork.Repository<ProviderServiceLink>()
                .Query()
                .Where(l => l.ServiceId == service.Id)
                .ToListAsync(cancellationToken);

            foreach (var link in existingLinks.Where(l => !targetProviderIds.Contains(l.ServiceProviderId)))
            {
                _unitOfWork.Repository<ProviderServiceLink>().Remove(link);
            }

            var activeExistingProviderIds = existingLinks.Where(l => !l.IsDeleted).Select(l => l.ServiceProviderId).ToList();
            foreach (var pId in targetProviderIds.Where(id => !activeExistingProviderIds.Contains(id)))
            {
                await _unitOfWork.Repository<ProviderServiceLink>().AddAsync(new ProviderServiceLink
                {
                    ServiceProviderId = pId,
                    ServiceId = service.Id,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = updatedBy
                }, cancellationToken);
            }

            // Sync legacy ServiceProvider.ServiceId for backward compatibility
            var currentAssignedProviders = await _unitOfWork.Repository<ServiceProvider>()
                .Query()
                .Where(sp => sp.ServiceId == id && !sp.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var p in currentAssignedProviders.Where(cp => !targetProviderIds.Contains(cp.Id)))
            {
                p.ServiceId = null;
                p.UpdatedAt = DateTime.UtcNow;
                p.UpdatedBy = updatedBy;
                _unitOfWork.Repository<ServiceProvider>().Update(p);
            }

            var toAssign = await _unitOfWork.Repository<ServiceProvider>()
                .Query()
                .Where(sp => targetProviderIds.Contains(sp.Id) && sp.TenantId == service.User.TenantId && !sp.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var p in toAssign.Where(p => !p.ServiceId.HasValue))
            {
                p.ServiceId = service.Id;
                p.UpdatedAt = DateTime.UtcNow;
                p.UpdatedBy = updatedBy;
                _unitOfWork.Repository<ServiceProvider>().Update(p);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var updatedService = await _unitOfWork.Repository<Service>()
            .Query()
            .AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.ServiceProviders)
                .ThenInclude(sp => sp.User)
            .Include(s => s.ProviderServiceLinks)
                .ThenInclude(l => l.ServiceProvider)
                    .ThenInclude(sp => sp.User)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, cancellationToken);

        return _mapper.Map<ServiceDto>(updatedService ?? service);
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

        // 1. Cascade soft-delete on ProviderServiceLinks
        var links = await _unitOfWork.Repository<ProviderServiceLink>()
            .Query()
            .Where(l => l.ServiceId == id && !l.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var link in links)
        {
            _unitOfWork.Repository<ProviderServiceLink>().Remove(link);
        }

        // 2. Unlink primary ServiceId from any providers still pointing to this service
        var linkedProviders = await _unitOfWork.Repository<ServiceProvider>()
            .Query()
            .Where(sp => sp.ServiceId == id && !sp.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var p in linkedProviders)
        {
            p.ServiceId = null;
            p.UpdatedAt = DateTime.UtcNow;
            p.UpdatedBy = deletedBy;
            _unitOfWork.Repository<ServiceProvider>().Update(p);
        }

        service.IsDeleted = true;
        service.DeletedAt = DateTime.UtcNow;
        service.DeletedBy = deletedBy;

        _unitOfWork.Repository<Service>().Update(service);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ProviderServicesOverviewDto?> GetProviderServicesOverviewAsync(
        int userId, 
        int? targetProviderId = null, 
        CancellationToken cancellationToken = default)
    {
        // 1. Identify the provider
        var providersQuery = _unitOfWork.Repository<ServiceProvider>()
            .Query()
            .Include(p => p.User)
            .Include(p => p.Service)
            .Include(p => p.ProviderServiceLinks)
                .ThenInclude(l => l.Service);

        ServiceProvider? provider;
        if (targetProviderId.HasValue && targetProviderId.Value > 0)
        {
            provider = await providersQuery.FirstOrDefaultAsync(p => p.Id == targetProviderId.Value && !p.IsDeleted, cancellationToken);
        }
        else
        {
            provider = await providersQuery.FirstOrDefaultAsync(p => p.UserId == userId && !p.IsDeleted, cancellationToken);
        }

        if (provider == null)
        {
            return null;
        }

        var providerName = provider.User != null ? $"{provider.User.FirstName} {provider.User.LastName}".Trim() : "Specialist";

        // 2. Fetch all assigned services for this provider
        var assignedServicesMap = new Dictionary<int, Service>();
        if (provider.Service != null && !provider.Service.IsDeleted)
        {
            assignedServicesMap[provider.Service.Id] = provider.Service;
        }

        if (provider.ProviderServiceLinks != null)
        {
            foreach (var link in provider.ProviderServiceLinks.Where(l => !l.IsDeleted && l.Service != null && !l.Service.IsDeleted))
            {
                if (!assignedServicesMap.ContainsKey(link.ServiceId))
                {
                    assignedServicesMap[link.ServiceId] = link.Service!;
                }
            }
        }

        // 3. Today's date and shift timing
        var now = _dateTimeProvider.GetCurrentLocalTime();
        var today = now.Date;
        var currentTime = now.TimeOfDay;

        // Get availability for today (date-specific override first, then dayOfWeek)
        var specificAvail = await _unitOfWork.Repository<ProviderAvailability>().Query()
            .FirstOrDefaultAsync(pa => pa.ServiceProviderId == provider.Id 
                                    && pa.SpecificDate.HasValue 
                                    && pa.SpecificDate.Value.Date == today, cancellationToken);

        var weeklyAvail = specificAvail != null ? null : await _unitOfWork.Repository<ProviderAvailability>().Query()
            .FirstOrDefaultAsync(pa => pa.ServiceProviderId == provider.Id 
                                    && pa.SpecificDate == null 
                                    && pa.DayOfWeek == today.DayOfWeek, cancellationToken);

        var activeAvail = specificAvail ?? weeklyAvail;
        bool isWorkingToday = activeAvail != null && activeAvail.IsAvailable;
        string workingHours = isWorkingToday 
            ? $"{Format12Hour(activeAvail!.StartTime)} – {Format12Hour(activeAvail.EndTime)}" 
            : "Off Today";

        string? breakHours = (isWorkingToday && activeAvail!.BreakStartTime.HasValue && activeAvail.BreakEndTime.HasValue)
            ? $"{Format12Hour(activeAvail.BreakStartTime.Value)} – {Format12Hour(activeAvail.BreakEndTime.Value)}"
            : null;

        // 4. Fetch today's appointments for this provider
        var todayAppointments = await _unitOfWork.Repository<Appointment>().Query().AsNoTracking()
            .Include(a => a.Customer)
            .Include(a => a.Service)
            .Where(a => a.ServiceProviderId == provider.Id 
                     && a.AppointmentDate.Date == today 
                     && !a.IsDeleted 
                     && a.Status != AppointmentStatus.Cancelled)
            .OrderBy(a => a.StartTime)
            .ToListAsync(cancellationToken);

        // All-time bookings count per service
        var allTimeServiceCounts = await _unitOfWork.Repository<Appointment>().Query().AsNoTracking()
            .Where(a => a.ServiceProviderId == provider.Id && !a.IsDeleted && a.Status == AppointmentStatus.Completed && a.ServiceId.HasValue)
            .GroupBy(a => a.ServiceId!.Value)
            .Select(g => new { ServiceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ServiceId, x => x.Count, cancellationToken);

        // Build today's timeline
        var timeline = new List<ProviderTodayBookingDto>();
        ProviderTodayBookingDto? currentOngoing = null;
        ProviderTodayBookingDto? nextUpcoming = null;

        foreach (var apt in todayAppointments)
        {
            bool isOngoing = currentTime >= apt.StartTime && currentTime < apt.EndTime;
            bool isUpcoming = currentTime < apt.StartTime;
            bool isCompleted = currentTime >= apt.EndTime || apt.Status == AppointmentStatus.Completed;

            string progressNote;
            if (isOngoing)
            {
                var remaining = (int)(apt.EndTime - currentTime).TotalMinutes;
                progressNote = $"In progress • {remaining}m remaining";
            }
            else if (isUpcoming)
            {
                var startsIn = (int)(apt.StartTime - currentTime).TotalMinutes;
                if (startsIn < 60)
                {
                    progressNote = $"Starts in {startsIn}m";
                }
                else
                {
                    progressNote = $"Starts in {startsIn / 60}h {startsIn % 60}m";
                }
            }
            else
            {
                progressNote = "Session finished";
            }

            var item = new ProviderTodayBookingDto
            {
                AppointmentId = apt.Id,
                AppointmentNumber = apt.AppointmentNumber,
                CustomerName = apt.Customer != null ? $"{apt.Customer.FirstName} {apt.Customer.LastName}".Trim() : "Client",
                CustomerPhone = apt.Customer?.PhoneNumber,
                ServiceId = apt.ServiceId ?? 0,
                ServiceName = apt.Service?.Name ?? "Service",
                StartTime = Format12Hour(apt.StartTime),
                EndTime = Format12Hour(apt.EndTime),
                TimeRange = $"{Format12Hour(apt.StartTime)} – {Format12Hour(apt.EndTime)}",
                DurationMinutes = (int)(apt.EndTime - apt.StartTime).TotalMinutes,
                Status = apt.Status.ToString(),
                IsOngoingNow = isOngoing,
                IsUpcoming = isUpcoming,
                IsCompleted = isCompleted,
                ProgressOrRemainingNote = progressNote
            };

            timeline.Add(item);

            if (isOngoing && currentOngoing == null)
            {
                currentOngoing = item;
            }
            if (isUpcoming && nextUpcoming == null)
            {
                nextUpcoming = item;
            }
        }

        // Determine live current work status
        string currentStatus = "Available";
        string statusNote = "Ready for appointments";

        if (!isWorkingToday)
        {
            currentStatus = "OffToday";
            statusNote = "Scheduled day off";
        }
        else if (currentTime < activeAvail!.StartTime)
        {
            currentStatus = "NotStarted";
            statusNote = $"Shift begins at {Format12Hour(activeAvail.StartTime)}";
        }
        else if (currentTime >= activeAvail.EndTime)
        {
            currentStatus = "ShiftEnded";
            statusNote = $"Shift concluded at {Format12Hour(activeAvail.EndTime)}";
        }
        else if (activeAvail.BreakStartTime.HasValue && activeAvail.BreakEndTime.HasValue 
              && currentTime >= activeAvail.BreakStartTime.Value && currentTime < activeAvail.BreakEndTime.Value)
        {
            currentStatus = "OnBreak";
            statusNote = $"On scheduled break until {Format12Hour(activeAvail.BreakEndTime.Value)}";
        }
        else if (currentOngoing != null)
        {
            currentStatus = "Busy";
            statusNote = $"Currently serving {currentOngoing.CustomerName} for {currentOngoing.ServiceName} (until {currentOngoing.EndTime})";
        }
        else if (nextUpcoming != null)
        {
            currentStatus = "Available";
            statusNote = $"Free right now • Next appointment with {nextUpcoming.CustomerName} at {nextUpcoming.StartTime}";
        }
        else
        {
            currentStatus = "Available";
            statusNote = "No more appointments scheduled for today";
        }

        // Build assigned services list
        var serviceItems = assignedServicesMap.Values.Select(s =>
        {
            int todayCount = todayAppointments.Count(a => a.ServiceId == s.Id);
            int allTimeCount = allTimeServiceCounts.TryGetValue(s.Id, out var c) ? c : 0;

            return new ProviderAssignedServiceItemDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                DurationMinutes = s.DurationMinutes,
                Price = s.Price,
                IsActive = s.IsActive,
                TodayBookingsCount = todayCount,
                AllTimeBookingsCount = allTimeCount
            };
        }).OrderBy(s => s.Name).ToList();

        return new ProviderServicesOverviewDto
        {
            ProviderId = provider.Id,
            ProviderName = providerName,
            Email = provider.User?.Email ?? string.Empty,
            PhoneNumber = provider.User?.PhoneNumber ?? string.Empty,
            ProfilePictureUrl = provider.User?.ProfilePictureUrl,
            Specialization = provider.Service?.Name ?? "Specialist",
            ChargesAmount = provider.Service?.Price ?? 0,
            TodayDateFormatted = now.ToString("dddd, MMMM dd, yyyy"),
            DayName = now.ToString("dddd"),
            IsWorkingToday = isWorkingToday,
            WorkingHours = workingHours,
            BreakHours = breakHours,
            CurrentWorkStatus = currentStatus,
            CurrentStatusNote = statusNote,
            TodayAppointmentsCount = todayAppointments.Count,
            CompletedTodayCount = todayAppointments.Count(a => currentTime >= a.EndTime || a.Status == AppointmentStatus.Completed),
            RemainingTodayCount = todayAppointments.Count(a => currentTime < a.EndTime && a.Status != AppointmentStatus.Completed),
            TodayTimeline = timeline,
            TotalServicesCount = serviceItems.Count,
            Services = serviceItems
        };
    }

    private static string Format12Hour(TimeSpan time)
    {
        int hour = time.Hours;
        string period = hour >= 12 ? "PM" : "AM";
        int h12 = hour % 12;
        if (h12 == 0) h12 = 12;
        return $"{h12:D2}:{time.Minutes:D2} {period}";
    }
}
