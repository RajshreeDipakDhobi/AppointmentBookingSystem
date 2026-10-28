using AppointmentBooking.BusinessLayer.Features.Availability.DTOs;
using AppointmentBooking.DomainLayer.Entities;
using AppointmentBooking.InfrastructureLayer.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AppointmentBooking.BusinessLayer.Features.Availability;

public class AvailabilityService : IAvailabilityService
{
    private readonly IUnitOfWork _unitOfWork;

    public AvailabilityService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ProviderAvailabilityOverviewResponseDto> GetAvailabilityOverviewAsync(
        int tenantId, 
        CancellationToken cancellationToken = default)
    {
        var providers = await _unitOfWork.Repository<ServiceProvider>()
            .Query()
            .Include(p => p.User)
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.User.FirstName)
            .ToListAsync(cancellationToken);

        var allAvailabilities = await _unitOfWork.Repository<ProviderAvailability>()
            .Query()
            .Where(pa => pa.TenantId == tenantId && pa.SpecificDate == null)
            .ToListAsync(cancellationToken);

        var orderedDays = new[]
        {
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday
        };

        var providerItems = new List<ProviderScheduleOverviewItemDto>();

        foreach (var provider in providers)
        {
            var providerRecords = allAvailabilities.Where(pa => pa.ServiceProviderId == provider.Id).ToList();
            var daySummaries = new List<DayScheduleSummaryDto>();

            foreach (var day in orderedDays)
            {
                var match = providerRecords.FirstOrDefault(r => r.DayOfWeek == day);
                if (match != null && match.IsAvailable)
                {
                    var blockedList = !string.IsNullOrWhiteSpace(match.BlockedSlots)
                        ? match.BlockedSlots.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        : null;
                    var slots = CalculateSlots(match.StartTime, match.EndTime, match.SlotDurationMinutes, match.BreakStartTime, match.BreakEndTime, blockedList);
                    var bookableCount = slots.Count(s => !s.IsBreak);

                    var startTimeStr = $"{match.StartTime.Hours:D2}:{match.StartTime.Minutes:D2}";
                    var endTimeStr = $"{match.EndTime.Hours:D2}:{match.EndTime.Minutes:D2}";
                    string? breakStartStr = match.BreakStartTime.HasValue ? $"{match.BreakStartTime.Value.Hours:D2}:{match.BreakStartTime.Value.Minutes:D2}" : null;
                    string? breakEndStr = match.BreakEndTime.HasValue ? $"{match.BreakEndTime.Value.Hours:D2}:{match.BreakEndTime.Value.Minutes:D2}" : null;

                    var timeDisp = $"{Format12Hour(match.StartTime)} – {Format12Hour(match.EndTime)}";
                    var breakDisp = (match.BreakStartTime.HasValue && match.BreakEndTime.HasValue) 
                        ? $"{Format12Hour(match.BreakStartTime.Value)} – {Format12Hour(match.BreakEndTime.Value)}" 
                        : "Continuous";

                    daySummaries.Add(new DayScheduleSummaryDto
                    {
                        DayOfWeek = day,
                        IsAvailable = true,
                        StartTime = startTimeStr,
                        EndTime = endTimeStr,
                        BreakStartTime = breakStartStr,
                        BreakEndTime = breakEndStr,
                        SlotDurationMinutes = match.SlotDurationMinutes,
                        BlockedSlots = match.BlockedSlots,
                        BlockedSlotList = blockedList?.ToList(),
                        TotalCalculatedSlots = bookableCount,
                        TimeDisplay = timeDisp,
                        BreakDisplay = breakDisp
                    });
                }
                else
                {
                    daySummaries.Add(new DayScheduleSummaryDto
                    {
                        DayOfWeek = day,
                        IsAvailable = false,
                        StartTime = null,
                        EndTime = null,
                        BreakStartTime = null,
                        BreakEndTime = null,
                        SlotDurationMinutes = match?.SlotDurationMinutes ?? 30,
                        TotalCalculatedSlots = 0,
                        TimeDisplay = "Off",
                        BreakDisplay = "None"
                    });
                }
            }

            var activeDays = daySummaries.Where(d => d.IsAvailable).ToList();
            bool isConfigured = activeDays.Any();
            int totalWeeklySlots = activeDays.Sum(d => d.TotalCalculatedSlots);
            int slotDuration = activeDays.FirstOrDefault()?.SlotDurationMinutes ?? 30;

            string activeDaysSummary = "Not Configured";
            string workingHoursSummary = "Not Set";
            string breakSummary = "None";

            if (isConfigured)
            {
                if (activeDays.Count == 7)
                {
                    activeDaysSummary = "All 7 Days";
                }
                else if (activeDays.Count == 6 && !activeDays.Any(d => d.DayOfWeek == DayOfWeek.Sunday))
                {
                    activeDaysSummary = "Mon – Sat";
                }
                else if (activeDays.Count == 5 && !activeDays.Any(d => d.DayOfWeek == DayOfWeek.Sunday || d.DayOfWeek == DayOfWeek.Saturday))
                {
                    activeDaysSummary = "Mon – Fri";
                }
                else
                {
                    activeDaysSummary = string.Join(", ", activeDays.Select(d => d.ShortName));
                }

                // Check if all active days share identical start and end time
                var distinctTimings = activeDays.Select(d => d.TimeDisplay).Distinct().ToList();
                if (distinctTimings.Count == 1)
                {
                    workingHoursSummary = distinctTimings[0];
                }
                else
                {
                    workingHoursSummary = "Custom per day";
                }

                // Break summary
                var distinctBreaks = activeDays.Select(d => d.BreakDisplay).Distinct().ToList();
                if (distinctBreaks.Count == 1)
                {
                    breakSummary = distinctBreaks[0];
                }
                else
                {
                    breakSummary = "Varies per day";
                }
            }

            providerItems.Add(new ProviderScheduleOverviewItemDto
            {
                ProviderId = provider.Id,
                ProviderName = provider.Name,
                Specialization = provider.ServiceType,
                Email = provider.Email,
                PhoneNumber = provider.PhoneNumber,
                ProfilePictureUrl = provider.ProfilePictureUrl,
                IsConfigured = isConfigured,
                ActiveDaysCount = activeDays.Count,
                ActiveDaysSummary = activeDaysSummary,
                WorkingHoursSummary = workingHoursSummary,
                BreakSummary = breakSummary,
                SlotDurationMinutes = slotDuration,
                TotalWeeklySlots = totalWeeklySlots,
                Days = daySummaries
            });
        }

