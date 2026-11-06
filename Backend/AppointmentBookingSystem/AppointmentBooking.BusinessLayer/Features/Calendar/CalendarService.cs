using AppointmentBooking.BusinessLayer.Features.Appointments.DTOs;
using AppointmentBooking.BusinessLayer.Features.Calendar.DTOs;
using AppointmentBooking.DomainLayer.Constants;
using AppointmentBooking.DomainLayer.Entities;
using AppointmentBooking.DomainLayer.Enums;
using AppointmentBooking.InfrastructureLayer.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AppointmentBooking.BusinessLayer.Features.Calendar;

public class CalendarService : ICalendarService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CalendarService> _logger;

    public CalendarService(IUnitOfWork unitOfWork, ILogger<CalendarService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<CalendarScheduleResponseDto> GetCalendarScheduleAsync(
        int tenantId,
        CalendarFilterDto filter,
        int? currentUserId,
        string? currentUserRole,
        int? currentProviderId,
        CancellationToken cancellationToken = default)
    {
        var baseDate = filter.Date.Date;

        // 1. Calculate Active View Window (with safety buffer for smooth transitions)
        DateTime rangeStart;
        DateTime rangeEnd;

        if (filter.StartDate.HasValue && filter.EndDate.HasValue)
        {
            rangeStart = filter.StartDate.Value.Date;
            rangeEnd = filter.EndDate.Value.Date;
        }
        else
        {
            switch (filter.View?.ToLowerInvariant())
            {
                case "week":
                    int dayOfWeek = (int)baseDate.DayOfWeek;
                    rangeStart = baseDate.AddDays(-dayOfWeek);
                    rangeEnd = rangeStart.AddDays(6);
                    break;

                case "month":
                    rangeStart = new DateTime(baseDate.Year, baseDate.Month, 1);
                    rangeEnd = rangeStart.AddMonths(1).AddDays(-1);
                    break;

                case "day":
                default:
                    rangeStart = baseDate.AddDays(-7);
                    rangeEnd = baseDate.AddDays(7);
                    break;
            }
        }

        // 2. Resolve Role Capabilities (Strict Enforcement: Admin = View & Book All; Provider = View & Manage Own)
        bool isAdminOrSuperAdmin = currentUserRole == AppRoles.Admin || currentUserRole == AppRoles.SuperAdmin;
        bool isServiceProvider = currentUserRole == AppRoles.ServiceProvider;

        // If Service Provider, ensure currentProviderId is accurately resolved (fallback to userId lookup if claim was missing)
        if (isServiceProvider && (!currentProviderId.HasValue || currentProviderId.Value <= 0) && currentUserId.HasValue)
        {
            var sp = await _unitOfWork.Repository<ServiceProvider>().Query()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == currentUserId.Value && p.TenantId == tenantId && !p.IsDeleted, cancellationToken);
            if (sp != null)
            {
                currentProviderId = sp.Id;
            }
        }

        var permissions = new CalendarPermissionsDto
        {
            CanViewAllProviders = isAdminOrSuperAdmin,
            CanBook = true, // Both Admin and Provider can schedule appointments
            CanManageAppointments = isServiceProvider, // Provider can manage lifecycle; Admin only monitors in calendar
            CanEditAvailability = isServiceProvider
        };

        // 3. Query Active Service Providers
        var providersQuery = _unitOfWork.Repository<ServiceProvider>().Query()
            .AsNoTracking()
            .Include(p => p.User)
            .Include(p => p.Service)
            .Where(p => p.TenantId == tenantId && p.Status == ProviderStatus.Active && !p.IsDeleted);

        // Security & Scope Enforcement:
        // Service Providers strictly see only their own column & schedule.
        // Admins can see all active providers or filter them.
        if (isServiceProvider && currentProviderId.HasValue)
        {
            providersQuery = providersQuery.Where(p => p.Id == currentProviderId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(filter.ProviderIds))
        {
            var requestedIds = filter.ProviderIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => int.TryParse(id, out var val) ? val : 0)
                .Where(id => id > 0)
                .ToHashSet();

            if (requestedIds.Count > 0)
            {
                providersQuery = providersQuery.Where(p => requestedIds.Contains(p.Id));
            }
        }

        var activeProviders = await providersQuery
            .OrderBy(p => p.User != null ? p.User.FirstName : p.Id.ToString())
            .ToListAsync(cancellationToken);

        var activeProviderIds = activeProviders.Select(p => p.Id).ToHashSet();

        // 4. Query Provider Availabilities: Weekly Schedule + Specific Date Overrides
        var availabilities = await _unitOfWork.Repository<ProviderAvailability>().Query()
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && !a.IsDeleted &&
                        activeProviderIds.Contains(a.ServiceProviderId) &&
                        (a.SpecificDate == null || 
                         (a.SpecificDate.HasValue && a.SpecificDate.Value.Date == baseDate)))
            .ToListAsync(cancellationToken);

        var weeklyAvailByProvider = availabilities
            .Where(a => !a.SpecificDate.HasValue)
            .GroupBy(a => a.ServiceProviderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var specificAvailByProvider = availabilities
            .Where(a => a.SpecificDate.HasValue && a.SpecificDate.Value.Date == baseDate)
            .GroupBy(a => a.ServiceProviderId)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault());

        // 5. Query Appointments in View Range
        var appointmentsQuery = _unitOfWork.Repository<Appointment>().Query()
            .AsNoTracking()
            .Include(a => a.Items)
                .ThenInclude(i => i.Service)
            .Include(a => a.Service)
            .Include(a => a.ServiceProvider)
                .ThenInclude(p => p.User)
            .Include(a => a.Customer)
            .Where(a => a.TenantId == tenantId && !a.IsDeleted &&
                        a.AppointmentDate.Date >= rangeStart && a.AppointmentDate.Date <= rangeEnd &&
                        !(a.ServiceProviderId == null && a.Status == AppointmentStatus.Cancelled) &&
                        !(a.Status == AppointmentStatus.Cancelled && a.CancellationReason != null && a.CancellationReason.Contains("No specialist was available")));

        // Security & Scope Enforcement: Service Provider only gets their own appointments
        if (isServiceProvider && currentProviderId.HasValue)
        {
            appointmentsQuery = appointmentsQuery.Where(a => a.ServiceProviderId == currentProviderId.Value);
        }

        // Status filter
        if (filter.Status.HasValue)
        {
            appointmentsQuery = appointmentsQuery.Where(a => (int)a.Status == filter.Status.Value);
        }

        // Text search filter
        if (!string.IsNullOrWhiteSpace(filter.SearchQuery))
        {
            var q = filter.SearchQuery.Trim().ToLower();
            appointmentsQuery = appointmentsQuery.Where(a =>
                (a.Customer != null && (
                    (a.Customer.FirstName != null && a.Customer.FirstName.ToLower().Contains(q)) ||
                    (a.Customer.LastName != null && a.Customer.LastName.ToLower().Contains(q)) ||
                    (a.Customer.PhoneNumber != null && a.Customer.PhoneNumber.Contains(q))
                )) ||
                (a.AppointmentNumber != null && a.AppointmentNumber.ToLower().Contains(q)) ||
                (a.Reason != null && a.Reason.ToLower().Contains(q)));
        }

        var dbAppointments = await appointmentsQuery
            .OrderBy(a => a.AppointmentDate)
            .ThenBy(a => a.StartTime)
            .ToListAsync(cancellationToken);

        // Sibling appointments for multi-session booking groups are strictly ONLY for Admin.
        // Service Providers must NOT see other specialists' sessions!
        if (isAdminOrSuperAdmin)
        {
            var bookingGroupIds = dbAppointments
                .Where(a => !string.IsNullOrEmpty(a.BookingGroupId))
                .Select(a => a.BookingGroupId!)
                .Distinct()
                .ToList();

            if (bookingGroupIds.Count > 0)
            {
                var existingIds = dbAppointments.Select(a => a.Id).ToHashSet();
                var siblingAppointments = await _unitOfWork.Repository<Appointment>().Query()
                    .AsNoTracking()
                    .Include(a => a.Customer)
                    .Include(a => a.ServiceProvider)
                        .ThenInclude(p => p.User)
                    .Include(a => a.Service)
                    .Include(a => a.Items)
                        .ThenInclude(i => i.Service)
                    .Where(a => a.TenantId == tenantId && !a.IsDeleted &&
                                a.BookingGroupId != null &&
                                bookingGroupIds.Contains(a.BookingGroupId) &&
                                !existingIds.Contains(a.Id) &&
                                !(a.ServiceProviderId == null && a.Status == AppointmentStatus.Cancelled) &&
                                !(a.Status == AppointmentStatus.Cancelled && a.CancellationReason != null && a.CancellationReason.Contains("No specialist was available")))
                    .ToListAsync(cancellationToken);

                dbAppointments.AddRange(siblingAppointments);
            }
        }

        // 6. Map Appointments to Clean DTOs
        var allAppointmentDtos = new List<CalendarAppointmentItemDto>();

        foreach (var a in dbAppointments)
        {
            var providerName = a.ServiceProvider?.User != null
                ? $"{a.ServiceProvider.User.FirstName} {a.ServiceProvider.User.LastName}".Trim()
                : "Specialist";

            var customerName = a.Customer != null
                ? $"{a.Customer.FirstName} {a.Customer.LastName}".Trim()
                : "Client";

            var serviceNames = a.Items?.Count > 0
                ? a.Items.Where(i => i.Service != null).Select(i => i.Service.Name).ToList()
                : (a.Service != null ? new List<string> { a.Service.Name } : new List<string>());

            string primaryServiceName = serviceNames.Count > 0 ? serviceNames[0] : (a.Service?.Name ?? "Consultation");

            // Permission rule: Service Providers can manage their own appointments. Admins only view & book.
            bool canManageThisApt = isServiceProvider && currentProviderId.HasValue && a.ServiceProviderId == currentProviderId.Value;

            var aptDto = new CalendarAppointmentItemDto
            {
                Id = a.Id,
                AppointmentNumber = a.AppointmentNumber ?? string.Empty,
                BookingGroupId = a.BookingGroupId,
                ServiceProviderId = a.ServiceProviderId,
                ServiceProviderName = providerName,
                ServiceProviderProfilePictureUrl = a.ServiceProvider?.User?.ProfilePictureUrl,
                CustomerId = a.CustomerId,
                CustomerName = customerName,
                CustomerPhone = a.Customer?.PhoneNumber,
                CustomerEmail = a.Customer?.Email,
                ServiceId = a.ServiceId ?? 0,
                ServiceName = primaryServiceName,
                ServiceNames = serviceNames.Count > 1 ? serviceNames : null,
                Items = a.Items?.Select(i => new AppointmentItemDto
                {
                    Id = i.Id,
                    AppointmentId = i.AppointmentId,
                    ServiceId = i.ServiceId,
                    ServiceName = i.ServiceName,
                    Price = i.Price,
                    DurationMinutes = i.DurationMinutes
                }).ToList() ?? new List<AppointmentItemDto>(),
                TotalServicesCount = Math.Max(1, serviceNames.Count),
                AppointmentDate = a.AppointmentDate.ToString("yyyy-MM-dd"),
                StartTime = FormatTimeSpan(a.StartTime),
                EndTime = FormatTimeSpan(a.EndTime),
                DurationMinutes = a.TotalDurationMinutes > 0 ? a.TotalDurationMinutes : (int)(a.EndTime - a.StartTime).TotalMinutes,
                Price = a.TotalAmount,
                Status = (int)a.Status,
                StatusText = a.Status.ToString(),
                Reason = a.Reason,
                IsMultiService = serviceNames.Count > 1,
                CanManage = canManageThisApt,
                CancellationReason = a.CancellationReason,
                CancelledAt = a.CancelledAt,
                CancelledByName = ExtractCancelledByName(a.CancellationReason),
                CreatedBy = a.CreatedBy,
                CreatedAt = a.CreatedAt,
                ProposedDate = a.ProposedDate,
                ProposedStartTime = a.ProposedStartTime.HasValue ? FormatTimeSpan(a.ProposedStartTime.Value) : null,
                ProposedEndTime = a.ProposedEndTime.HasValue ? FormatTimeSpan(a.ProposedEndTime.Value) : null,
                RescheduleNotes = a.RescheduleNotes,
                RescheduledFromAppointmentId = a.RescheduledFromAppointmentId
            };

            allAppointmentDtos.Add(aptDto);
        }

        // 6.1 If Service Provider is viewing calendar, also map eligible open broadcast requests awaiting acceptance
        if (isServiceProvider && currentProviderId.HasValue)
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-10);
            var providerServiceIds = await _unitOfWork.Repository<ProviderServiceLink>().Query()
                .Where(ps => ps.ServiceProviderId == currentProviderId.Value)
                .Select(ps => ps.ServiceId)
                .ToListAsync(cancellationToken);

            var currentProvObj = activeProviders.FirstOrDefault(p => p.Id == currentProviderId.Value);
            if (currentProvObj?.ServiceId.HasValue == true && !providerServiceIds.Contains(currentProvObj.ServiceId.Value))
            {
                providerServiceIds.Add(currentProvObj.ServiceId.Value);
            }

            var broadcastQuery = _unitOfWork.Repository<Appointment>().Query()
                .AsNoTracking()
                .Include(a => a.Items)
                    .ThenInclude(i => i.Service)
                .Include(a => a.Service)
                .Include(a => a.Customer)
                .Where(a => a.TenantId == tenantId && !a.IsDeleted &&
                            a.ServiceProviderId == null &&
                            a.Status == AppointmentStatus.Booked &&
                            a.CreatedAt >= cutoffTime &&
                            a.AppointmentDate.Date >= rangeStart && a.AppointmentDate.Date <= rangeEnd &&
                            (a.CreatedBy == null || (!a.CreatedBy.Contains("Admin") && !a.CreatedBy.Contains("SuperAdmin") && !a.CreatedBy.Contains("Staff"))));

            List<Appointment> broadcastRequests = new();
            if (providerServiceIds.Count > 0)
            {
                broadcastQuery = broadcastQuery.Where(a =>
                    (a.Items.Any() && a.Items.Any(i => providerServiceIds.Contains(i.ServiceId)))
                    || (!a.Items.Any() && a.ServiceId.HasValue && providerServiceIds.Contains(a.ServiceId.Value)));
                broadcastRequests = await broadcastQuery.ToListAsync(cancellationToken);
            }

            foreach (var b in broadcastRequests)
            {
                var bReqServiceIds = b.Items?.Select(i => i.ServiceId).Where(id => id > 0).Distinct().ToList() ?? new List<int>();
                if (bReqServiceIds.Count == 0 && b.ServiceId.HasValue && b.ServiceId.Value > 0)
                {
                    bReqServiceIds.Add(b.ServiceId.Value);
                }

                // Provider MUST be qualified to perform ALL services in this request
                if (bReqServiceIds.Count == 0 || !bReqServiceIds.All(id => providerServiceIds.Contains(id)))
                {
                    continue;
                }

                // Check if provider already has an appointment during this slot
                bool hasConflict = dbAppointments.Any(booked =>
                    booked.AppointmentDate.Date == b.AppointmentDate.Date &&
                    booked.StartTime < b.EndTime &&
                    booked.EndTime > b.StartTime);

                if (hasConflict) continue;

                var bServiceNames = b.Items?.Count > 0
                    ? b.Items.Where(i => i.Service != null).Select(i => i.Service.Name).ToList()
                    : (b.Service != null ? new List<string> { b.Service.Name } : new List<string>());

                string primaryName = bServiceNames.Count > 0 ? bServiceNames[0] : (b.Service?.Name ?? "Service Request");
                var custName = b.Customer != null ? $"{b.Customer.FirstName} {b.Customer.LastName}".Trim() : "Customer";

                var bDto = new CalendarAppointmentItemDto
                {
                    Id = b.Id,
                    AppointmentNumber = b.AppointmentNumber ?? string.Empty,
                    BookingGroupId = b.BookingGroupId,
                    ServiceProviderId = currentProviderId.Value,
                    ServiceProviderName = currentProvObj?.User != null ? $"{currentProvObj.User.FirstName} {currentProvObj.User.LastName}".Trim() : "You",
                    CustomerId = b.CustomerId,
                    CustomerName = custName,
                    CustomerPhone = b.Customer?.PhoneNumber,
                    CustomerEmail = b.Customer?.Email,
                    ServiceId = b.ServiceId ?? 0,
                    ServiceName = primaryName,
                    ServiceNames = bServiceNames.Count > 1 ? bServiceNames : null,
                    Items = b.Items?.Select(i => new AppointmentItemDto
                    {
                        Id = i.Id,
                        AppointmentId = i.AppointmentId,
                        ServiceId = i.ServiceId,
                        ServiceName = i.ServiceName,
                        Price = i.Price,
                        DurationMinutes = i.DurationMinutes
                    }).ToList() ?? new List<AppointmentItemDto>(),
                    TotalServicesCount = Math.Max(1, bServiceNames.Count),
                    AppointmentDate = b.AppointmentDate.ToString("yyyy-MM-dd"),
                    StartTime = FormatTimeSpan(b.StartTime),
                    EndTime = FormatTimeSpan(b.EndTime),
                    DurationMinutes = b.TotalDurationMinutes > 0 ? b.TotalDurationMinutes : (int)(b.EndTime - b.StartTime).TotalMinutes,
                    Price = b.TotalAmount,
                    Status = (int)b.Status,
                    StatusText = "Acceptance Request",
                    Reason = b.Reason,
                    IsMultiService = bServiceNames.Count > 1,
                    CanManage = true,
                    IsBroadcastRequest = true,
                    CancellationReason = b.CancellationReason,
                    CancelledAt = b.CancelledAt,
                    CancelledByName = ExtractCancelledByName(b.CancellationReason),
                    CreatedBy = b.CreatedBy,
                    CreatedAt = b.CreatedAt,
                    ProposedDate = b.ProposedDate,
                    ProposedStartTime = b.ProposedStartTime.HasValue ? FormatTimeSpan(b.ProposedStartTime.Value) : null,
                    ProposedEndTime = b.ProposedEndTime.HasValue ? FormatTimeSpan(b.ProposedEndTime.Value) : null,
                    RescheduleNotes = b.RescheduleNotes,
                    RescheduledFromAppointmentId = b.RescheduledFromAppointmentId
                };

                allAppointmentDtos.Add(bDto);
            }
        }

        // Group appointments by provider ID
        var appointmentsByProvider = allAppointmentDtos
            .Where(a => a.ServiceProviderId.HasValue)
            .GroupBy(a => a.ServiceProviderId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 7. Calculate Dynamic Grid Bounds based purely on Provider Operating Hours
        int minStartMins = 24 * 60;
        int maxEndMins = 0;
        bool anyProviderAvailable = false;

        var allProviderIds = activeProviders.Select(p => p.Id).ToList();
        var allProviderLinks = await _unitOfWork.Repository<ProviderServiceLink>().Query()
            .AsNoTracking()
            .Where(ps => allProviderIds.Contains(ps.ServiceProviderId))
            .ToListAsync(cancellationToken);
        var providerServicesMap = allProviderLinks
            .GroupBy(ps => ps.ServiceProviderId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ServiceId).Distinct().ToList());

        var providerScheduleList = new List<CalendarProviderScheduleDto>();

        foreach (var p in activeProviders)
        {
            var fullName = p.User != null ? $"{p.User.FirstName} {p.User.LastName}".Trim() : $"Specialist #{p.Id}";
            var pSvcIds = providerServicesMap.TryGetValue(p.Id, out var svcList) ? new List<int>(svcList) : new List<int>();
            if (p.ServiceId.HasValue && !pSvcIds.Contains(p.ServiceId.Value))
            {
                pSvcIds.Add(p.ServiceId.Value);
            }

            var schedDto = new CalendarProviderScheduleDto
            {
                ProviderId = p.Id,
                UserId = p.UserId,
                Name = fullName,
                Email = p.User?.Email,
                PhoneNumber = p.User?.PhoneNumber,
                ProfilePictureUrl = p.User?.ProfilePictureUrl,
                SpecialistType = p.Service?.Name ?? "Specialist",
                Status = (int)p.Status,
                ServiceIds = pSvcIds
            };

            specificAvailByProvider.TryGetValue(p.Id, out var specAv);
            weeklyAvailByProvider.TryGetValue(p.Id, out var weeklyList);

            // Populate all 7 days of the week for this provider
            for (int dayIndex = 0; dayIndex <= 6; dayIndex++)
            {
                var dayEnum = (DayOfWeek)dayIndex;
                var dayRecord = (dayEnum == baseDate.DayOfWeek && specAv != null)
                    ? specAv
                    : weeklyList?.FirstOrDefault(w => w.DayOfWeek == dayEnum);

                if (dayRecord != null && dayRecord.IsAvailable)
                {
                    schedDto.Days.Add(new CalendarDayScheduleDto
                    {
                        DayOfWeek = dayIndex,
                        DayName = dayEnum.ToString(),
                        IsAvailable = true,
                        StartTime = FormatTimeSpan(dayRecord.StartTime),
                        EndTime = FormatTimeSpan(dayRecord.EndTime),
                        BreakStartTime = dayRecord.BreakStartTime.HasValue ? FormatTimeSpan(dayRecord.BreakStartTime.Value) : null,
                        BreakEndTime = dayRecord.BreakEndTime.HasValue ? FormatTimeSpan(dayRecord.BreakEndTime.Value) : null,
                        SlotDurationMinutes = (dayRecord.SlotDurationMinutes > 0 && dayRecord.SlotDurationMinutes != 90) ? dayRecord.SlotDurationMinutes : 30
                    });
                }
                else
                {
                    schedDto.Days.Add(new CalendarDayScheduleDto
                    {
                        DayOfWeek = dayIndex,
                        DayName = dayEnum.ToString(),
                        IsAvailable = false,
                        StartTime = null,
                        EndTime = null,
                        SlotDurationMinutes = 30
                    });
                }
            }

            // Effective availability for baseDate
            var todayWeeklyAv = weeklyList?.FirstOrDefault(w => w.DayOfWeek == baseDate.DayOfWeek);
            var effectiveTodayAv = specAv ?? todayWeeklyAv;

            if (effectiveTodayAv != null && effectiveTodayAv.IsAvailable)
            {
                anyProviderAvailable = true;
                int sMins = (int)effectiveTodayAv.StartTime.TotalMinutes;
                int eMins = (int)effectiveTodayAv.EndTime.TotalMinutes;
                int bStart = effectiveTodayAv.BreakStartTime.HasValue ? (int)effectiveTodayAv.BreakStartTime.Value.TotalMinutes : -1;
                int bEnd = effectiveTodayAv.BreakEndTime.HasValue ? (int)effectiveTodayAv.BreakEndTime.Value.TotalMinutes : -1;

                schedDto.IsAvailableToday = true;
                schedDto.StartTime = FormatTimeSpan(effectiveTodayAv.StartTime);
                schedDto.EndTime = FormatTimeSpan(effectiveTodayAv.EndTime);
                schedDto.BreakStartTime = effectiveTodayAv.BreakStartTime.HasValue ? FormatTimeSpan(effectiveTodayAv.BreakStartTime.Value) : null;
                schedDto.BreakEndTime = effectiveTodayAv.BreakEndTime.HasValue ? FormatTimeSpan(effectiveTodayAv.BreakEndTime.Value) : null;
                schedDto.StartMinutes = sMins;
                schedDto.EndMinutes = eMins;
                schedDto.BreakStartMinutes = bStart;
                schedDto.BreakEndMinutes = bEnd;
                schedDto.WorkingHoursText = $"{FormatTo12Hour(sMins)} – {FormatTo12Hour(eMins)}";

                if (sMins < minStartMins && sMins >= 6 * 60) minStartMins = sMins;
                if (eMins > maxEndMins && eMins <= 23 * 60) maxEndMins = eMins;
            }
            else
            {
                schedDto.IsAvailableToday = false;
                schedDto.WorkingHoursText = "Day Off";
            }

            // Assign this provider's appointments
            if (appointmentsByProvider.TryGetValue(p.Id, out var pApts))
            {
                schedDto.Appointments = pApts;
            }

            providerScheduleList.Add(schedDto);
        }

        // Check today's appointments so no scheduled appointment is visually clipped
        var baseDateStr = baseDate.ToString("yyyy-MM-dd");
        foreach (var apt in allAppointmentDtos)
        {
            if (apt.AppointmentDate == baseDateStr)
            {
                var sParts = apt.StartTime?.Split(':');
                var eParts = apt.EndTime?.Split(':');
                if (sParts?.Length >= 2 && int.TryParse(sParts[0], out var sh) && int.TryParse(sParts[1], out var sm))
                {
                    int aptSMins = sh * 60 + sm;
                    if (aptSMins < minStartMins && aptSMins >= 6 * 60) minStartMins = aptSMins;
                }
                if (eParts?.Length >= 2 && int.TryParse(eParts[0], out var eh) && int.TryParse(eParts[1], out var em))
                {
                    int aptEMins = eh * 60 + em;
                    if (aptEMins > maxEndMins && aptEMins <= 23 * 60) maxEndMins = aptEMins;
                }
            }
        }

        // If no active provider is working today and no appointments, provide a clean default
        if (!anyProviderAvailable && maxEndMins == 0)
        {
            minStartMins = 10 * 60; // 10:00 AM
            maxEndMins = 17 * 60;   // 05:00 PM
        }

        int calculatedStartHour = Math.Max(6, (int)Math.Floor(minStartMins / 60.0));
        int calculatedEndHour = Math.Min(23, (int)Math.Ceiling(maxEndMins / 60.0));
        if (calculatedEndHour <= calculatedStartHour)
        {
            calculatedEndHour = calculatedStartHour + 1;
        }

        // 8. Assemble Full Response Payload
        return new CalendarScheduleResponseDto
        {
            SelectedDate = baseDate.ToString("yyyy-MM-dd"),
            View = filter.View ?? "day",
            RangeStartDate = rangeStart.ToString("yyyy-MM-dd"),
            RangeEndDate = rangeEnd.ToString("yyyy-MM-dd"),
            TenantId = tenantId,
            GridStartHour = calculatedStartHour,
            GridEndHour = calculatedEndHour,
            Providers = providerScheduleList,
            AllAppointments = allAppointmentDtos,
            Permissions = permissions
        };
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}";
    }

    private static string FormatTo12Hour(int totalMinutes)
    {
        int h24 = (totalMinutes / 60) % 24;
        int m = totalMinutes % 60;
        string period = h24 >= 12 ? "PM" : "AM";
        int h12 = h24 % 12;
        if (h12 == 0) h12 = 12;
        return $"{h12:D2}:{m:D2} {period}";
    }

    private static string? ExtractCancelledByName(string? cancellationReason)
    {
        if (string.IsNullOrWhiteSpace(cancellationReason)) return null;
        const string marker = "(Cancelled by ";
        var idx = cancellationReason.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var end = cancellationReason.IndexOf(')', idx);
            if (end > idx)
            {
                return cancellationReason.Substring(idx + marker.Length, end - (idx + marker.Length)).Trim();
            }
        }
        return null;
    }
}
