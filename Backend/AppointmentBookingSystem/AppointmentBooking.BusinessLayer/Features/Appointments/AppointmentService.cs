using System.Security.Cryptography;
using AppointmentBooking.BusinessLayer.Common.Interfaces;
using AppointmentBooking.BusinessLayer.Common.Models;
using AppointmentBooking.BusinessLayer.Features.Appointments.DTOs;
using AppointmentBooking.BusinessLayer.Features.Auth.DTOs;
using AppointmentBooking.DomainLayer.Entities;
using AppointmentBooking.DomainLayer.Enums;
using AppointmentBooking.InfrastructureLayer.Repositories;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AppointmentBooking.BusinessLayer.Features.Appointments;

public class AppointmentService : IAppointmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IEmailService _emailService;
    private readonly ILogger<AppointmentService> _logger;

    public AppointmentService(
        IUnitOfWork unitOfWork, 
        IMapper mapper, 
        IPasswordHasher passwordHasher,
        IDateTimeProvider dateTimeProvider,
        IEmailService emailService,
        ILogger<AppointmentService> logger)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _passwordHasher = passwordHasher;
        _dateTimeProvider = dateTimeProvider;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<ProviderDailyAvailabilityResponseDto?> GetAvailableSlotsAsync(int providerId, DateTime date, List<int>? serviceIds = null, CancellationToken cancellationToken = default)
    {
        var provider = await _unitOfWork.Repository<ServiceProvider>()
            .Query()
            .Include(p => p.User)
            .Include(p => p.Service)
            .FirstOrDefaultAsync(p => p.Id == providerId, cancellationToken);

        if (provider == null)
        {
            return null;
        }

        int serviceDuration = (provider.Service != null && provider.Service.DurationMinutes > 0)
            ? provider.Service.DurationMinutes
            : 30;

        string serviceTypeName = provider.Service?.Name ?? string.Empty;

        // Multi-service combined duration calculation
        if (serviceIds != null && serviceIds.Count > 0)
        {
            var validIds = serviceIds.Where(id => id > 0).ToList();
            if (validIds.Count > 0)
            {
                var distinctIds = validIds.Distinct().ToList();
                var targetServices = await _unitOfWork.Repository<Service>().Query()
                    .AsNoTracking()
                    .Where(s => distinctIds.Contains(s.Id) && s.IsActive && !s.IsDeleted)
                    .ToListAsync(cancellationToken);

                if (targetServices.Count > 0)
                {
                    serviceDuration = validIds
                        .Select(id => targetServices.FirstOrDefault(s => s.Id == id)?.DurationMinutes is int m && m > 0 ? m : 30)
                        .Sum();
                    serviceTypeName = string.Join(" + ", validIds
                        .Select(id => targetServices.FirstOrDefault(s => s.Id == id)?.Name)
                        .Where(n => !string.IsNullOrEmpty(n)));
                }
            }
        }

        var response = new ProviderDailyAvailabilityResponseDto
        {
            ServiceProviderId = provider.Id,
            ServiceProviderName = provider.User != null ? $"{provider.User.FirstName} {provider.User.LastName}".Trim() : string.Empty,
            ServiceType = serviceTypeName,
            Date = date.Date,
            DayName = date.DayOfWeek.ToString(),
            IsWorkingDay = false,
            WorkingHours = "Closed",
            SlotDurationMinutes = serviceDuration,
            Slots = new List<AvailableSlotDto>()
        };

        // If provider is inactive, they do not accept bookings
        if (provider.Status != ProviderStatus.Active)
        {
            return response;
        }

        // Check date-specific override first, then fallback to weekly day of week
        var specificAvailability = await _unitOfWork.Repository<ProviderAvailability>().Query()
            .FirstOrDefaultAsync(pa => pa.ServiceProviderId == providerId 
                                    && pa.SpecificDate.HasValue 
                                    && pa.SpecificDate.Value.Date == date.Date, cancellationToken);

        var availability = specificAvailability ?? await _unitOfWork.Repository<ProviderAvailability>().Query()
            .FirstOrDefaultAsync(pa => pa.ServiceProviderId == providerId 
                                    && !pa.SpecificDate.HasValue 
                                    && pa.DayOfWeek == date.DayOfWeek, cancellationToken);

        if (availability == null || !availability.IsAvailable)
        {
            return response;
        }

        int slotDurationMins = serviceDuration > 0 ? serviceDuration : (availability.SlotDurationMinutes > 0 ? availability.SlotDurationMinutes : 30);

        response.IsWorkingDay = true;
        response.WorkingHours = $"{availability.StartTime:hh\\:mm} – {availability.EndTime:hh\\:mm}";
        response.SlotDurationMinutes = slotDurationMins;

        // Fetch existing active appointments for this provider on this date
        var existingAppointments = await _unitOfWork.Repository<Appointment>().Query()
            .Where(a => a.ServiceProviderId == providerId 
                     && a.AppointmentDate.Date == date.Date
                     && a.Status != AppointmentStatus.Cancelled
                     && a.Status != AppointmentStatus.Rescheduled)
            .ToListAsync(cancellationToken);

        // Calculate time slot intervals using timezone-aware business local time
        var now = _dateTimeProvider.GetCurrentLocalTime();
        var currentSlotTime = availability.StartTime;
        var duration = TimeSpan.FromMinutes(slotDurationMins);
        int stepMinutes = slotDurationMins >= 90 ? 30 : slotDurationMins;
        var step = TimeSpan.FromMinutes(stepMinutes);

        while (currentSlotTime + duration <= availability.EndTime)
        {
            var nextSlotTime = currentSlotTime + duration;
            string startStr = currentSlotTime.ToString(@"hh\:mm");
            string endStr = nextSlotTime.ToString(@"hh\:mm");

            // Check if slot overlaps with provider's scheduled break time
            bool isBreak = availability.BreakStartTime.HasValue 
                        && availability.BreakEndTime.HasValue 
                        && currentSlotTime < availability.BreakEndTime.Value 
                        && nextSlotTime > availability.BreakStartTime.Value;

            // Check if slot has already been booked (overlapping non-cancelled appointments)
            bool isBooked = existingAppointments.Any(a => a.StartTime < nextSlotTime && a.EndTime > currentSlotTime);

            // Check if slot is in the past
            bool isPast = false;
            if (date.Date < now.Date)
            {
                isPast = true;
            }
            else if (date.Date == now.Date && currentSlotTime <= now.TimeOfDay)
            {
                isPast = true;
            }

            var slotDto = new AvailableSlotDto
            {
                StartTime = startStr,
                EndTime = endStr,
                DisplayText = $"{date.Date.Add(currentSlotTime):hh:mm tt} – {date.Date.Add(nextSlotTime):hh:mm tt}",
                IsAvailable = !isBreak && !isBooked && !isPast,
                Status = isBreak ? "Break" : (isBooked ? "Booked" : (isPast ? "Past" : "Available")),
                Reason = isBreak ? "Staff Lunch / Rest Break" : (isBooked ? "Slot already booked" : (isPast ? "Slot time has passed" : null))
            };

            response.Slots.Add(slotDto);
            currentSlotTime = currentSlotTime.Add(step);
        }

        return response;
    }

    public async Task<ProviderDailyAvailabilityResponseDto?> GetAvailableSlotsByServiceAsync(
        int serviceId, 
        DateTime date, 
        int? tenantId, 
        List<int>? serviceIds = null,
        string? assignedSpecialistIds = null,
        CancellationToken cancellationToken = default)
    {
        var targetServiceIds = serviceIds != null && serviceIds.Count > 0
            ? serviceIds.Where(id => id > 0).Distinct().ToList()
            : (serviceId > 0 ? new List<int> { serviceId } : new List<int>());

        if (serviceId > 0 && !targetServiceIds.Contains(serviceId))
        {
            targetServiceIds.Insert(0, serviceId);
        }
        targetServiceIds = targetServiceIds.Distinct().ToList();

        if (targetServiceIds.Count == 0)
        {
            return null;
        }

        var distinctIds = targetServiceIds.Distinct().ToList();
        targetServiceIds = distinctIds;
        var services = await _unitOfWork.Repository<Service>().Query()
            .AsNoTracking()
            .Where(s => distinctIds.Contains(s.Id) && s.IsActive && !s.IsDeleted)
            .ToListAsync(cancellationToken);

        if (services.Count == 0)
        {
            return null;
        }

        int slotDurationMins = targetServiceIds
            .Select(id => services.FirstOrDefault(s => s.Id == id)?.DurationMinutes is int m && m > 0 ? m : 30)
            .Sum();
        string serviceTypeName = string.Join(" + ", targetServiceIds
            .Select(id => services.FirstOrDefault(s => s.Id == id)?.Name)
            .Where(n => !string.IsNullOrEmpty(n)));

        var response = new ProviderDailyAvailabilityResponseDto
        {
            ServiceProviderId = 0,
            ServiceProviderName = "All Available Specialists",
            ServiceType = serviceTypeName,
            Date = date.Date,
            DayName = date.DayOfWeek.ToString(),
            IsWorkingDay = false,
            WorkingHours = "Closed",
            SlotDurationMinutes = slotDurationMins,
            Slots = new List<AvailableSlotDto>()
        };

        // Fetch all active specialists offering ALL selected services (via Primary Service OR Many-to-Many ProviderServiceLinks)
        var providersQuery = _unitOfWork.Repository<ServiceProvider>().Query()
            .Include(p => p.User)
            .Include(p => p.ProviderServiceLinks)
            .Where(p => p.Status == ProviderStatus.Active && !p.IsDeleted);

        if (tenantId.HasValue && tenantId.Value > 0)
        {
            providersQuery = providersQuery.Where(p => p.TenantId == tenantId.Value);
        }

        var allProviders = await providersQuery.ToListAsync(cancellationToken);
        var providers = allProviders.Where(p =>
            distinctIds.All(sId => p.ServiceId == sId || p.ProviderServiceLinks.Any(ps => ps.ServiceId == sId && !ps.IsDeleted))
        ).ToList();

        if (providers.Count == 0)
        {
            if (distinctIds.Count > 1)
            {
                // Multi-Specialist Sequential Slot Engine (Urban Company Split & Combo Model)
                var assignedProviderMap = new Dictionary<int, int>();
                if (!string.IsNullOrWhiteSpace(assignedSpecialistIds))
                {
                    var parts = assignedSpecialistIds.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        var part = parts[i];
                        if (part.Contains(':'))
                        {
                            var sub = part.Split(':');
                            if (int.TryParse(sub[0], out int sid) && int.TryParse(sub[1], out int pid) && pid > 0)
                            {
                                assignedProviderMap[sid] = pid;
                            }
                        }
                        else if (int.TryParse(part, out int pid) && pid > 0 && i < distinctIds.Count)
                        {
                            assignedProviderMap[distinctIds[i]] = pid;
                        }
                    }
                }

                // Urban Company Intelligent Provider Clustering:
                // Group services belonging to the same assigned specialist consecutively
                // so that e.g. services 1, 2, 4, 5 for Provider A are evaluated as a continuous block,
                // followed by service 3 for Provider B (avoiding awkward multi-handoff fragmentation).
                if (assignedProviderMap.Count > 0)
                {
                    distinctIds = distinctIds
                        .OrderBy(sId => assignedProviderMap.TryGetValue(sId, out int pId) ? pId : int.MaxValue)
                        .ToList();
                }

                var serviceLegs = new List<(int ServiceId, int DurationMinutes, List<ServiceProvider> QualifiedProviders)>();
                foreach (var sId in distinctIds)
                {
                    var sMatch = services.FirstOrDefault(s => s.Id == sId);
                    int dur = sMatch?.DurationMinutes is int dm && dm > 0 ? dm : 30;

                    List<ServiceProvider> qp;
                    if (assignedProviderMap.TryGetValue(sId, out int assignedPid))
                    {
                        qp = allProviders.Where(p => p.Id == assignedPid).ToList();
                        if (qp.Count == 0)
                        {
                            qp = allProviders.Where(p => p.ServiceId == sId || p.ProviderServiceLinks.Any(ps => ps.ServiceId == sId && !ps.IsDeleted)).ToList();
                        }
                    }
                    else
                    {
                        qp = allProviders.Where(p => p.ServiceId == sId || p.ProviderServiceLinks.Any(ps => ps.ServiceId == sId && !ps.IsDeleted)).ToList();
                    }

                    if (qp.Count == 0)
                    {
                        // Fallback to active salon specialists so slots can still be generated and booked
                        qp = allProviders;
                    }
                    serviceLegs.Add((sId, dur, qp));
                }

                var allLegProviderIds = serviceLegs.SelectMany(l => l.QualifiedProviders.Select(p => p.Id)).Distinct().ToList();

                var legSpecificAvail = await _unitOfWork.Repository<ProviderAvailability>().Query()
                    .Where(pa => allLegProviderIds.Contains(pa.ServiceProviderId)
                              && pa.SpecificDate.HasValue
                              && pa.SpecificDate.Value.Date == date.Date)
                    .ToListAsync(cancellationToken);

                var legWeeklyAvail = await _unitOfWork.Repository<ProviderAvailability>().Query()
                    .Where(pa => allLegProviderIds.Contains(pa.ServiceProviderId)
                              && !pa.SpecificDate.HasValue
                              && pa.DayOfWeek == date.DayOfWeek)
                    .ToListAsync(cancellationToken);

                var legExistingAppts = await _unitOfWork.Repository<Appointment>().Query()
                    .Where(a => a.ServiceProviderId.HasValue 
                             && allLegProviderIds.Contains(a.ServiceProviderId.Value)
                             && a.AppointmentDate.Date == date.Date
                             && a.Status != AppointmentStatus.Cancelled
                             && a.Status != AppointmentStatus.Rescheduled)
                    .ToListAsync(cancellationToken);

                TimeSpan? overallMin = null;
                TimeSpan? overallMax = null;

                foreach (var pId in allLegProviderIds)
                {
                    var av = legSpecificAvail.FirstOrDefault(sa => sa.ServiceProviderId == pId)
                          ?? legWeeklyAvail.FirstOrDefault(wa => wa.ServiceProviderId == pId);
                    if (av != null && av.IsAvailable)
                    {
                        if (!overallMin.HasValue || av.StartTime < overallMin.Value) overallMin = av.StartTime;
                        if (!overallMax.HasValue || av.EndTime > overallMax.Value) overallMax = av.EndTime;
                    }
                }

                if (!overallMin.HasValue || !overallMax.HasValue)
                {
                    response.WorkingHours = "No specialists available on this day";
                    return response;
                }

                response.IsWorkingDay = true;
                response.WorkingHours = $"{overallMin.Value:hh\\:mm} – {overallMax.Value:hh\\:mm}";

                var nowLocal = _dateTimeProvider.GetCurrentLocalTime();
                var totalDuration = TimeSpan.FromMinutes(slotDurationMins);
                int stepMins = slotDurationMins >= 90 ? 30 : slotDurationMins;
                var stepInterval = TimeSpan.FromMinutes(stepMins);
                var slotCursor = overallMin.Value;

                while (slotCursor + totalDuration <= overallMax.Value)
                {
                    var slotEnd = slotCursor + totalDuration;
                    bool canFulfillAllLegs = true;
                    var currentLegStart = slotCursor;

                    foreach (var leg in serviceLegs)
                    {
                        var legDuration = TimeSpan.FromMinutes(leg.DurationMinutes);
                        var legEnd = currentLegStart + legDuration;

                        bool hasSpecialistForLeg = false;
                        foreach (var prov in leg.QualifiedProviders)
                        {
                            var av = legSpecificAvail.FirstOrDefault(sa => sa.ServiceProviderId == prov.Id)
                                  ?? legWeeklyAvail.FirstOrDefault(wa => wa.ServiceProviderId == prov.Id);

                            if (av == null || !av.IsAvailable) continue;
                            if (currentLegStart < av.StartTime || legEnd > av.EndTime) continue;

                            bool isBreak = av.BreakStartTime.HasValue && av.BreakEndTime.HasValue
                                        && currentLegStart < av.BreakEndTime.Value
                                        && legEnd > av.BreakStartTime.Value;
                            if (isBreak) continue;

                            bool isBooked = legExistingAppts.Any(a => a.ServiceProviderId == prov.Id
                                                                && a.StartTime < legEnd
                                                                && a.EndTime > currentLegStart);
                            if (isBooked) continue;

                            hasSpecialistForLeg = true;
                            break;
                        }

                        if (!hasSpecialistForLeg)
                        {
                            canFulfillAllLegs = false;
                            break;
                        }

                        currentLegStart = legEnd;
                    }

                    bool isPast = false;
                    if (date.Date < nowLocal.Date) isPast = true;
                    else if (date.Date == nowLocal.Date && slotCursor <= nowLocal.TimeOfDay) isPast = true;

                    bool isAvail = canFulfillAllLegs && !isPast;
                    string status = isPast ? "Past" : (canFulfillAllLegs ? "Available" : "Booked");
                    string? reason = isPast ? "Slot time has passed" : (!canFulfillAllLegs ? "Specialists unavailable during this interval" : null);

                    string startStr = slotCursor.ToString(@"hh\:mm");
                    string endStr = slotEnd.ToString(@"hh\:mm");

                    response.Slots.Add(new AvailableSlotDto
                    {
                        StartTime = startStr,
                        EndTime = endStr,
                        DisplayText = $"{DateTime.Today.Add(slotCursor):hh:mm tt} – {DateTime.Today.Add(slotEnd):hh:mm tt}",
                        IsAvailable = isAvail,
                        Status = status,
                        Reason = reason
                    });

                    slotCursor += stepInterval;
                }

                return response;
            }

            response.WorkingHours = "No specialists certified for all selected services";
            return response;
        }

        var providerIds = providers.Select(p => p.Id).ToList();

        // Fetch availabilities for all these providers on this date
        var specificAvailabilities = await _unitOfWork.Repository<ProviderAvailability>().Query()
            .Where(pa => providerIds.Contains(pa.ServiceProviderId)
                      && pa.SpecificDate.HasValue
                      && pa.SpecificDate.Value.Date == date.Date)
            .ToListAsync(cancellationToken);

        var weeklyAvailabilities = await _unitOfWork.Repository<ProviderAvailability>().Query()
            .Where(pa => providerIds.Contains(pa.ServiceProviderId)
                      && !pa.SpecificDate.HasValue
                      && pa.DayOfWeek == date.DayOfWeek)
            .ToListAsync(cancellationToken);

        // Fetch all existing non-cancelled appointments for all these providers on this date
        var existingAppointments = await _unitOfWork.Repository<Appointment>().Query()
            .Where(a => a.ServiceProviderId.HasValue 
                     && providerIds.Contains(a.ServiceProviderId.Value)
                     && a.AppointmentDate.Date == date.Date
                     && a.Status != AppointmentStatus.Cancelled
                     && a.Status != AppointmentStatus.Rescheduled)
            .ToListAsync(cancellationToken);

        // Filter providers who are active and working on this date
        var activeWorkingProviders = new List<(ServiceProvider Provider, ProviderAvailability Availability)>();
        foreach (var p in providers)
        {
            var avail = specificAvailabilities.FirstOrDefault(sa => sa.ServiceProviderId == p.Id)
                     ?? weeklyAvailabilities.FirstOrDefault(wa => wa.ServiceProviderId == p.Id);

            if (avail != null && avail.IsAvailable)
            {
                activeWorkingProviders.Add((p, avail));
            }
        }

        if (activeWorkingProviders.Count == 0)
        {
            return response;
        }

        response.IsWorkingDay = true;
        var minStart = activeWorkingProviders.Min(x => x.Availability.StartTime);
        var maxEnd = activeWorkingProviders.Max(x => x.Availability.EndTime);
        response.WorkingHours = $"{minStart:hh\\:mm} – {maxEnd:hh\\:mm}";

        var now = _dateTimeProvider.GetCurrentLocalTime();
        var duration = TimeSpan.FromMinutes(slotDurationMins);
        int stepMinutes = slotDurationMins >= 90 ? 30 : slotDurationMins;
        var step = TimeSpan.FromMinutes(stepMinutes);
        var currentSlotTime = minStart;

        while (currentSlotTime + duration <= maxEnd)
        {
            var nextSlotTime = currentSlotTime + duration;
            string startStr = currentSlotTime.ToString(@"hh\:mm");
            string endStr = nextSlotTime.ToString(@"hh\:mm");

            // Count how many providers are available and free at this time
            int freeProvidersCount = 0;
            foreach (var (provider, avail) in activeWorkingProviders)
            {
                if (currentSlotTime < avail.StartTime || nextSlotTime > avail.EndTime)
                    continue;

                bool isBreak = avail.BreakStartTime.HasValue && avail.BreakEndTime.HasValue
                            && currentSlotTime < avail.BreakEndTime.Value
                            && nextSlotTime > avail.BreakStartTime.Value;
                if (isBreak)
                    continue;

                bool isBooked = existingAppointments.Any(a => a.ServiceProviderId == provider.Id
                                                           && a.StartTime < nextSlotTime
                                                           && a.EndTime > currentSlotTime);
                if (isBooked)
                    continue;

                freeProvidersCount++;
            }

            bool isPast = false;
            if (date.Date < now.Date)
            {
                isPast = true;
            }
            else if (date.Date == now.Date && currentSlotTime <= now.TimeOfDay)
            {
                isPast = true;
            }

            bool isAvailable = freeProvidersCount > 0 && !isPast;
            string status = isPast ? "Past" : (freeProvidersCount > 0 ? "Available" : "Booked");
            string? reason = isPast 
                ? "Slot time has passed" 
                : (freeProvidersCount > 0 ? $"{freeProvidersCount} {(freeProvidersCount == 1 ? "specialist" : "specialists")} available" : "All specialists are booked");

            response.Slots.Add(new AvailableSlotDto
            {
                StartTime = startStr,
                EndTime = endStr,
                DisplayText = $"{date.Date.Add(currentSlotTime):hh:mm tt} – {date.Date.Add(nextSlotTime):hh:mm tt}",
                IsAvailable = isAvailable,
                Status = status,
                Reason = reason
            });

            currentSlotTime = currentSlotTime.Add(step);
        }

        return response;
    }

    public async Task<UserDto?> LookupCustomerAsync(int? tenantId, string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var cleanQuery = query.Trim();
        var normalizedEmail = cleanQuery.ToLowerInvariant();
        var digitsOnly = new string(cleanQuery.Where(char.IsDigit).ToArray());

        var usersQuery = _unitOfWork.Repository<User>().Query()
            .Where(u => u.Role == UserRole.Customer && u.IsActive);

        if (tenantId.HasValue)
        {
            usersQuery = usersQuery.Where(u => !u.TenantId.HasValue || u.TenantId == tenantId.Value);
        }

        User? customer = null;

        if (cleanQuery.Contains("@"))
        {
            customer = await usersQuery.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);
        }
        else if (digitsOnly.Length >= 7)
        {
            var matchDigits = digitsOnly.Length >= 10 ? digitsOnly.Substring(digitsOnly.Length - 10) : digitsOnly;
            customer = await usersQuery.FirstOrDefaultAsync(u => u.PhoneNumber != null && u.PhoneNumber.Contains(matchDigits), cancellationToken);
        }
        else
        {
            customer = await usersQuery.FirstOrDefaultAsync(u =>
                (u.FirstName + " " + u.LastName).ToLower().Contains(cleanQuery.ToLower()), cancellationToken);
        }

        if (customer == null)
            return null;

        return new UserDto
        {
            Id = customer.Id,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            FullName = $"{customer.FirstName} {customer.LastName}".Trim(),
            Email = customer.Email,
            PhoneNumber = customer.PhoneNumber,
            Address = customer.Address,
            City = customer.City,
            State = customer.State,
            PostalCode = customer.PostalCode,
            TenantId = customer.TenantId,
            Role = customer.Role,
            IsPasswordAutoGenerated = customer.IsPasswordAutoGenerated
        };
    }

    public async Task<List<UserDto>> SearchCustomersAsync(int? tenantId, string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<UserDto>();

        var cleanQuery = query.Trim().ToLower();
        var digitsOnly = new string(query.Where(char.IsDigit).ToArray());

        var usersQuery = _unitOfWork.Repository<User>().Query()
            .Where(u => u.Role == UserRole.Customer && u.IsActive);

        if (tenantId.HasValue)
        {
            usersQuery = usersQuery.Where(u => !u.TenantId.HasValue || u.TenantId == tenantId.Value);
        }

        usersQuery = usersQuery.Where(u =>
            (u.FirstName + " " + u.LastName).ToLower().Contains(cleanQuery) ||
            u.Email.ToLower().Contains(cleanQuery) ||
            (digitsOnly.Length >= 3 && u.PhoneNumber != null && u.PhoneNumber.Contains(digitsOnly))
        );

        var list = await usersQuery
            .OrderBy(u => u.FirstName)
            .ThenBy(u => u.LastName)
            .Take(8)
            .ToListAsync(cancellationToken);

        return list.Select(customer => new UserDto
        {
            Id = customer.Id,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            FullName = $"{customer.FirstName} {customer.LastName}".Trim(),
            Email = customer.Email,
            PhoneNumber = customer.PhoneNumber,
            Address = customer.Address,
            City = customer.City,
            State = customer.State,
            PostalCode = customer.PostalCode,
            TenantId = customer.TenantId,
            Role = customer.Role,
            IsPasswordAutoGenerated = customer.IsPasswordAutoGenerated
        }).ToList();
    }

    public async Task<ApiResponse<AppointmentDto>> BookAppointmentAsync(
        int? tenantId, 
        int? customerId, 
        BookAppointmentRequestDto request, 
        bool isBookedByProvider = false, 
        string? createdByName = null, 
        CancellationToken cancellationToken = default)
    {
        bool isDirectProviderBooking = request.ServiceProviderId.HasValue && request.ServiceProviderId.Value > 0;
        ServiceProvider? provider = null;
        Service? service = null;
        int resolvedTenantId;

        // Parse and Validate Times
        if (!TimeSpan.TryParse(request.StartTime, out var startTime) || !TimeSpan.TryParse(request.EndTime, out var endTime))
        {
            return ApiResponse<AppointmentDto>.Fail("Invalid time format. Please provide time in HH:mm format.");
        }

        if (endTime <= startTime)
        {
            return ApiResponse<AppointmentDto>.Fail("EndTime must be after StartTime.");
        }

        var appointmentDate = request.AppointmentDate.Date;
        var now = _dateTimeProvider.GetCurrentLocalTime();

        // Block past dates
        if (appointmentDate < now.Date)
        {
            return ApiResponse<AppointmentDto>.Fail("Cannot book an appointment for a past date.");
        }

        // Block dates beyond 15 days from today
        var maxAdvanceBookingDate = now.Date.AddDays(15);
        if (appointmentDate > maxAdvanceBookingDate)
        {
            return ApiResponse<AppointmentDto>.Fail($"Appointments can only be booked up to 15 days in advance (until {maxAdvanceBookingDate:dd-MMM-yyyy}).");
        }

        // Block past time slots on today's date
        if (appointmentDate == now.Date && startTime <= now.TimeOfDay)
        {
            return ApiResponse<AppointmentDto>.Fail("Cannot book a time slot that has already passed.");
        }

        // Multi-Service Cart resolution (Urban Company model - Supports repeated items / quantities)
        var targetServiceIds = request.ServiceIds != null && request.ServiceIds.Count > 0
            ? request.ServiceIds.Where(id => id > 0).ToList()
            : (request.ServiceId.HasValue && request.ServiceId.Value > 0 ? new List<int> { request.ServiceId.Value } : new List<int>());

        if (isDirectProviderBooking && targetServiceIds.Count == 0 && request.ServiceProviderId.HasValue)
        {
            var pInfo = await _unitOfWork.Repository<ServiceProvider>().GetByIdAsync(request.ServiceProviderId.Value, cancellationToken);
            if (pInfo?.ServiceId.HasValue == true)
            {
                targetServiceIds.Add(pInfo.ServiceId.Value);
            }
        }

        if (targetServiceIds.Count == 0)
        {
            return ApiResponse<AppointmentDto>.Fail("Please select at least one valid service to book.");
        }

        var distinctIds = targetServiceIds.Distinct().ToList();
        targetServiceIds = distinctIds;
        var allServices = await _unitOfWork.Repository<Service>().Query()
            .Where(s => distinctIds.Contains(s.Id) && s.IsActive && !s.IsDeleted)
            .ToListAsync(cancellationToken);

        if (allServices.Count == 0)
        {
            return ApiResponse<AppointmentDto>.Fail("Selected service(s) are inactive or not found.");
        }

        service = allServices.FirstOrDefault(s => s.Id == request.ServiceId) ?? allServices.First();
        decimal totalAmount = targetServiceIds
            .Select(id => allServices.FirstOrDefault(s => s.Id == id)?.Price ?? 0m)
            .Sum();
        int totalDurationMinutes = targetServiceIds
            .Select(id => allServices.FirstOrDefault(s => s.Id == id)?.DurationMinutes is int m && m > 0 ? m : 30)
            .Sum();

        if (isDirectProviderBooking)
        {
            // 1. Direct Specialist Path
            provider = await _unitOfWork.Repository<ServiceProvider>()
                .Query()
                .Include(p => p.User)
                .Include(p => p.Service)
                .Include(p => p.ProviderServiceLinks)
                .FirstOrDefaultAsync(p => p.Id == request.ServiceProviderId!.Value, cancellationToken);

            if (provider == null)
            {
                return ApiResponse<AppointmentDto>.Fail("Service provider not found.");
            }

            if (tenantId.HasValue && provider.TenantId != tenantId.Value)
            {
                return ApiResponse<AppointmentDto>.Fail("Service provider not found in current workspace.");
            }

            if (provider.Status != ProviderStatus.Active)
            {
                return ApiResponse<AppointmentDto>.Fail("This service provider is inactive and currently not accepting appointments.");
            }

            // Verify specialist has all target services assigned
            var providerLinks = await _unitOfWork.Repository<ProviderServiceLink>().Query()
                .Where(ps => ps.ServiceProviderId == provider.Id && !ps.IsDeleted)
                .Select(ps => ps.ServiceId)
                .ToListAsync(cancellationToken);

            if (provider.ServiceId.HasValue && !providerLinks.Contains(provider.ServiceId.Value))
            {
                providerLinks.Add(provider.ServiceId.Value);
            }

            bool isQualified = distinctIds.All(id => providerLinks.Contains(id));
            if (!isQualified)
            {
                var provName = provider.User != null ? $"{provider.User.FirstName} {provider.User.LastName}".Trim() : "This specialist";
                return ApiResponse<AppointmentDto>.Fail($"{provName} is not certified to perform all selected services in this booking.");
            }

            resolvedTenantId = tenantId ?? provider.TenantId;

            // Verify Provider Availability configuration (Specific date override first, then weekly)
            var specificAvailability = await _unitOfWork.Repository<ProviderAvailability>().Query()
                .FirstOrDefaultAsync(pa => pa.ServiceProviderId == request.ServiceProviderId 
                                        && pa.SpecificDate.HasValue 
                                        && pa.SpecificDate.Value.Date == appointmentDate);

            var availability = specificAvailability ?? await _unitOfWork.Repository<ProviderAvailability>().Query()
                .FirstOrDefaultAsync(pa => pa.ServiceProviderId == request.ServiceProviderId 
                                        && !pa.SpecificDate.HasValue 
                                        && pa.DayOfWeek == appointmentDate.DayOfWeek);

            if (availability == null || !availability.IsAvailable)
            {
                var provName = provider.User != null ? $"{provider.User.FirstName} {provider.User.LastName}".Trim() : "Provider";
                return ApiResponse<AppointmentDto>.Fail($"Service provider {provName} is not available on {appointmentDate.DayOfWeek}.");
            }

            if (startTime < availability.StartTime || endTime > availability.EndTime)
            {
                return ApiResponse<AppointmentDto>.Fail($"Selected slot is outside provider's operating hours ({availability.StartTime:hh\\:mm} – {availability.EndTime:hh\\:mm}).");
            }

            if (availability.BreakStartTime.HasValue && availability.BreakEndTime.HasValue)
            {
                if (startTime < availability.BreakEndTime.Value && endTime > availability.BreakStartTime.Value)
                {
                    return ApiResponse<AppointmentDto>.Fail($"Selected slot falls within the provider's scheduled break time ({availability.BreakStartTime.Value:hh\\:mm} – {availability.BreakEndTime.Value:hh\\:mm}).");
                }
            }
        }
        else
        {
            // 2. Urban Company Broadcast Path: Validate Qualified Specialists
            var specialistsQuery = _unitOfWork.Repository<ServiceProvider>().Query()
                .Include(p => p.ProviderServiceLinks)
                .Where(p => p.Status == ProviderStatus.Active && !p.IsDeleted);

            if (tenantId.HasValue && tenantId.Value > 0)
            {
                specialistsQuery = specialistsQuery.Where(p => p.TenantId == tenantId.Value);
            }

            var allSpecialists = await specialistsQuery.ToListAsync(cancellationToken);
            var activeSpecialists = allSpecialists.Where(p =>
                distinctIds.All(sId => p.ServiceId == sId || p.ProviderServiceLinks.Any(ps => ps.ServiceId == sId && !ps.IsDeleted))
            ).ToList();

            if (activeSpecialists.Count == 0)
            {
                // Fallback: If no single specialist has ALL services across categories, allow broadcast to specialists who can perform the selected services or active specialists in the tenant
                activeSpecialists = allSpecialists.Where(p =>
                    distinctIds.Any(sId => p.ServiceId == sId || p.ProviderServiceLinks.Any(ps => ps.ServiceId == sId && !ps.IsDeleted))
                ).ToList();

                if (activeSpecialists.Count == 0)
                {
                    activeSpecialists = allSpecialists;
                }

                if (activeSpecialists.Count == 0)
                {
                    return ApiResponse<AppointmentDto>.Fail("No active specialists are currently available to perform the requested services.");
                }
            }

            resolvedTenantId = tenantId ?? activeSpecialists.FirstOrDefault()?.TenantId ?? 1;
        }

        // Concurrency Guard & Creation inside Transaction
        await _unitOfWork.BeginTransactionAsync();
        try
        {
            if (isDirectProviderBooking)
            {
                bool isSlotTaken = await _unitOfWork.Repository<Appointment>().Query()
                    .AnyAsync(a => a.ServiceProviderId == request.ServiceProviderId
                                && a.AppointmentDate.Date == appointmentDate
                                && a.Status != AppointmentStatus.Cancelled
                                && a.Status != AppointmentStatus.Rescheduled
                                && a.StartTime < endTime 
                                && a.EndTime > startTime);

                if (isSlotTaken)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    return ApiResponse<AppointmentDto>.Fail("This time slot has just been booked by another customer. Please choose a different slot.");
                }
            }

            // 5. Unified Customer Account Linking / Creation (Even for Guests)
            var normalizedEmail = request.CustomerEmail.Trim().ToLowerInvariant();
            User? customerUser = null;

            if (customerId.HasValue)
            {
                customerUser = await _unitOfWork.Repository<User>().GetByIdAsync(customerId.Value);
            }

            if (customerUser == null)
            {
                customerUser = await _unitOfWork.Repository<User>().Query()
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
            }

            if (customerUser == null && !string.IsNullOrWhiteSpace(request.CustomerPhone))
            {
                var digitsOnly = new string(request.CustomerPhone.Where(char.IsDigit).ToArray());
                if (digitsOnly.Length >= 10)
                {
                    var last10 = digitsOnly.Substring(digitsOnly.Length - 10);
                    customerUser = await _unitOfWork.Repository<User>().Query()
                        .FirstOrDefaultAsync(u => u.Role == UserRole.Customer && u.PhoneNumber != null && u.PhoneNumber.Contains(last10));
                }
            }

            // Set default initial customer password to "123456"
            var defaultPassword = "123456";
            bool isNewCustomerAccount = false;

            if (customerUser == null)
            {
                isNewCustomerAccount = true;
                var nameParts = request.CustomerName.Trim().Split(' ', 2);
                customerUser = new User
                {
                    FirstName = nameParts[0],
                    LastName = nameParts.Length > 1 ? nameParts[1] : string.Empty,
                    Email = normalizedEmail,
                    PasswordHash = _passwordHasher.HashPassword(defaultPassword),
                    PhoneNumber = request.CustomerPhone.Trim(),
                    Address = request.CustomerAddress?.Trim(),
                    Role = UserRole.Customer,
                    TenantId = resolvedTenantId,
                    IsActive = true,
                    IsPasswordAutoGenerated = true,
                    CreatedAt = DateTime.UtcNow
                };
                await _unitOfWork.Repository<User>().AddAsync(customerUser);
                await _unitOfWork.SaveChangesAsync();
            }
            else
            {
                if (!customerUser.TenantId.HasValue && resolvedTenantId > 0)
                {
                    customerUser.TenantId = resolvedTenantId;
                }
                // If user was created without password, set default password to "123456"
                if (string.IsNullOrEmpty(customerUser.PasswordHash))
                {
                    customerUser.PasswordHash = _passwordHasher.HashPassword(defaultPassword);
                    customerUser.IsPasswordAutoGenerated = true;
                }
                if (string.IsNullOrWhiteSpace(customerUser.PhoneNumber) && !string.IsNullOrWhiteSpace(request.CustomerPhone))
                {
                    customerUser.PhoneNumber = request.CustomerPhone.Trim();
                }
                if (string.IsNullOrWhiteSpace(customerUser.Address) && !string.IsNullOrWhiteSpace(request.CustomerAddress))
                {
                    customerUser.Address = request.CustomerAddress.Trim();
                }
                await _unitOfWork.SaveChangesAsync();
            }

            customerId = customerUser.Id;

            // 6. Generate Unique Human-Readable Appointment Number (e.g., APT-20261014-4821)
            string appointmentNumber;
            bool isUnique = false;
            int attempts = 0;
            do
            {
                int randomNum = RandomNumberGenerator.GetInt32(1000, 9999);
                appointmentNumber = $"APT-{DateTime.UtcNow:yyyyMMdd}-{randomNum}";
                isUnique = !await _unitOfWork.Repository<Appointment>().Query()
                    .AnyAsync(a => a.AppointmentNumber == appointmentNumber);
                attempts++;
            } while (!isUnique && attempts < 10);

            var appointment = new Appointment
            {
                TenantId = resolvedTenantId,
                AppointmentNumber = appointmentNumber,
                ServiceProviderId = isDirectProviderBooking ? request.ServiceProviderId : null,
                ServiceId = service.Id,
                CustomerId = customerId.Value,
                Reason = request.Reason?.Trim(),
                AppointmentDate = appointmentDate,
                StartTime = startTime,
                EndTime = endTime,
                TotalAmount = totalAmount,
                TotalDurationMinutes = totalDurationMinutes,
                Status = (isDirectProviderBooking && isBookedByProvider) ? AppointmentStatus.Approved : AppointmentStatus.Booked,
                ApprovedAt = (isDirectProviderBooking && isBookedByProvider) ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = !string.IsNullOrWhiteSpace(createdByName) 
                    ? createdByName.Trim() 
                    : (isBookedByProvider ? "Service Provider" : $"{customerUser.FirstName} {customerUser.LastName}".Trim())
            };

            // Attach multi-service cart items (Urban Company model - Supports repeated items / quantities)
            foreach (var sId in targetServiceIds)
            {
                var s = allServices.FirstOrDefault(x => x.Id == sId);
                if (s != null)
                {
                    appointment.Items.Add(new AppointmentItem
                    {
                        ServiceId = s.Id,
                        ServiceName = s.Name,
                        Price = s.Price,
                        DurationMinutes = s.DurationMinutes > 0 ? s.DurationMinutes : 30,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = appointment.CreatedBy
                    });
                }
            }

            await _unitOfWork.Repository<Appointment>().AddAsync(appointment);
            await _unitOfWork.CommitTransactionAsync();

            // If a new customer account was created during booking flow, dispatch credentials email in background
            if (isNewCustomerAccount && !string.IsNullOrWhiteSpace(customerUser?.Email))
            {
                var targetCustomerEmail = customerUser.Email;
                var customerFullName = $"{customerUser.FirstName} {customerUser.LastName}".Trim();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emailService.SendAccountCreatedEmailAsync(
                            targetCustomerEmail,
                            customerFullName,
                            defaultPassword);
                    }
                    catch (Exception emailEx)
                    {
                        _logger.LogError(emailEx, "Failed to send welcome credentials email to newly registered Customer {Email} during multi-booking flow", targetCustomerEmail);
                    }
                });
            }

            // If a new customer account was created during booking flow, dispatch credentials email in background
            if (isNewCustomerAccount && !string.IsNullOrWhiteSpace(customerUser?.Email))
            {
                var targetCustomerEmail = customerUser.Email;
                var customerFullName = $"{customerUser.FirstName} {customerUser.LastName}".Trim();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emailService.SendAccountCreatedEmailAsync(
                            targetCustomerEmail,
                            customerFullName,
                            defaultPassword);
                    }
                    catch (Exception emailEx)
                    {
                        _logger.LogError(emailEx, "Failed to send welcome credentials email to newly registered Customer {Email} during booking flow", targetCustomerEmail);
                    }
                });
            }

            // Load navigation properties for return (AsNoTracking)
            var created = await _unitOfWork.Repository<Appointment>().Query()
                .AsNoTracking()
                .Include(a => a.ServiceProvider).ThenInclude(s => s!.User)
                .Include(a => a.Service)
                .Include(a => a.Customer)
                .Include(a => a.Items).ThenInclude(i => i.Service)
                .FirstAsync(a => a.Id == appointment.Id);

            string successMsg = isDirectProviderBooking 
                ? "Appointment booked successfully." 
                : "Appointment request broadcasted to specialists successfully! A specialist will confirm shortly.";

            return ApiResponse<AppointmentDto>.Ok(_mapper.Map<AppointmentDto>(created), successMsg);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            return ApiResponse<AppointmentDto>.Fail(ex.Message);
        }
    }

    public async Task<ApiResponse<List<AppointmentDto>>> BookMultiAppointmentAsync(
        int? tenantId, 
        int? customerId, 
        BookMultiAppointmentRequestDto request, 
        bool isBookedByProvider = false, 
        string? createdByName = null, 
        CancellationToken cancellationToken = default)
    {
        if (request.Legs == null || request.Legs.Count == 0)
        {
            return ApiResponse<List<AppointmentDto>>.Fail("At least one booking leg is required.");
        }

        // 1. Collect all distinct service IDs across all legs
        var allServiceIds = request.Legs
            .SelectMany(l => l.ServiceIds != null && l.ServiceIds.Count > 0 ? l.ServiceIds : (l.ServiceId.HasValue ? new List<int> { l.ServiceId.Value } : new List<int>()))
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (allServiceIds.Count == 0)
        {
            return ApiResponse<List<AppointmentDto>>.Fail("Please select at least one valid service.");
        }

        var allServices = await _unitOfWork.Repository<Service>().Query()
            .Where(s => allServiceIds.Contains(s.Id) && s.IsActive && !s.IsDeleted)
            .ToListAsync(cancellationToken);

        if (allServices.Count == 0)
        {
            return ApiResponse<List<AppointmentDto>>.Fail("Selected services were not found or are inactive.");
        }

        // 2. Resolve / Create customer User
        var normalizedEmail = request.CustomerEmail.Trim().ToLowerInvariant();
        User? customerUser = null;

        if (customerId.HasValue)
        {
            customerUser = await _unitOfWork.Repository<User>().GetByIdAsync(customerId.Value);
        }

        if (customerUser == null)
        {
            customerUser = await _unitOfWork.Repository<User>().Query()
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);
        }

        if (customerUser == null && !string.IsNullOrWhiteSpace(request.CustomerPhone))
        {
            var digitsOnly = new string(request.CustomerPhone.Where(char.IsDigit).ToArray());
            if (digitsOnly.Length >= 10)
            {
                var last10 = digitsOnly.Substring(digitsOnly.Length - 10);
                customerUser = await _unitOfWork.Repository<User>().Query()
                    .FirstOrDefaultAsync(u => u.Role == UserRole.Customer && u.PhoneNumber != null && u.PhoneNumber.Contains(last10), cancellationToken);
            }
        }

        var defaultPassword = "123456";
        bool isNewCustomerAccount = false;

        if (customerUser == null)
        {
            isNewCustomerAccount = true;
            var nameParts = request.CustomerName.Trim().Split(' ', 2);
            customerUser = new User
            {
                FirstName = nameParts[0],
                LastName = nameParts.Length > 1 ? nameParts[1] : string.Empty,
                Email = normalizedEmail,
                PhoneNumber = request.CustomerPhone.Trim(),
                Address = request.CustomerAddress.Trim(),
                Role = UserRole.Customer,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Repository<User>().AddAsync(customerUser);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (string.IsNullOrEmpty(customerUser.Address) && !string.IsNullOrWhiteSpace(request.CustomerAddress))
            {
                customerUser.Address = request.CustomerAddress.Trim();
                _unitOfWork.Repository<User>().Update(customerUser);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        customerId = customerUser.Id;
        int resolvedTenantId = tenantId ?? customerUser.TenantId ?? 1;
        if (resolvedTenantId <= 0) resolvedTenantId = 1;

        var createdAppointments = new List<Appointment>();

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            int legIndex = 0;
            int totalLegs = request.Legs.Count;
            var sharedBookingGroupId = totalLegs > 1 
                ? $"GRP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}" 
                : null;

            foreach (var leg in request.Legs)
            {
                legIndex++;
                if (!TimeSpan.TryParse(leg.StartTime, out var startTime) || !TimeSpan.TryParse(leg.EndTime, out var endTime))
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    return ApiResponse<List<AppointmentDto>>.Fail($"Invalid time format for leg {legIndex}. Use HH:mm.");
                }

                if (endTime <= startTime)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    return ApiResponse<List<AppointmentDto>>.Fail($"EndTime must be after StartTime for leg {legIndex}.");
                }

                var legDate = leg.AppointmentDate.Date;
                var targetLegServiceIds = leg.ServiceIds != null && leg.ServiceIds.Count > 0
                    ? leg.ServiceIds.Where(id => id > 0).Distinct().ToList()
                    : (leg.ServiceId.HasValue && leg.ServiceId.Value > 0 ? new List<int> { leg.ServiceId.Value } : new List<int>());

                if (targetLegServiceIds.Count == 0 && allServiceIds.Count > 0)
                {
                    targetLegServiceIds = new List<int> { allServiceIds[0] };
                }

                var legServices = allServices.Where(s => targetLegServiceIds.Contains(s.Id)).ToList();
                var primaryLegService = legServices.FirstOrDefault(s => s.Id == leg.ServiceId) ?? legServices.FirstOrDefault() ?? allServices.First();

                decimal legTotalAmount = targetLegServiceIds
                    .Select(id => allServices.FirstOrDefault(s => s.Id == id)?.Price ?? 0m)
                    .Sum();

                int legTotalDuration = targetLegServiceIds
                    .Select(id => allServices.FirstOrDefault(s => s.Id == id)?.DurationMinutes is int m && m > 0 ? m : 30)
                    .Sum();

                bool isDirect = leg.ServiceProviderId.HasValue && leg.ServiceProviderId.Value > 0;

                if (isDirect)
                {
                    bool isSlotTaken = await _unitOfWork.Repository<Appointment>().Query()
                        .AnyAsync(a => a.ServiceProviderId == leg.ServiceProviderId.Value
                                    && a.AppointmentDate.Date == legDate
                                    && a.Status != AppointmentStatus.Cancelled
                                    && a.Status != AppointmentStatus.Rescheduled
                                    && a.StartTime < endTime
                                    && a.EndTime > startTime, cancellationToken);

                    if (isSlotTaken)
                    {
                        await _unitOfWork.RollbackTransactionAsync();
                        return ApiResponse<List<AppointmentDto>>.Fail($"Specialist is already booked during {leg.StartTime} - {leg.EndTime} on {legDate:yyyy-MM-dd}.");
                    }
                }

                // Generate Unique Appointment Number
                string appointmentNumber;
                bool isUnique = false;
                int attempts = 0;
                var rand = new Random();
                do
                {
                    appointmentNumber = $"APT-{legDate:yyyyMMdd}-{rand.Next(1000, 9999)}";
                    isUnique = !await _unitOfWork.Repository<Appointment>().Query()
                        .AnyAsync(a => a.AppointmentNumber == appointmentNumber, cancellationToken);
                    attempts++;
                } while (!isUnique && attempts < 10);

                var reasonNote = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

                var appointment = new Appointment
                {
                    TenantId = resolvedTenantId,
                    AppointmentNumber = appointmentNumber,
                    BookingGroupId = sharedBookingGroupId,
                    ServiceProviderId = isDirect ? leg.ServiceProviderId : null,
                    ServiceId = primaryLegService.Id,
                    CustomerId = customerId.Value,
                    Reason = reasonNote,
                    AppointmentDate = legDate,
                    StartTime = startTime,
                    EndTime = endTime,
                    TotalAmount = legTotalAmount,
                    TotalDurationMinutes = legTotalDuration,
                    Status = (isDirect && isBookedByProvider) ? AppointmentStatus.Approved : AppointmentStatus.Booked,
                    ApprovedAt = (isDirect && isBookedByProvider) ? DateTime.UtcNow : null,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = !string.IsNullOrWhiteSpace(createdByName) 
                        ? createdByName.Trim() 
                        : (isBookedByProvider ? "Service Provider" : $"{customerUser.FirstName} {customerUser.LastName}".Trim())
                };

                foreach (var sId in targetLegServiceIds)
                {
                    var s = allServices.FirstOrDefault(x => x.Id == sId);
                    if (s != null)
                    {
                        appointment.Items.Add(new AppointmentItem
                        {
                            ServiceId = s.Id,
                            ServiceName = s.Name,
                            Price = s.Price,
                            DurationMinutes = s.DurationMinutes > 0 ? s.DurationMinutes : 30,
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = appointment.CreatedBy
                        });
                    }
                }

                await _unitOfWork.Repository<Appointment>().AddAsync(appointment);
                createdAppointments.Add(appointment);
            }

            await _unitOfWork.CommitTransactionAsync();

            // If a new customer account was created during booking flow, dispatch credentials email in background
            if (isNewCustomerAccount && !string.IsNullOrWhiteSpace(customerUser?.Email))
            {
                var targetCustomerEmail = customerUser.Email;
                var customerFullName = $"{customerUser.FirstName} {customerUser.LastName}".Trim();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emailService.SendAccountCreatedEmailAsync(
                            targetCustomerEmail,
                            customerFullName,
                            defaultPassword);
                    }
                    catch (Exception emailEx)
                    {
                        _logger.LogError(emailEx, "Failed to send welcome credentials email to newly registered Customer {Email} during multi-booking flow", targetCustomerEmail);
                    }
                });
            }

            // Load navigation properties for return
            var createdIds = createdAppointments.Select(a => a.Id).ToList();
            var resultList = await _unitOfWork.Repository<Appointment>().Query()
                .AsNoTracking()
                .Include(a => a.ServiceProvider).ThenInclude(s => s!.User)
                .Include(a => a.Service)
                .Include(a => a.Customer)
                .Include(a => a.Items).ThenInclude(i => i.Service)
                .Where(a => createdIds.Contains(a.Id))
                .ToListAsync(cancellationToken);

            var dtos = _mapper.Map<List<AppointmentDto>>(resultList);
            return ApiResponse<List<AppointmentDto>>.Ok(dtos, $"{dtos.Count} appointment(s) booked successfully!");
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            return ApiResponse<List<AppointmentDto>>.Fail(ex.Message);
        }
    }

    public async Task<ApiResponse<AppointmentDto>> RescheduleAppointmentAsync(
        int appointmentId, 
        int? tenantId, 
        int? customerId, 
        bool isStaff, 
        RescheduleAppointmentRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Appointment>().Query()
            .Include(a => a.ServiceProvider).ThenInclude(s => s.User)
            .Include(a => a.Customer);

        var appointment = tenantId.HasValue
            ? await query.FirstOrDefaultAsync(a => a.Id == appointmentId && a.TenantId == tenantId.Value)
            : await query.FirstOrDefaultAsync(a => a.Id == appointmentId);

        if (appointment == null)
        {
            return ApiResponse<AppointmentDto>.Fail($"Appointment with ID {appointmentId} not found.");
        }

        // Ownership / IDOR Security Verification
        var authError = VerifyAppointmentOwnership(appointment, customerId, isStaff, request.CustomerContact);
        if (authError != null)
        {
            return ApiResponse<AppointmentDto>.Fail(authError);
        }

        // Restrict rescheduling for completed or cancelled appointments
        if (appointment.Status == AppointmentStatus.Completed)
        {
            return ApiResponse<AppointmentDto>.Fail("Cannot reschedule an appointment that has already been completed.");
        }

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            return ApiResponse<AppointmentDto>.Fail("Cannot reschedule a cancelled appointment.");
        }

        if (!appointment.ServiceProviderId.HasValue || appointment.ServiceProviderId.Value <= 0)
        {
            return ApiResponse<AppointmentDto>.Fail("This appointment request has not been accepted by a specialist yet and cannot be rescheduled.");
        }

        var now = _dateTimeProvider.GetCurrentLocalTime();
        var appointmentStartDateTime = appointment.AppointmentDate.Date.Add(appointment.StartTime);
        if (now >= appointmentStartDateTime.AddHours(-1))
        {
            return ApiResponse<AppointmentDto>.Fail("Appointments can only be rescheduled at least 1 hour prior to the scheduled start time.");
        }

        if (!TimeSpan.TryParse(request.NewStartTime, out var newStart) || !TimeSpan.TryParse(request.NewEndTime, out var newEnd))
        {
            return ApiResponse<AppointmentDto>.Fail("Invalid time format for new schedule.");
        }

        var newDate = request.NewDate.Date;
        if (newDate < now.Date)
        {
            return ApiResponse<AppointmentDto>.Fail("Cannot reschedule to a past date.");
        }

        var maxAdvanceRescheduleDate = now.Date.AddDays(15);
        if (newDate > maxAdvanceRescheduleDate)
        {
            return ApiResponse<AppointmentDto>.Fail($"Appointments can only be rescheduled up to 15 days in advance (until {maxAdvanceRescheduleDate:dd-MMM-yyyy}).");
        }

        if (newDate == now.Date && newStart <= now.TimeOfDay)
        {
            return ApiResponse<AppointmentDto>.Fail("Cannot reschedule to a time slot that has already passed.");
        }

        if (appointment.AppointmentDate.Date == newDate && appointment.StartTime == newStart && appointment.EndTime == newEnd)
        {
            return ApiResponse<AppointmentDto>.Fail("The appointment is already scheduled for this slot. Please select a different date or time slot.");
        }

        // Verify provider availability for new schedule (Specific date override first, then weekly)
        var specificAvailability = await _unitOfWork.Repository<ProviderAvailability>().Query()
            .FirstOrDefaultAsync(pa => pa.ServiceProviderId == appointment.ServiceProviderId 
                                    && pa.SpecificDate.HasValue 
                                    && pa.SpecificDate.Value.Date == newDate);

        var availability = specificAvailability ?? await _unitOfWork.Repository<ProviderAvailability>().Query()
            .FirstOrDefaultAsync(pa => pa.ServiceProviderId == appointment.ServiceProviderId 
                                    && !pa.SpecificDate.HasValue 
                                    && pa.DayOfWeek == newDate.DayOfWeek);

        if (availability == null || !availability.IsAvailable)
        {
            return ApiResponse<AppointmentDto>.Fail($"Provider is not available on {newDate.DayOfWeek}.");
        }

        if (newStart < availability.StartTime || newEnd > availability.EndTime)
        {
            return ApiResponse<AppointmentDto>.Fail("New slot is outside provider's operating hours.");
        }

        if (availability.BreakStartTime.HasValue && availability.BreakEndTime.HasValue)
        {
            if (newStart < availability.BreakEndTime.Value && newEnd > availability.BreakStartTime.Value)
            {
                return ApiResponse<AppointmentDto>.Fail($"New slot falls within the provider's scheduled break time ({availability.BreakStartTime.Value:hh\\:mm} – {availability.BreakEndTime.Value:hh\\:mm}).");
            }
        }

        // Concurrency check on new slot
        await _unitOfWork.BeginTransactionAsync();
        try
        {
            bool isSlotTaken = await _unitOfWork.Repository<Appointment>().Query()
                .AnyAsync(a => a.Id != appointmentId
                            && a.ServiceProviderId == appointment.ServiceProviderId
                            && a.AppointmentDate.Date == newDate
                            && a.Status != AppointmentStatus.Cancelled
                            && a.Status != AppointmentStatus.Rescheduled
                            && a.StartTime < newEnd 
                            && a.EndTime > newStart);

            if (isSlotTaken)
            {
                await _unitOfWork.RollbackTransactionAsync();
                return ApiResponse<AppointmentDto>.Fail("The requested new time slot is already booked. Please choose another slot.");
            }

            // Release previous slot and apply new schedule
            appointment.AppointmentDate = newDate;
            appointment.StartTime = newStart;
            appointment.EndTime = newEnd;
            if (!string.IsNullOrWhiteSpace(request.Reason))
            {
                appointment.Reason = request.Reason.Trim();
            }
            appointment.Status = AppointmentStatus.Booked;
            appointment.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.Repository<Appointment>().Update(appointment);
            await _unitOfWork.CommitTransactionAsync();

            return ApiResponse<AppointmentDto>.Ok(_mapper.Map<AppointmentDto>(appointment), "Appointment rescheduled successfully.");
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            return ApiResponse<AppointmentDto>.Fail(ex.Message);
        }
    }

    public async Task<ApiResponse<AppointmentDto>> CancelAppointmentAsync(
        int appointmentId, 
        int? tenantId, 
        int? customerId, 
        bool isStaff, 
        CancelAppointmentRequestDto request,
        string? cancellerName = null,
        CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Appointment>().Query()
            .Include(a => a.ServiceProvider).ThenInclude(s => s.User)
            .Include(a => a.Customer);

        var appointment = tenantId.HasValue
            ? await query.FirstOrDefaultAsync(a => a.Id == appointmentId && a.TenantId == tenantId.Value)
            : await query.FirstOrDefaultAsync(a => a.Id == appointmentId);

        if (appointment == null)
        {
            return ApiResponse<AppointmentDto>.Fail($"Appointment with ID {appointmentId} not found.");
        }

        // Ownership / IDOR Security Verification
        var authError = VerifyAppointmentOwnership(appointment, customerId, isStaff, request.CustomerContact);
        if (authError != null)
        {
            return ApiResponse<AppointmentDto>.Fail(authError);
        }

        if (appointment.Status == AppointmentStatus.Completed)
        {
            return ApiResponse<AppointmentDto>.Fail("Cannot cancel an appointment that has already been marked as completed.");
        }

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            return ApiResponse<AppointmentDto>.Fail("This appointment is already cancelled.");
        }

        var now = _dateTimeProvider.GetCurrentLocalTime();
        var appointmentStartDateTime = appointment.AppointmentDate.Date.Add(appointment.StartTime);
        if (now >= appointmentStartDateTime.AddHours(-1))
        {
            return ApiResponse<AppointmentDto>.Fail("Appointments can only be cancelled at least 1 hour prior to the scheduled start time.");
        }

        try
        {
            string canceller = "Customer";
            if (!string.IsNullOrWhiteSpace(cancellerName))
            {
                canceller = cancellerName.Trim();
            }
            else if (isStaff)
            {
                canceller = "Staff / Specialist";
            }
            else if (appointment.Customer != null)
            {
                var custName = $"{appointment.Customer.FirstName} {appointment.Customer.LastName}".Trim();
                if (!string.IsNullOrWhiteSpace(custName))
                {
                    canceller = custName;
                }
            }

            var reasonText = string.IsNullOrWhiteSpace(request?.CancellationReason)
                ? "Cancelled upon request"
                : request.CancellationReason.Trim();

            appointment.Status = AppointmentStatus.Cancelled;
            appointment.CancelledAt = DateTime.UtcNow;
            appointment.CancellationReason = $"{reasonText} (Cancelled by {canceller})";
            appointment.ProposedDate = null;
            appointment.ProposedStartTime = null;
            appointment.ProposedEndTime = null;
            appointment.RescheduleNotes = null;
            appointment.UpdatedAt = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync();

            return ApiResponse<AppointmentDto>.Ok(_mapper.Map<AppointmentDto>(appointment), "Appointment cancelled successfully.");
        }
        catch (DbUpdateConcurrencyException)
        {
            var latest = await _unitOfWork.Repository<Appointment>().Query()
                .Include(a => a.ServiceProvider)
                .Include(a => a.Customer)
                .FirstOrDefaultAsync(a => a.Id == appointmentId);

            if (latest != null && latest.Status == AppointmentStatus.Cancelled)
            {
                return ApiResponse<AppointmentDto>.Ok(_mapper.Map<AppointmentDto>(latest), "Appointment cancelled successfully.");
            }
            return ApiResponse<AppointmentDto>.Fail("Concurrent update conflict. Please refresh and try again.");
        }
        catch (Exception ex)
        {
            return ApiResponse<AppointmentDto>.Fail(ex.Message);
        }
    }



    private static List<AppointmentDto> GroupMultiPartAppointments(List<AppointmentDto> list)
    {
        if (list == null || list.Count == 0) return new List<AppointmentDto>();

        var result = new List<AppointmentDto>();
        var groupedIds = new HashSet<int>();

        for (int i = 0; i < list.Count; i++)
        {
            var apt = list[i];
            if (groupedIds.Contains(apt.Id)) continue;

                        var parts = list.Where(other =>
                other.AppointmentDate.Date == apt.AppointmentDate.Date &&
                other.CustomerId == apt.CustomerId &&
                Math.Abs((other.CreatedAt - apt.CreatedAt).TotalSeconds) < 120
            ).ToList();

                if (parts.Count > 1)
                {
                    foreach (var p in parts) groupedIds.Add(p.Id);
                    parts = parts.OrderBy(p => p.StartTime).ToList();

                    var firstPart = parts[0];
                    var lastPart = parts[parts.Count - 1];

                    var combinedItems = new List<AppointmentItemDto>();
                    var combinedNames = new List<string>();
                    decimal sumAmount = 0m;
                    int sumDur = 0;

                    foreach (var p in parts)
                    {
                        sumAmount += p.TotalAmount > 0 ? p.TotalAmount : (p.ServicePrice ?? 0m);
                        sumDur += p.TotalDurationMinutes > 0 ? p.TotalDurationMinutes : (p.ServiceDurationMinutes ?? 30);

                        if (p.Items != null && p.Items.Count > 0)
                        {
                            foreach (var it in p.Items)
                            {
                                combinedItems.Add(it);
                                if (!string.IsNullOrEmpty(it.ServiceName) && !combinedNames.Contains(it.ServiceName))
                                {
                                    combinedNames.Add(it.ServiceName);
                                }
                            }
                        }
                        else if (!string.IsNullOrEmpty(p.ServiceName))
                        {
                            combinedItems.Add(new AppointmentItemDto
                            {
                                Id = 0,
                                AppointmentId = p.Id,
                                ServiceId = p.ServiceId ?? 0,
                                ServiceName = p.ServiceName,
                                Price = p.ServicePrice ?? 0m,
                                DurationMinutes = p.ServiceDurationMinutes ?? 30
                            });
                            if (!combinedNames.Contains(p.ServiceName))
                            {
                                combinedNames.Add(p.ServiceName);
                            }
                        }
                    }

                    var isAllApproved = parts.All(p => p.Status == AppointmentStatus.Approved);
                    var isAllCancelled = parts.All(p => p.Status == AppointmentStatus.Cancelled);
                    var isAnyAwaiting = parts.Any(p => !p.ServiceProviderId.HasValue && p.Status == AppointmentStatus.Booked && !p.IsExpired);
                    var approvedParts = parts.Where(p => p.Status == AppointmentStatus.Approved).ToList();
                    var awaitingParts = parts.Where(p => !p.ServiceProviderId.HasValue && p.Status == AppointmentStatus.Booked && !p.IsExpired).ToList();
                    int approvedCount = approvedParts.Count;
                    int totalParts = parts.Count;
                    bool hasPartial = approvedCount > 0 && approvedCount < totalParts;

                    var safeParts = parts.Select(p => new AppointmentDto
                    {
                        Id = p.Id,
                        TenantId = p.TenantId,
                        AppointmentNumber = p.AppointmentNumber,
                        ServiceProviderId = p.ServiceProviderId,
                        ServiceProviderName = p.ServiceProviderName,
                        ServiceProviderType = p.ServiceProviderType,
                        ServiceProviderProfilePictureUrl = p.ServiceProviderProfilePictureUrl,
                        ServiceId = p.ServiceId,
                        ServiceName = p.ServiceName,
                        ServicePrice = p.ServicePrice,
                        ServiceDurationMinutes = p.ServiceDurationMinutes,
                        TotalAmount = p.TotalAmount,
                        TotalDurationMinutes = p.TotalDurationMinutes,
                        Items = p.Items != null ? new List<AppointmentItemDto>(p.Items) : new List<AppointmentItemDto>(),
                        CustomerId = p.CustomerId,
                        CustomerName = p.CustomerName,
                        CustomerEmail = p.CustomerEmail,
                        CustomerPhone = p.CustomerPhone,
                        CustomerAddress = p.CustomerAddress,
                        Reason = p.Reason,
                        AppointmentDate = p.AppointmentDate,
                        StartTime = p.StartTime,
                        EndTime = p.EndTime,
                        Status = p.Status,
                        StatusText = p.StatusText,
                        CreatedAt = p.CreatedAt,
                        CreatedBy = p.CreatedBy,
                        ApprovedAt = p.ApprovedAt,
                        CancelledAt = p.CancelledAt,
                        CancellationReason = p.CancellationReason,
                        RejectionReason = p.RejectionReason,
                        ProposedDate = p.ProposedDate,
                        ProposedStartTime = p.ProposedStartTime,
                        ProposedEndTime = p.ProposedEndTime,
                        RescheduleNotes = p.RescheduleNotes,
                        JourneyParts = null
                    }).ToList();

                    var unified = new AppointmentDto
                    {
                        Id = firstPart.Id,
                        TenantId = firstPart.TenantId,
                        AppointmentNumber = firstPart.AppointmentNumber,
                        CustomerId = firstPart.CustomerId,
                        CustomerName = firstPart.CustomerName,
                        CustomerEmail = firstPart.CustomerEmail,
                        CustomerPhone = firstPart.CustomerPhone,
                        CustomerAddress = firstPart.CustomerAddress,
                        AppointmentDate = firstPart.AppointmentDate,
                        StartTime = firstPart.StartTime,
                        EndTime = lastPart.EndTime,
                        TotalAmount = sumAmount,
                        TotalDurationMinutes = sumDur,
                        Items = combinedItems,
                        ServiceName = string.Join(" + ", combinedNames),
                        CreatedAt = firstPart.CreatedAt,
                        CreatedBy = firstPart.CreatedBy,
                        ApprovedAt = firstPart.ApprovedAt,
                        CancelledAt = firstPart.CancelledAt,
                        CancellationReason = firstPart.CancellationReason,
                        RejectionReason = firstPart.RejectionReason,
                        RescheduledFromAppointmentId = firstPart.RescheduledFromAppointmentId,
                        Reason = firstPart.Reason,
                        HasPartialApproval = hasPartial,
                        ApprovedPartsCount = approvedCount,
                        PendingPartsCount = awaitingParts.Count,
                        JourneyParts = safeParts
                    };

                    var distinctProviders = parts
                        .Where(p => !string.IsNullOrWhiteSpace(p.ServiceProviderName))
                        .Select(p => p.ServiceProviderName)
                        .Distinct()
                        .ToList();

                    if (distinctProviders.Count > 1)
                    {
                        unified.ServiceProviderName = string.Join(", ", distinctProviders);
                        unified.ServiceProviderType = "Multi-Specialist Session";
                    }
                    else if (distinctProviders.Count == 1)
                    {
                        unified.ServiceProviderName = distinctProviders[0];
                    }

                    var hasAssignedProviders = parts.Any(p => p.ServiceProviderId.HasValue);
                    if (hasAssignedProviders)
                    {
                        unified.ServiceProviderId = firstPart.ServiceProviderId ?? parts.FirstOrDefault(p => p.ServiceProviderId.HasValue)?.ServiceProviderId;
                    }

                    if (hasPartial)
                    {
                        unified.Status = AppointmentStatus.Approved;
                        unified.StatusText = $"{approvedCount} of {totalParts} Confirmed";
                    }
                    else if (isAnyAwaiting)
                    {
                        unified.Status = AppointmentStatus.Booked;
                        unified.StatusText = awaitingParts.Count == totalParts ? "Awaiting Specialist" : $"{approvedCount} Confirmed, {awaitingParts.Count} Awaiting";
                    }
                    else if (isAllApproved)
                    {
                        unified.Status = AppointmentStatus.Approved;
                    }
                    else if (isAllCancelled)
                    {
                        unified.Status = AppointmentStatus.Cancelled;
                    }
                    else
                    {
                        unified.Status = firstPart.Status;
                    }

                    result.Add(unified);
                    continue;
                }

            groupedIds.Add(apt.Id);
            result.Add(apt);
        }

        return result;
    }

    public async Task<List<AppointmentDto>> GetMyAppointmentsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var appointments = await _unitOfWork.Repository<Appointment>().Query().AsNoTracking()
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp!.User)
            .Include(a => a.Service)
            .Include(a => a.Customer)
            .Include(a => a.Items).ThenInclude(i => i.Service)
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<List<AppointmentDto>>(appointments);
        return GroupMultiPartAppointments(dtos);
    }

    public async Task<PagedResult<AppointmentDto>> GetMyAppointmentsPagedAsync(
        int customerId, 
        PaginationParams paginationParams, 
        AppointmentStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Appointment>().Query().AsNoTracking()
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp!.User)
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp!.Service)
            .Include(a => a.Service)
            .Include(a => a.Customer)
            .Include(a => a.Items).ThenInclude(i => i.Service)
            .Where(a => a.CustomerId == customerId);

        if (status.HasValue)
        {
            if (status.Value == AppointmentStatus.Booked)
            {
                query = query.Where(a => a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Approved);
            }
            else
            {
                query = query.Where(a => a.Status == status.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(paginationParams.Search))
        {
            var search = paginationParams.Search.Trim().ToLower();
            query = query.Where(a => a.AppointmentNumber.ToLower().Contains(search)
                                  || (a.ServiceProvider != null && (a.ServiceProvider.User.FirstName + " " + a.ServiceProvider.User.LastName).ToLower().Contains(search))
                                  || (a.Customer != null && (a.Customer.FirstName + " " + a.Customer.LastName).ToLower().Contains(search)));
        }

        var appointments = await query
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<List<AppointmentDto>>(appointments);
        var groupedDtos = GroupMultiPartAppointments(dtos);

        var totalCount = groupedDtos.Count;
        var pagedDtos = groupedDtos
            .Skip((paginationParams.PageNumber - 1) * paginationParams.PageSize)
            .Take(paginationParams.PageSize)
            .ToList();

        return new PagedResult<AppointmentDto>(pagedDtos, totalCount, paginationParams.PageNumber, paginationParams.PageSize);
    }

    public async Task<List<AppointmentDto>> GetAppointmentsAsync(
        int tenantId, 
        int? providerId, 
        DateTime? startDate, 
        DateTime? endDate, 
        AppointmentStatus? status, 
        string? search,
        CancellationToken cancellationToken = default)
    {
        await SweepExpiredBroadcastAppointmentsAsync(cancellationToken);

        var query = _unitOfWork.Repository<Appointment>().Query().AsNoTracking()
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp!.User)
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp!.Service)
            .Include(a => a.Service)
            .Include(a => a.Customer)
            .Include(a => a.Items).ThenInclude(i => i.Service)
            .Where(a => a.TenantId == tenantId);

        if (providerId.HasValue)
        {
            query = query.Where(a => a.ServiceProviderId == providerId.Value);
        }

        if (startDate.HasValue)
        {
            query = query.Where(a => a.AppointmentDate.Date >= startDate.Value.Date);
        }

        if (endDate.HasValue)
        {
            query = query.Where(a => a.AppointmentDate.Date <= endDate.Value.Date);
        }

        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string lowerSearch = search.Trim().ToLower();
            query = query.Where(a => a.Customer.FirstName.ToLower().Contains(lowerSearch)
                                  || a.Customer.LastName.ToLower().Contains(lowerSearch)
                                  || (a.Customer.PhoneNumber != null && a.Customer.PhoneNumber.Contains(lowerSearch))
                                  || a.Customer.Email.ToLower().Contains(lowerSearch)
                                  || a.AppointmentNumber.ToLower().Contains(lowerSearch));
        }

        var appointments = await query
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<List<AppointmentDto>>(appointments);
        return GroupMultiPartAppointments(dtos);
    }

    public async Task<PagedResult<AppointmentDto>> GetAppointmentsPagedAsync(
        int tenantId, 
        int? providerId, 
        DateTime? startDate, 
        DateTime? endDate, 
        AppointmentStatus? status, 
        PaginationParams paginationParams,
        CancellationToken cancellationToken = default)
    {
        await SweepExpiredBroadcastAppointmentsAsync(cancellationToken);

        var query = _unitOfWork.Repository<Appointment>().Query().AsNoTracking()
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp!.User)
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp!.Service)
            .Include(a => a.Service)
            .Include(a => a.Customer)
            .Include(a => a.Items).ThenInclude(i => i.Service)
            .Where(a => a.TenantId == tenantId);

        if (providerId.HasValue)
        {
            query = query.Where(a => a.ServiceProviderId == providerId.Value);
        }

        if (startDate.HasValue)
        {
            query = query.Where(a => a.AppointmentDate.Date >= startDate.Value.Date);
        }

        if (endDate.HasValue)
        {
            query = query.Where(a => a.AppointmentDate.Date <= endDate.Value.Date);
        }

        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(paginationParams.Search))
        {
            string lowerSearch = paginationParams.Search.Trim().ToLower();
            query = query.Where(a => a.Customer.FirstName.ToLower().Contains(lowerSearch)
                                  || a.Customer.LastName.ToLower().Contains(lowerSearch)
                                  || (a.Customer.PhoneNumber != null && a.Customer.PhoneNumber.Contains(lowerSearch))
                                  || a.Customer.Email.ToLower().Contains(lowerSearch)
                                  || a.AppointmentNumber.ToLower().Contains(lowerSearch));
        }

        var appointments = await query
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<List<AppointmentDto>>(appointments);
        var groupedDtos = GroupMultiPartAppointments(dtos);

        var totalCount = groupedDtos.Count;
        var pagedDtos = groupedDtos
            .Skip((paginationParams.PageNumber - 1) * paginationParams.PageSize)
            .Take(paginationParams.PageSize)
            .ToList();

        return new PagedResult<AppointmentDto>(pagedDtos, totalCount, paginationParams.PageNumber, paginationParams.PageSize);
    }

    public async Task<AppointmentDto?> GetAppointmentByIdAsync(int appointmentId, int tenantId, CancellationToken cancellationToken = default)
    {
        var appointment = await _unitOfWork.Repository<Appointment>().Query()
            .AsNoTracking()
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp.User)
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp.Service)
            .Include(a => a.Service)
            .Include(a => a.Customer)
            .Include(a => a.Items).ThenInclude(i => i.Service)
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.TenantId == tenantId, cancellationToken);

        return appointment == null ? null : _mapper.Map<AppointmentDto>(appointment);
    }

    public async Task<ApiResponse<AppointmentDto>> UpdateStatusAsync(
        int appointmentId, 
        int? tenantId, 
        int? providerId, 
        int? currentUserId, 
        bool isAdmin, 
        UpdateAppointmentStatusDto request,
        string? cancellerName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _unitOfWork.Repository<Appointment>().Query()
                .Include(a => a.ServiceProvider).ThenInclude(s => s.User)
                .Include(a => a.Customer);

            var appointment = tenantId.HasValue
                ? await query.FirstOrDefaultAsync(a => a.Id == appointmentId && a.TenantId == tenantId.Value)
                : await query.FirstOrDefaultAsync(a => a.Id == appointmentId);

            if (appointment == null)
            {
                return ApiResponse<AppointmentDto>.Fail($"Appointment with ID {appointmentId} not found.");
            }

            // Authorization: Service Provider can only update their own appointments; Admin can manage all
            if (!isAdmin)
            {
                bool isAssignedProvider = false;
                if (providerId.HasValue && appointment.ServiceProviderId == providerId.Value)
                {
                    isAssignedProvider = true;
                }
                else if (currentUserId.HasValue && appointment.ServiceProvider != null && appointment.ServiceProvider.UserId == currentUserId.Value)
                {
                    isAssignedProvider = true;
                }

                if (!isAssignedProvider)
                {
                    return ApiResponse<AppointmentDto>.Fail("You are not authorized to manage appointments for another service provider.");
                }
            }

            // If already at requested status
            if (appointment.Status == request.Status)
            {
                return ApiResponse<AppointmentDto>.Ok(_mapper.Map<AppointmentDto>(appointment), "Status unchanged.");
            }

            // State Machine Validations
            if (appointment.Status == AppointmentStatus.Cancelled && request.Status != AppointmentStatus.Cancelled)
            {
                return ApiResponse<AppointmentDto>.Fail("Cannot change the status of an already cancelled appointment.");
            }

            if (appointment.Status == AppointmentStatus.Completed && (request.Status == AppointmentStatus.Pending || request.Status == AppointmentStatus.Approved))
            {
                return ApiResponse<AppointmentDto>.Fail("A completed appointment cannot be reopened.");
            }

            if (request.Status == AppointmentStatus.Completed && !isAdmin)
            {
                var now = _dateTimeProvider.GetCurrentLocalTime();
                var appointmentEndDateTime = appointment.AppointmentDate.Date.Add(appointment.EndTime);
                if (now < appointmentEndDateTime.AddMinutes(-30))
                {
                    return ApiResponse<AppointmentDto>.Fail("Appointments can only be marked as completed within 30 minutes of the service end time or after completion.");
                }
            }

            if (request.Status == AppointmentStatus.Cancelled || request.Status == AppointmentStatus.RescheduleProposed)
            {
                var now = _dateTimeProvider.GetCurrentLocalTime();
                var appointmentStartDateTime = appointment.AppointmentDate.Date.Add(appointment.StartTime);
                if (now >= appointmentStartDateTime.AddHours(-1))
                {
                    var actionName = request.Status == AppointmentStatus.Cancelled ? "cancelled" : "rescheduled";
                    return ApiResponse<AppointmentDto>.Fail($"Appointments can only be {actionName} at least 1 hour prior to the scheduled start time.");
                }
            }

            // If transitioning from RescheduleProposed to Approved or Booked (Customer accepting the proposal)
            if (appointment.Status == AppointmentStatus.RescheduleProposed && 
                (request.Status == AppointmentStatus.Approved || request.Status == AppointmentStatus.Booked))
            {
                if (appointment.ProposedDate.HasValue && appointment.ProposedStartTime.HasValue && appointment.ProposedEndTime.HasValue)
                {
                    appointment.AppointmentDate = appointment.ProposedDate.Value;
                    appointment.StartTime = appointment.ProposedStartTime.Value;
                    appointment.EndTime = appointment.ProposedEndTime.Value;
                }
            }

            // Apply specific lifecycle tracking
            switch (request.Status)
            {
                case AppointmentStatus.RescheduleProposed:
                    if (request.ProposedDate.HasValue)
                    {
                        appointment.ProposedDate = request.ProposedDate.Value.Date;
                    }
                    if (!string.IsNullOrWhiteSpace(request.ProposedStartTime) && TimeSpan.TryParse(request.ProposedStartTime, out var pStart))
                    {
                        appointment.ProposedStartTime = pStart;
                    }
                    if (!string.IsNullOrWhiteSpace(request.ProposedEndTime) && TimeSpan.TryParse(request.ProposedEndTime, out var pEnd))
                    {
                        appointment.ProposedEndTime = pEnd;
                    }
                    if (!string.IsNullOrWhiteSpace(request.Notes))
                    {
                        appointment.RescheduleNotes = request.Notes.Trim();
                    }
                    break;

                case AppointmentStatus.Approved:
                    appointment.ApprovedAt = DateTime.UtcNow;
                    break;

                case AppointmentStatus.Cancelled:
                    appointment.CancelledAt = DateTime.UtcNow;
                    string staffCanceller = !string.IsNullOrWhiteSpace(cancellerName)
                        ? cancellerName.Trim()
                        : (isAdmin ? "Administrator" : "Service Provider");

                    if (string.IsNullOrWhiteSpace(cancellerName) && !isAdmin && appointment.ServiceProvider?.User != null)
                    {
                        var provName = $"{appointment.ServiceProvider.User.FirstName} {appointment.ServiceProvider.User.LastName}".Trim();
                        if (!string.IsNullOrWhiteSpace(provName))
                        {
                            staffCanceller = provName;
                        }
                    }
                    var cancelNote = !string.IsNullOrWhiteSpace(request.Notes) ? request.Notes.Trim() : "Cancelled by staff";
                    appointment.CancellationReason = $"{cancelNote} (Cancelled by {staffCanceller})";
                    appointment.ProposedDate = null;
                    appointment.ProposedStartTime = null;
                    appointment.ProposedEndTime = null;
                    appointment.RescheduleNotes = null;
                    break;

                case AppointmentStatus.Completed:
                    break;
            }

            appointment.Status = request.Status;
            appointment.UpdatedAt = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync();

            return ApiResponse<AppointmentDto>.Ok(_mapper.Map<AppointmentDto>(appointment), $"Appointment status successfully updated to {request.Status}.");
        }
        catch (Exception ex)
        {
            return ApiResponse<AppointmentDto>.Fail(ex.Message);
        }
    }

    public async Task<ApiResponse<AppointmentDto>> RespondToRescheduleProposalAsync(int appointmentId, int customerId, bool accept, CancellationToken cancellationToken = default)
    {
        try
        {
            var appointment = await _unitOfWork.Repository<Appointment>().Query()
                .Include(a => a.ServiceProvider)
                    .ThenInclude(sp => sp.User)
                .Include(a => a.Customer)
                .FirstOrDefaultAsync(a => a.Id == appointmentId && (customerId <= 0 || a.CustomerId == customerId), cancellationToken);

            if (appointment == null)
            {
                return ApiResponse<AppointmentDto>.Fail("Appointment not found or you are not authorized to respond to this proposal.");
            }

            if (appointment.Status != AppointmentStatus.RescheduleProposed)
            {
                return ApiResponse<AppointmentDto>.Fail("This appointment does not have an active reschedule proposal.");
            }

            if (accept)
            {
                if (!appointment.ProposedDate.HasValue || !appointment.ProposedStartTime.HasValue || !appointment.ProposedEndTime.HasValue)
                {
                    return ApiResponse<AppointmentDto>.Fail("No proposed slot details found on this appointment.");
                }

                // Check if slot is still available
                bool isSlotTaken = await _unitOfWork.Repository<Appointment>().Query()
                    .AnyAsync(a => a.Id != appointmentId
                                && a.ServiceProviderId == appointment.ServiceProviderId
                                && a.AppointmentDate.Date == appointment.ProposedDate.Value.Date
                                && a.Status != AppointmentStatus.Cancelled
                                && a.Status != AppointmentStatus.Rescheduled
                                && a.StartTime < appointment.ProposedEndTime.Value 
                                && a.EndTime > appointment.ProposedStartTime.Value, cancellationToken);

                if (isSlotTaken)
                {
                    return ApiResponse<AppointmentDto>.Fail("The proposed slot is no longer available. Please choose another slot.");
                }

                appointment.AppointmentDate = appointment.ProposedDate.Value.Date;
                appointment.StartTime = appointment.ProposedStartTime.Value;
                appointment.EndTime = appointment.ProposedEndTime.Value;
                appointment.Status = AppointmentStatus.Approved;
                appointment.ApprovedAt = DateTime.UtcNow;
                appointment.ProposedDate = null;
                appointment.ProposedStartTime = null;
                appointment.ProposedEndTime = null;
                appointment.RescheduleNotes = null;
                appointment.UpdatedAt = DateTime.UtcNow;

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return ApiResponse<AppointmentDto>.Ok(_mapper.Map<AppointmentDto>(appointment), "Proposed reschedule accepted successfully. Appointment is confirmed!");
            }
            else
            {
                // Declined by customer: cancel the appointment
                appointment.Status = AppointmentStatus.Cancelled;
                appointment.CancelledAt = DateTime.UtcNow;
                appointment.CancellationReason = "Reschedule proposal declined by customer";
                appointment.ProposedDate = null;
                appointment.ProposedStartTime = null;
                appointment.ProposedEndTime = null;
                appointment.RescheduleNotes = null;
                appointment.UpdatedAt = DateTime.UtcNow;

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return ApiResponse<AppointmentDto>.Ok(_mapper.Map<AppointmentDto>(appointment), "Reschedule proposal declined. Appointment has been cancelled.");
            }
        }
        catch (Exception ex)
        {
            return ApiResponse<AppointmentDto>.Fail(ex.Message);
        }
    }

    public async Task<AppointmentStatsDto> GetStatsAsync(int tenantId, int? providerId = null, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Appointment>().Query()
            .Where(a => a.TenantId == tenantId);

        if (providerId.HasValue && providerId.Value > 0)
        {
            query = query.Where(a => a.ServiceProviderId == providerId.Value);
        }

        var appointments = await query.ToListAsync(cancellationToken);

        var today = _dateTimeProvider.Today;

        return new AppointmentStatsDto
        {
            TotalAppointments = appointments.Count,
            TodayAppointments = appointments.Count(a => a.AppointmentDate.Date == today && a.Status != AppointmentStatus.Cancelled),
            UpcomingAppointments = appointments.Count(a => a.AppointmentDate.Date >= today && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Approved)),
            CompletedAppointments = appointments.Count(a => a.Status == AppointmentStatus.Completed),
            CancelledAppointments = appointments.Count(a => a.Status == AppointmentStatus.Cancelled)
        };
    }

    public async Task<AppointmentStatsDto> GetCustomerStatsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var appointments = await _unitOfWork.Repository<Appointment>().Query()
            .Where(a => a.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var today = _dateTimeProvider.Today;

        return new AppointmentStatsDto
        {
            TotalAppointments = appointments.Count,
            TodayAppointments = appointments.Count(a => a.AppointmentDate.Date == today && a.Status != AppointmentStatus.Cancelled),
            UpcomingAppointments = appointments.Count(a => a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Approved),
            CompletedAppointments = appointments.Count(a => a.Status == AppointmentStatus.Completed),
            CancelledAppointments = appointments.Count(a => a.Status == AppointmentStatus.Cancelled)
        };
    }

    private async Task SweepExpiredBroadcastAppointmentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-10);
            var expired = await _unitOfWork.Repository<Appointment>().Query()
                .Where(a => a.ServiceProviderId == null
                         && a.Status == AppointmentStatus.Booked
                         && a.CreatedAt < cutoffTime)
                .ToListAsync(cancellationToken);

            if (expired.Count > 0)
            {
                foreach (var apt in expired)
                {
                    apt.Status = AppointmentStatus.Cancelled;
                    apt.CancellationReason = "No specialist was available for this requested slot. (Cancelled by System)";
                    apt.CancelledAt = DateTime.UtcNow;
                    apt.UpdatedAt = DateTime.UtcNow;
                    _unitOfWork.Repository<Appointment>().Update(apt);
                }
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sweeping expired broadcast appointments");
        }
    }

    public async Task<int?> GetProviderIdByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        var provider = await _unitOfWork.Repository<ServiceProvider>().Query()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && !p.IsDeleted, cancellationToken);
        return provider?.Id;
    }

    public async Task<List<AppointmentDto>> GetIncomingBroadcastRequestsAsync(
        int? providerId, 
        int? tenantId, 
        CancellationToken cancellationToken = default)
    {
        await SweepExpiredBroadcastAppointmentsAsync(cancellationToken);

        var today = _dateTimeProvider.Today;
        var nowTime = _dateTimeProvider.GetCurrentLocalTime().TimeOfDay;
        var cutoffTime = DateTime.UtcNow.AddMinutes(-10);

        // Base query for unassigned customer broadcast appointments within active 10-minute window
        // Strictly exclude appointments created by Admin/SuperAdmin/Staff so acceptance popup is customer-only
        var query = _unitOfWork.Repository<Appointment>().Query()
            .Include(a => a.Service)
            .Include(a => a.Customer)
            .Include(a => a.Items).ThenInclude(i => i.Service)
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp!.User)
            .Where(a => a.ServiceProviderId == null 
                     && a.Status == AppointmentStatus.Booked
                     && a.CreatedAt >= cutoffTime
                     && a.AppointmentDate.Date >= today
                     && (a.CreatedBy == null || (!a.CreatedBy.Contains("Admin") && !a.CreatedBy.Contains("SuperAdmin") && !a.CreatedBy.Contains("Staff"))));

        if (tenantId.HasValue && tenantId.Value > 0)
        {
            query = query.Where(a => a.TenantId == tenantId.Value);
        }

        if (providerId.HasValue && providerId.Value > 0)
        {
            var provider = await _unitOfWork.Repository<ServiceProvider>().GetByIdAsync(providerId.Value);
            if (provider == null || provider.Status != ProviderStatus.Active)
            {
                return new List<AppointmentDto>();
            }

            // Show requests for any service category the specialist is assigned to
            var assignedServiceIds = await _unitOfWork.Repository<ProviderServiceLink>().Query()
                .Where(ps => ps.ServiceProviderId == providerId.Value)
                .Select(ps => ps.ServiceId)
                .ToListAsync(cancellationToken);

            if (provider.ServiceId.HasValue && !assignedServiceIds.Contains(provider.ServiceId.Value))
            {
                assignedServiceIds.Add(provider.ServiceId.Value);
            }

            if (assignedServiceIds.Count == 0)
            {
                return new List<AppointmentDto>();
            }

            query = query.Where(a => 
                (a.Items.Any() && a.Items.All(i => assignedServiceIds.Contains(i.ServiceId)))
                || (!a.Items.Any() && a.ServiceId.HasValue && assignedServiceIds.Contains(a.ServiceId.Value))
            );

            // Fetch provider's active bookings to avoid showing conflicting slots
            var providerBookings = await _unitOfWork.Repository<Appointment>().Query()
                .Where(a => a.ServiceProviderId == providerId.Value
                         && a.AppointmentDate.Date >= today
                         && a.Status != AppointmentStatus.Cancelled
                         && a.Status != AppointmentStatus.Rescheduled)
                .Select(a => new { a.AppointmentDate, a.StartTime, a.EndTime })
                .ToListAsync(cancellationToken);

            var openRequests = await query
                .OrderBy(a => a.AppointmentDate)
                .ThenBy(a => a.StartTime)
                .ToListAsync(cancellationToken);

            // Filter out requests whose services this provider cannot perform,
            // filter out past slots for today, and any slots where provider is already busy
            var validRequests = openRequests.Where(a =>
            {
                var reqSvcIds = a.Items?.Select(i => i.ServiceId).Where(id => id > 0).Distinct().ToList() ?? new List<int>();
                if (reqSvcIds.Count == 0 && a.ServiceId.HasValue && a.ServiceId.Value > 0)
                {
                    reqSvcIds.Add(a.ServiceId.Value);
                }

                // Provider MUST be able to perform ALL services in this request
                if (reqSvcIds.Count == 0 || !reqSvcIds.All(id => assignedServiceIds.Contains(id)))
                {
                    return false;
                }

                if (a.AppointmentDate.Date == today && a.StartTime <= nowTime)
                    return false;

                bool isConflict = providerBookings.Any(b =>
                    b.AppointmentDate.Date == a.AppointmentDate.Date &&
                    b.StartTime < a.EndTime &&
                    b.EndTime > a.StartTime);

                return !isConflict;
            }).ToList();

            return _mapper.Map<List<AppointmentDto>>(validRequests);
        }
        else
        {
            // For Admins / All Requests
            var openRequests = await query
                .OrderBy(a => a.AppointmentDate)
                .ThenBy(a => a.StartTime)
                .ToListAsync(cancellationToken);

            var validRequests = openRequests.Where(a =>
                !(a.AppointmentDate.Date == today && a.StartTime <= nowTime)
            ).ToList();

            return _mapper.Map<List<AppointmentDto>>(validRequests);
        }
    }

    public async Task<ApiResponse<AppointmentDto>> AcceptBroadcastAppointmentAsync(
        int appointmentId, 
        int providerId, 
        CancellationToken cancellationToken = default)
    {
        var provider = await _unitOfWork.Repository<ServiceProvider>().Query()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == providerId, cancellationToken);

        if (provider == null || provider.Status != ProviderStatus.Active)
        {
            return ApiResponse<AppointmentDto>.Fail("Active service specialist profile was not found.");
        }

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            var appointment = await _unitOfWork.Repository<Appointment>().Query()
                .Include(a => a.Service)
                .Include(a => a.Customer)
                .Include(a => a.Items)
                .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

            if (appointment == null)
            {
                await _unitOfWork.RollbackTransactionAsync();
                return ApiResponse<AppointmentDto>.Fail("Appointment request was not found.");
            }

            // First-come, first-served concurrency check
            if (appointment.ServiceProviderId.HasValue)
            {
                await _unitOfWork.RollbackTransactionAsync();
                return ApiResponse<AppointmentDto>.Fail("This appointment request has already been accepted and confirmed by another specialist.");
            }

            if (appointment.Status == AppointmentStatus.Cancelled)
            {
                await _unitOfWork.RollbackTransactionAsync();
                return ApiResponse<AppointmentDto>.Fail("This appointment has already been cancelled.");
            }

            if (appointment.CreatedAt.AddMinutes(10) < DateTime.UtcNow)
            {
                appointment.Status = AppointmentStatus.Cancelled;
                appointment.CancellationReason = "No specialist was available for this requested slot. (Cancelled by System)";
                appointment.CancelledAt = DateTime.UtcNow;
                appointment.UpdatedAt = DateTime.UtcNow;
                _unitOfWork.Repository<Appointment>().Update(appointment);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync();
                return ApiResponse<AppointmentDto>.Fail("This request has expired as no specialist could be assigned for this slot.");
            }

            // Multi-service certification verification
            var assignedServiceIds = await _unitOfWork.Repository<ProviderServiceLink>().Query()
                .Where(ps => ps.ServiceProviderId == provider.Id && !ps.IsDeleted)
                .Select(ps => ps.ServiceId)
                .ToListAsync(cancellationToken);

            if (provider.ServiceId.HasValue && !assignedServiceIds.Contains(provider.ServiceId.Value))
            {
                assignedServiceIds.Add(provider.ServiceId.Value);
            }

            if (appointment.Items != null && appointment.Items.Count > 0)
            {
                bool isQualifiedForAll = appointment.Items.All(item => assignedServiceIds.Contains(item.ServiceId));
                if (!isQualifiedForAll)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    return ApiResponse<AppointmentDto>.Fail("You are not certified to perform all services requested in this appointment.");
                }
            }
            else if (appointment.ServiceId.HasValue)
            {
                if (!assignedServiceIds.Contains(appointment.ServiceId.Value))
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    return ApiResponse<AppointmentDto>.Fail("You are not certified to perform the service requested in this appointment.");
                }
            }

            // Verify no time conflict with this provider's existing schedule
            bool hasConflict = await _unitOfWork.Repository<Appointment>().Query()
                .AnyAsync(a => a.ServiceProviderId == providerId
                            && a.AppointmentDate.Date == appointment.AppointmentDate.Date
                            && a.Status != AppointmentStatus.Cancelled
                            && a.Status != AppointmentStatus.Rescheduled
                            && a.StartTime < appointment.EndTime
                            && a.EndTime > appointment.StartTime, cancellationToken);

            if (hasConflict)
            {
                await _unitOfWork.RollbackTransactionAsync();
                return ApiResponse<AppointmentDto>.Fail("You already have an active appointment scheduled during this time slot.");
            }

            // Assign provider and auto-approve
            appointment.ServiceProviderId = providerId;
            appointment.Status = AppointmentStatus.Approved;
            appointment.ApprovedAt = _dateTimeProvider.UtcNow;
            appointment.UpdatedAt = _dateTimeProvider.UtcNow;

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            // Reload with relations for DTO (AsNoTracking)
            var updated = await _unitOfWork.Repository<Appointment>().Query()
                .AsNoTracking()
                .Include(a => a.ServiceProvider).ThenInclude(sp => sp!.User)
                .Include(a => a.Service)
                .Include(a => a.Customer)
                .Include(a => a.Items).ThenInclude(i => i.Service)
                .FirstAsync(a => a.Id == appointment.Id, cancellationToken);

            var providerName = provider.User != null ? $"{provider.User.FirstName} {provider.User.LastName}".Trim() : "Specialist";
            return ApiResponse<AppointmentDto>.Ok(_mapper.Map<AppointmentDto>(updated), $"Appointment successfully accepted and confirmed by {providerName}!");
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync();
            return ApiResponse<AppointmentDto>.Fail("This appointment request has already been accepted and confirmed by another specialist.");
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(ex, "Error accepting broadcast appointment ID {AppointmentId} by Provider ID {ProviderId}", appointmentId, providerId);
            return ApiResponse<AppointmentDto>.Fail(ex.Message);
        }
    }

    private static string? VerifyAppointmentOwnership(
        Appointment appointment, 
        int? customerId, 
        bool isStaff, 
        string? customerContact)
    {
        // 1. Staff (Admins, SuperAdmins, Service Providers) have authority to manage bookings
        if (isStaff)
        {
            return null;
        }

        // 2. Authenticated Customer ownership check
        if (customerId.HasValue)
        {
            if (appointment.CustomerId != customerId.Value)
            {
                return "You are not authorized to modify this appointment.";
            }
            return null;
        }

        // 3. Guest / Anonymous Tracking Verification
        if (string.IsNullOrWhiteSpace(customerContact))
        {
            return "Registered contact (mobile number or email) is required to verify ownership.";
        }

        var cleanInput = customerContact.Trim().ToLowerInvariant();
        var phoneMatches = !string.IsNullOrEmpty(appointment.Customer?.PhoneNumber) && cleanInput == appointment.Customer.PhoneNumber.Trim().ToLowerInvariant();
        var emailMatches = !string.IsNullOrEmpty(appointment.Customer?.Email) && cleanInput == appointment.Customer.Email.Trim().ToLowerInvariant();

        if (!phoneMatches && !emailMatches)
        {
            return "Verification failed: The provided contact does not match this booking.";
        }

        return null;
    }
}