        return new ProviderAvailabilityOverviewResponseDto
        {
            TotalProviders = providers.Count,
            ConfiguredProvidersCount = providerItems.Count(p => p.IsConfigured),
            TotalWeeklySlots = providerItems.Sum(p => p.TotalWeeklySlots),
            ActiveDaysTotal = providerItems.Sum(p => p.ActiveDaysCount),
            Providers = providerItems
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

    public async Task<IReadOnlyList<ProviderAvailabilityDto>?> GetProviderWeeklyAvailabilityAsync(
        int providerId, 
        int tenantId, 
        CancellationToken cancellationToken = default)
    {
        var provider = await _unitOfWork.Repository<ServiceProvider>()
            .Query()
            .FirstOrDefaultAsync(p => p.Id == providerId && p.TenantId == tenantId, cancellationToken);

        if (provider == null)
        {
            return null;
        }

        var existingRecords = await _unitOfWork.Repository<ProviderAvailability>()
            .Query()
            .Where(pa => pa.ServiceProviderId == providerId && pa.TenantId == tenantId && pa.SpecificDate == null)
            .ToListAsync(cancellationToken);

        // Days of week ordered Monday (1) through Sunday (0)
        var orderedDays = new[]
        {
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday
        };

        var result = new List<ProviderAvailabilityDto>();

        foreach (var day in orderedDays)
        {
            var match = existingRecords.FirstOrDefault(r => r.DayOfWeek == day);
            if (match != null)
            {
                var blockedList = !string.IsNullOrWhiteSpace(match.BlockedSlots)
                    ? match.BlockedSlots.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : null;
                var slots = CalculateSlots(match.StartTime, match.EndTime, match.SlotDurationMinutes, match.BreakStartTime, match.BreakEndTime, blockedList);
                result.Add(new ProviderAvailabilityDto
                {
                    Id = match.Id,
                    ServiceProviderId = providerId,
                    ServiceProviderName = provider.Name,
                    DayOfWeek = match.DayOfWeek,
                    SpecificDate = match.SpecificDate,
                    StartTime = $"{match.StartTime.Hours:D2}:{match.StartTime.Minutes:D2}",
                    EndTime = $"{match.EndTime.Hours:D2}:{match.EndTime.Minutes:D2}",
                    BreakStartTime = match.BreakStartTime.HasValue ? $"{match.BreakStartTime.Value.Hours:D2}:{match.BreakStartTime.Value.Minutes:D2}" : null,
                    BreakEndTime = match.BreakEndTime.HasValue ? $"{match.BreakEndTime.Value.Hours:D2}:{match.BreakEndTime.Value.Minutes:D2}" : null,
                    SlotDurationMinutes = match.SlotDurationMinutes,
                    BlockedSlots = match.BlockedSlots,
                    IsAvailable = match.IsAvailable,
                    TotalCalculatedSlots = match.IsAvailable ? slots.Count(s => !s.IsBreak && !s.IsBlocked) : 0
                });
            }
            else
            {
                result.Add(new ProviderAvailabilityDto
                {
                    Id = 0,
                    ServiceProviderId = providerId,
                    ServiceProviderName = provider.Name,
                    DayOfWeek = day,
                    SpecificDate = null,
                    StartTime = null,
                    EndTime = null,
                    BreakStartTime = null,
                    BreakEndTime = null,
                    SlotDurationMinutes = 30,
                    BlockedSlots = null,
                    IsAvailable = false,
                    TotalCalculatedSlots = 0
                });
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<ProviderAvailabilityDto>?> SaveWeeklyAvailabilityAsync(
        int tenantId, 
        SaveWeeklyAvailabilityRequestDto request, 
        string userEmail, 
        CancellationToken cancellationToken = default)
    {
        var provider = await _unitOfWork.Repository<ServiceProvider>()
            .Query()
            .FirstOrDefaultAsync(p => p.Id == request.ServiceProviderId && p.TenantId == tenantId, cancellationToken);

        if (provider == null)
        {
            return null;
        }

        var existingRecords = await _unitOfWork.Repository<ProviderAvailability>()
            .Query()
            .Where(pa => pa.ServiceProviderId == request.ServiceProviderId && pa.TenantId == tenantId && pa.SpecificDate == null)
            .ToListAsync(cancellationToken);

        foreach (var schedule in request.Schedules)
        {
            if (!TimeSpan.TryParse(schedule.StartTime, out var startTime))
            {
                startTime = new TimeSpan(10, 0, 0);
            }

            if (!TimeSpan.TryParse(schedule.EndTime, out var endTime))
            {
                endTime = new TimeSpan(17, 0, 0);
            }

            TimeSpan? breakStartTime = null;
            if (!string.IsNullOrWhiteSpace(schedule.BreakStartTime) && TimeSpan.TryParse(schedule.BreakStartTime, out var bStart))
            {
                breakStartTime = bStart;
            }

            TimeSpan? breakEndTime = null;
            if (!string.IsNullOrWhiteSpace(schedule.BreakEndTime) && TimeSpan.TryParse(schedule.BreakEndTime, out var bEnd))
            {
                breakEndTime = bEnd;
            }

            string? blockedSlotsStr = schedule.BlockedSlots != null && schedule.BlockedSlots.Count > 0
                ? string.Join(",", schedule.BlockedSlots)
                : null;

            var existing = existingRecords.FirstOrDefault(r => r.DayOfWeek == schedule.DayOfWeek && r.SpecificDate == null);

            if (existing != null)
            {
                existing.StartTime = startTime;
                existing.EndTime = endTime;
                existing.BreakStartTime = breakStartTime;
                existing.BreakEndTime = breakEndTime;
                existing.SlotDurationMinutes = schedule.SlotDurationMinutes;
                existing.BlockedSlots = blockedSlotsStr;
                existing.IsAvailable = schedule.IsAvailable;
                existing.UpdatedAt = DateTime.UtcNow;

                _unitOfWork.Repository<ProviderAvailability>().Update(existing);
            }
            else
            {
                var newRecord = new ProviderAvailability
                {
                    TenantId = tenantId,
                    ServiceProviderId = request.ServiceProviderId,
                    DayOfWeek = schedule.DayOfWeek,
                    SpecificDate = null,
                    StartTime = startTime,
                    EndTime = endTime,
                    BreakStartTime = breakStartTime,
                    BreakEndTime = breakEndTime,
                    SlotDurationMinutes = schedule.SlotDurationMinutes,
                    BlockedSlots = blockedSlotsStr,
                    IsAvailable = schedule.IsAvailable,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = userEmail
                };

                await _unitOfWork.Repository<ProviderAvailability>().AddAsync(newRecord, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetProviderWeeklyAvailabilityAsync(request.ServiceProviderId, tenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderAvailabilityDto>?> GetProviderSpecificDatesAsync(
        int providerId, 
        int tenantId, 
        CancellationToken cancellationToken = default)
    {
        var provider = await _unitOfWork.Repository<ServiceProvider>()
            .Query()
            .FirstOrDefaultAsync(p => p.Id == providerId && p.TenantId == tenantId, cancellationToken);

        if (provider == null)
        {
            return null;
        }

        var records = await _unitOfWork.Repository<ProviderAvailability>()
            .Query()
            .Where(pa => pa.ServiceProviderId == providerId && pa.TenantId == tenantId && pa.SpecificDate != null)
            .OrderBy(pa => pa.SpecificDate)
            .ToListAsync(cancellationToken);

        var result = new List<ProviderAvailabilityDto>();
        foreach (var r in records)
        {
            var blockedList = !string.IsNullOrWhiteSpace(r.BlockedSlots)
                ? r.BlockedSlots.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;
            var slots = CalculateSlots(r.StartTime, r.EndTime, r.SlotDurationMinutes, r.BreakStartTime, r.BreakEndTime, blockedList);

            result.Add(new ProviderAvailabilityDto
            {
                Id = r.Id,
                ServiceProviderId = providerId,
                ServiceProviderName = provider.Name,
                DayOfWeek = r.DayOfWeek,
                SpecificDate = r.SpecificDate,
                StartTime = $"{r.StartTime.Hours:D2}:{r.StartTime.Minutes:D2}",
                EndTime = $"{r.EndTime.Hours:D2}:{r.EndTime.Minutes:D2}",
                BreakStartTime = r.BreakStartTime.HasValue ? $"{r.BreakStartTime.Value.Hours:D2}:{r.BreakStartTime.Value.Minutes:D2}" : null,
                BreakEndTime = r.BreakEndTime.HasValue ? $"{r.BreakEndTime.Value.Hours:D2}:{r.BreakEndTime.Value.Minutes:D2}" : null,
                SlotDurationMinutes = r.SlotDurationMinutes,
                BlockedSlots = r.BlockedSlots,
                IsAvailable = r.IsAvailable,
                TotalCalculatedSlots = r.IsAvailable ? slots.Count(s => !s.IsBreak && !s.IsBlocked) : 0
            });
        }

        return result;
    }

    public async Task<ProviderAvailabilityDto?> SaveDateSpecificAvailabilityAsync(
        int tenantId, 
        SaveDateSpecificAvailabilityRequestDto request, 
        string userEmail, 
        CancellationToken cancellationToken = default)
    {
        var provider = await _unitOfWork.Repository<ServiceProvider>()
            .Query()
            .FirstOrDefaultAsync(p => p.Id == request.ServiceProviderId && p.TenantId == tenantId, cancellationToken);

        if (provider == null)
        {
            return null;
        }

        if (!TimeSpan.TryParse(request.StartTime, out var startTime))
        {
            startTime = new TimeSpan(10, 0, 0);
        }

        if (!TimeSpan.TryParse(request.EndTime, out var endTime))
        {
            endTime = new TimeSpan(17, 0, 0);
        }

        TimeSpan? breakStartTime = null;
        if (!string.IsNullOrWhiteSpace(request.BreakStartTime) && TimeSpan.TryParse(request.BreakStartTime, out var bStart))
        {
            breakStartTime = bStart;
        }

        TimeSpan? breakEndTime = null;
        if (!string.IsNullOrWhiteSpace(request.BreakEndTime) && TimeSpan.TryParse(request.BreakEndTime, out var bEnd))
        {
            breakEndTime = bEnd;
        }

        string? blockedSlotsStr = request.BlockedSlots != null && request.BlockedSlots.Count > 0
            ? string.Join(",", request.BlockedSlots)
            : null;

        var targetDate = request.Date.Date;

        var existing = await _unitOfWork.Repository<ProviderAvailability>()
            .Query()
            .FirstOrDefaultAsync(pa => pa.ServiceProviderId == request.ServiceProviderId 
                                    && pa.TenantId == tenantId 
                                    && pa.SpecificDate.HasValue 
                                    && pa.SpecificDate.Value.Date == targetDate, cancellationToken);

        if (existing != null)
        {
            existing.DayOfWeek = targetDate.DayOfWeek;
            existing.StartTime = startTime;
            existing.EndTime = endTime;
            existing.BreakStartTime = breakStartTime;
            existing.BreakEndTime = breakEndTime;
            existing.SlotDurationMinutes = request.SlotDurationMinutes;
            existing.BlockedSlots = blockedSlotsStr;
            existing.IsAvailable = request.IsAvailable;
            existing.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.Repository<ProviderAvailability>().Update(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var blockedList = !string.IsNullOrWhiteSpace(existing.BlockedSlots)
                ? existing.BlockedSlots.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;
            var slots = CalculateSlots(existing.StartTime, existing.EndTime, existing.SlotDurationMinutes, existing.BreakStartTime, existing.BreakEndTime, blockedList);

            return new ProviderAvailabilityDto
            {
                Id = existing.Id,
                ServiceProviderId = request.ServiceProviderId,
                ServiceProviderName = provider.Name,
                DayOfWeek = existing.DayOfWeek,
                SpecificDate = existing.SpecificDate,
                StartTime = $"{existing.StartTime.Hours:D2}:{existing.StartTime.Minutes:D2}",
                EndTime = $"{existing.EndTime.Hours:D2}:{existing.EndTime.Minutes:D2}",
                BreakStartTime = existing.BreakStartTime.HasValue ? $"{existing.BreakStartTime.Value.Hours:D2}:{existing.BreakStartTime.Value.Minutes:D2}" : null,
                BreakEndTime = existing.BreakEndTime.HasValue ? $"{existing.BreakEndTime.Value.Hours:D2}:{existing.BreakEndTime.Value.Minutes:D2}" : null,
                SlotDurationMinutes = existing.SlotDurationMinutes,
                BlockedSlots = existing.BlockedSlots,
                IsAvailable = existing.IsAvailable,
                TotalCalculatedSlots = existing.IsAvailable ? slots.Count(s => !s.IsBreak && !s.IsBlocked) : 0
            };
        }
        else
        {
            var newRecord = new ProviderAvailability
            {
                TenantId = tenantId,
                ServiceProviderId = request.ServiceProviderId,
                DayOfWeek = targetDate.DayOfWeek,
                SpecificDate = targetDate,
                StartTime = startTime,
                EndTime = endTime,
                BreakStartTime = breakStartTime,
                BreakEndTime = breakEndTime,
                SlotDurationMinutes = request.SlotDurationMinutes,
                BlockedSlots = blockedSlotsStr,
                IsAvailable = request.IsAvailable,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userEmail
            };

            await _unitOfWork.Repository<ProviderAvailability>().AddAsync(newRecord, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var blockedList = !string.IsNullOrWhiteSpace(newRecord.BlockedSlots)
                ? newRecord.BlockedSlots.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;
            var slots = CalculateSlots(newRecord.StartTime, newRecord.EndTime, newRecord.SlotDurationMinutes, newRecord.BreakStartTime, newRecord.BreakEndTime, blockedList);

            return new ProviderAvailabilityDto
            {
                Id = newRecord.Id,
                ServiceProviderId = request.ServiceProviderId,
                ServiceProviderName = provider.Name,
                DayOfWeek = newRecord.DayOfWeek,
                SpecificDate = newRecord.SpecificDate,
                StartTime = $"{newRecord.StartTime.Hours:D2}:{newRecord.StartTime.Minutes:D2}",
                EndTime = $"{newRecord.EndTime.Hours:D2}:{newRecord.EndTime.Minutes:D2}",
                BreakStartTime = newRecord.BreakStartTime.HasValue ? $"{newRecord.BreakStartTime.Value.Hours:D2}:{newRecord.BreakStartTime.Value.Minutes:D2}" : null,
                BreakEndTime = newRecord.BreakEndTime.HasValue ? $"{newRecord.BreakEndTime.Value.Hours:D2}:{newRecord.BreakEndTime.Value.Minutes:D2}" : null,
                SlotDurationMinutes = newRecord.SlotDurationMinutes,
                BlockedSlots = newRecord.BlockedSlots,
                IsAvailable = newRecord.IsAvailable,
                TotalCalculatedSlots = newRecord.IsAvailable ? slots.Count(s => !s.IsBreak && !s.IsBlocked) : 0
            };
        }
    }

    public async Task<ProviderAvailabilityDto?> GetProviderAvailabilityForDateAsync(
        int providerId, 
        int tenantId, 
        DateTime date, 
        CancellationToken cancellationToken = default)
    {
        var provider = await _unitOfWork.Repository<ServiceProvider>()
            .Query()
            .FirstOrDefaultAsync(p => p.Id == providerId && p.TenantId == tenantId, cancellationToken);

        if (provider == null)
        {
            return null;
        }

        var targetDate = date.Date;

        // 1. Check if a date-specific override exists
        var specificMatch = await _unitOfWork.Repository<ProviderAvailability>()
            .Query()
            .FirstOrDefaultAsync(pa => pa.ServiceProviderId == providerId 
                                    && pa.TenantId == tenantId 
                                    && pa.SpecificDate.HasValue 
                                    && pa.SpecificDate.Value.Date == targetDate, cancellationToken);

        var match = specificMatch;
        if (match == null)
        {
            // 2. Fallback to weekly schedule for this day of week
            match = await _unitOfWork.Repository<ProviderAvailability>()
                .Query()
                .FirstOrDefaultAsync(pa => pa.ServiceProviderId == providerId 
                                        && pa.TenantId == tenantId 
                                        && pa.SpecificDate == null 
                                        && pa.DayOfWeek == targetDate.DayOfWeek, cancellationToken);
        }

        if (match == null)
        {
            return new ProviderAvailabilityDto
            {
                Id = 0,
                ServiceProviderId = providerId,
                ServiceProviderName = provider.Name,
                DayOfWeek = targetDate.DayOfWeek,
                SpecificDate = targetDate,
                StartTime = null,
                EndTime = null,
                BreakStartTime = null,
                BreakEndTime = null,
                SlotDurationMinutes = 30,
                BlockedSlots = null,
                IsAvailable = false,
                TotalCalculatedSlots = 0
            };
        }

        var blockedList = !string.IsNullOrWhiteSpace(match.BlockedSlots)
            ? match.BlockedSlots.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;
        var slots = CalculateSlots(match.StartTime, match.EndTime, match.SlotDurationMinutes, match.BreakStartTime, match.BreakEndTime, blockedList);

        return new ProviderAvailabilityDto
        {
            Id = match.Id,
            ServiceProviderId = providerId,
            ServiceProviderName = provider.Name,
            DayOfWeek = match.DayOfWeek,
            SpecificDate = match.SpecificDate ?? targetDate,
            StartTime = $"{match.StartTime.Hours:D2}:{match.StartTime.Minutes:D2}",
            EndTime = $"{match.EndTime.Hours:D2}:{match.EndTime.Minutes:D2}",
            BreakStartTime = match.BreakStartTime.HasValue ? $"{match.BreakStartTime.Value.Hours:D2}:{match.BreakStartTime.Value.Minutes:D2}" : null,
            BreakEndTime = match.BreakEndTime.HasValue ? $"{match.BreakEndTime.Value.Hours:D2}:{match.BreakEndTime.Value.Minutes:D2}" : null,
            SlotDurationMinutes = match.SlotDurationMinutes,
            BlockedSlots = match.BlockedSlots,
            IsAvailable = match.IsAvailable,
            TotalCalculatedSlots = match.IsAvailable ? slots.Count(s => !s.IsBreak && !s.IsBlocked) : 0
        };
    }

    public IReadOnlyList<TimeSlotDto> CalculateSlots(
        TimeSpan startTime, 
        TimeSpan endTime, 
        int slotDurationMinutes, 
        TimeSpan? breakStartTime = null, 
        TimeSpan? breakEndTime = null,
        IEnumerable<string>? blockedSlots = null)
    {
        var slots = new List<TimeSlotDto>();
        if (slotDurationMinutes <= 0 || endTime <= startTime)
        {
            return slots;
        }

        var current = startTime;
        var duration = TimeSpan.FromMinutes(slotDurationMinutes);
        bool hasBreak = breakStartTime.HasValue && breakEndTime.HasValue && breakEndTime > breakStartTime;

        var blockedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (blockedSlots != null)
        {
            foreach (var b in blockedSlots)
            {
                if (!string.IsNullOrWhiteSpace(b))
                {
                    blockedSet.Add(b.Trim());
                }
            }
        }

        while (current + duration <= endTime)
        {
            var slotEnd = current + duration;
            string startStr = $"{current.Hours:D2}:{current.Minutes:D2}";
            string endStr = $"{slotEnd.Hours:D2}:{slotEnd.Minutes:D2}";

            bool isSlotInBreak = hasBreak && (current < breakEndTime!.Value && slotEnd > breakStartTime!.Value);
            bool isBlocked = blockedSet.Contains(startStr);

            slots.Add(new TimeSlotDto
            {
                StartTime = startStr,
                EndTime = endStr,
                IsAvailable = !isSlotInBreak && !isBlocked,
                IsBreak = isSlotInBreak,
                IsBlocked = isBlocked,
                Reason = isSlotInBreak ? "Lunch / Rest Break" : (isBlocked ? "Manually Blocked" : null)
            });
            current = slotEnd;
        }

        return slots;
    }

    public async Task<bool> RemoveAvailabilityAsync(int availabilityId, int tenantId, CancellationToken cancellationToken = default)
    {
        var record = await _unitOfWork.Repository<ProviderAvailability>()
            .FindAsync(pa => pa.Id == availabilityId && pa.TenantId == tenantId, cancellationToken);

        if (record == null)
        {
            return false;
        }

        _unitOfWork.Repository<ProviderAvailability>().Remove(record);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
