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

    public async Task<ProviderDailyAvailabilityResponseDto?> GetAvailableSlotsAsync(int providerId, DateTime date, CancellationToken cancellationToken = default)
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

        var response = new ProviderDailyAvailabilityResponseDto
        {
            ServiceProviderId = provider.Id,
            ServiceProviderName = provider.User != null ? $"{provider.User.FirstName} {provider.User.LastName}".Trim() : string.Empty,
            ServiceType = provider.Service?.Name ?? string.Empty,
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
                     && a.Status != AppointmentStatus.Rescheduled
                     && a.Status != AppointmentStatus.Rejected)
            .ToListAsync(cancellationToken);

        // Calculate time slot intervals using timezone-aware business local time
        var now = _dateTimeProvider.GetCurrentLocalTime();
        var currentSlotTime = availability.StartTime;
        var duration = TimeSpan.FromMinutes(slotDurationMins);

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
            currentSlotTime = nextSlotTime;
        }

        return response;
    }

    public async Task<ProviderDailyAvailabilityResponseDto?> GetAvailableSlotsByServiceAsync(
        int serviceId, 
        DateTime date, 
        int? tenantId, 
        CancellationToken cancellationToken = default)
    {
        var service = await _unitOfWork.Repository<Service>().Query()
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.IsActive, cancellationToken);

        if (service == null)
        {
            return null;
        }

        int slotDurationMins = service.DurationMinutes > 0 ? service.DurationMinutes : 30;

        var response = new ProviderDailyAvailabilityResponseDto
        {
            ServiceProviderId = 0,
            ServiceProviderName = "All Available Specialists",
            ServiceType = service.Name,
            Date = date.Date,
            DayName = date.DayOfWeek.ToString(),
            IsWorkingDay = false,
            WorkingHours = "Closed",
            SlotDurationMinutes = slotDurationMins,
            Slots = new List<AvailableSlotDto>()
        };

        // Fetch all active specialists offering this service
        var providersQuery = _unitOfWork.Repository<ServiceProvider>().Query()
            .Include(p => p.User)
            .Where(p => p.ServiceId == serviceId && p.Status == ProviderStatus.Active);

        if (tenantId.HasValue && tenantId.Value > 0)
        {
            providersQuery = providersQuery.Where(p => p.TenantId == tenantId.Value);
        }

        var providers = await providersQuery.ToListAsync(cancellationToken);
        if (providers.Count == 0)
        {
            response.WorkingHours = "No specialists assigned";
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
                     && a.Status != AppointmentStatus.Rescheduled
                     && a.Status != AppointmentStatus.Rejected)
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

            currentSlotTime = nextSlotTime;
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

        if (isDirectProviderBooking)
        {
            // 1. Direct Specialist Path
            provider = await _unitOfWork.Repository<ServiceProvider>()
                .Query()
                .Include(p => p.User)
                .Include(p => p.Service)
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
            // 2. Urban Company Broadcast Path: Validate Selected Service
            if (!request.ServiceId.HasValue || request.ServiceId.Value <= 0)
            {
                return ApiResponse<AppointmentDto>.Fail("Please select a valid service to book.");
            }

            service = await _unitOfWork.Repository<Service>().Query()
                .FirstOrDefaultAsync(s => s.Id == request.ServiceId.Value && s.IsActive, cancellationToken);

            if (service == null)
            {
                return ApiResponse<AppointmentDto>.Fail("Selected service is inactive or not found.");
            }

            var specialistsQuery = _unitOfWork.Repository<ServiceProvider>().Query()
                .Where(p => p.ServiceId == service.Id && p.Status == ProviderStatus.Active);

            if (tenantId.HasValue && tenantId.Value > 0)
            {
                specialistsQuery = specialistsQuery.Where(p => p.TenantId == tenantId.Value);
            }

            var activeSpecialists = await specialistsQuery.ToListAsync(cancellationToken);

            if (activeSpecialists.Count == 0)
            {
                return ApiResponse<AppointmentDto>.Fail("No active specialists are currently assigned to this service.");
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
                                && a.Status != AppointmentStatus.Rejected
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
                ServiceId = request.ServiceId ?? provider?.ServiceId ?? service?.Id,
                CustomerId = customerId.Value,
                Reason = request.Reason?.Trim(),
                AppointmentDate = appointmentDate,
                StartTime = startTime,
                EndTime = endTime,
                Status = (isDirectProviderBooking && isBookedByProvider) ? AppointmentStatus.Approved : AppointmentStatus.Booked,
                ApprovedAt = (isDirectProviderBooking && isBookedByProvider) ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = !string.IsNullOrWhiteSpace(createdByName) 
                    ? createdByName.Trim() 
                    : (isBookedByProvider ? "Service Provider" : $"{customerUser.FirstName} {customerUser.LastName}".Trim())
            };

            await _unitOfWork.Repository<Appointment>().AddAsync(appointment);
            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();

            // If a new customer account was created during booking flow, dispatch credentials email
            if (isNewCustomerAccount && !string.IsNullOrWhiteSpace(customerUser?.Email))
            {
                try
                {
                    var customerFullName = $"{customerUser.FirstName} {customerUser.LastName}".Trim();
                    await _emailService.SendAccountCreatedEmailAsync(
                        customerUser.Email,
                        customerFullName,
                        defaultPassword);
                }
                catch (Exception emailEx)
                {
                    _logger.LogError(emailEx, "Failed to send welcome credentials email to newly registered Customer {Email} during booking flow", customerUser.Email);
                }
            }

            // Load navigation properties for return
            var created = await _unitOfWork.Repository<Appointment>().Query()
                .Include(a => a.ServiceProvider).ThenInclude(s => s!.User)
                .Include(a => a.Service)
                .Include(a => a.Customer)
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

    public async Task<ApiResponse<AppointmentDto>> RescheduleAppointmentAsync(
        int appointmentId, 
        int? tenantId, 
        int? customerId, 
        bool isAdmin, 
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
        var authError = VerifyAppointmentOwnership(appointment, customerId, isAdmin, request.CustomerContact);
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
                            && a.Status != AppointmentStatus.Rejected
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
            await _unitOfWork.SaveChangesAsync();
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
        bool isAdmin, 
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
        var authError = VerifyAppointmentOwnership(appointment, customerId, isAdmin, request.CustomerContact);
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
            else if (isAdmin)
            {
                canceller = "Administrator";
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



    public async Task<List<AppointmentDto>> GetMyAppointmentsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var appointments = await _unitOfWork.Repository<Appointment>().Query()
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp.User)
            .Include(a => a.Customer)
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        return _mapper.Map<List<AppointmentDto>>(appointments);
    }

    public async Task<PagedResult<AppointmentDto>> GetMyAppointmentsPagedAsync(
        int customerId, 
        PaginationParams paginationParams, 
        AppointmentStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Appointment>().Query()
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp.User)
            .Include(a => a.Customer)
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

        var totalCount = await query.CountAsync(cancellationToken);

        var appointments = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((paginationParams.PageNumber - 1) * paginationParams.PageSize)
            .Take(paginationParams.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<List<AppointmentDto>>(appointments);
        return new PagedResult<AppointmentDto>(dtos, totalCount, paginationParams.PageNumber, paginationParams.PageSize);
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
        var query = _unitOfWork.Repository<Appointment>().Query()
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp.User)
            .Include(a => a.Customer)
            .Where(a => a.TenantId == tenantId);

        if (providerId.HasValue && providerId.Value > 0)
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

        return _mapper.Map<List<AppointmentDto>>(appointments);
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
        var query = _unitOfWork.Repository<Appointment>().Query()
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp.User)
            .Include(a => a.Customer)
            .Where(a => a.TenantId == tenantId);

        if (providerId.HasValue && providerId.Value > 0)
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

        var totalCount = await query.CountAsync(cancellationToken);

        var appointments = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((paginationParams.PageNumber - 1) * paginationParams.PageSize)
            .Take(paginationParams.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<List<AppointmentDto>>(appointments);
        return new PagedResult<AppointmentDto>(dtos, totalCount, paginationParams.PageNumber, paginationParams.PageSize);
    }

    public async Task<AppointmentDto?> GetAppointmentByIdAsync(int appointmentId, int tenantId, CancellationToken cancellationToken = default)
    {
        var appointment = await _unitOfWork.Repository<Appointment>().Query()
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp.User)
            .Include(a => a.Customer)
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

            if (appointment.Status == AppointmentStatus.Rejected && request.Status != AppointmentStatus.Rejected)
            {
                return ApiResponse<AppointmentDto>.Fail("Cannot change the status of an already rejected appointment.");
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

                case AppointmentStatus.Rejected:
                    appointment.RejectedAt = DateTime.UtcNow;
                    appointment.RejectionReason = !string.IsNullOrWhiteSpace(request.Notes)
                        ? request.Notes.Trim()
                        : "Rejected by service provider";
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
                                && a.Status != AppointmentStatus.Rejected
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

    public async Task<List<AppointmentDto>> GetIncomingBroadcastRequestsAsync(
        int? providerId, 
        int? tenantId, 
        CancellationToken cancellationToken = default)
    {
        var today = _dateTimeProvider.Today;
        var nowTime = _dateTimeProvider.GetCurrentLocalTime().TimeOfDay;

        // Base query for unassigned broadcast appointments
        var query = _unitOfWork.Repository<Appointment>().Query()
            .Include(a => a.Service)
            .Include(a => a.Customer)
            .Include(a => a.ServiceProvider).ThenInclude(sp => sp!.User)
            .Where(a => a.ServiceProviderId == null 
                     && a.Status == AppointmentStatus.Booked
                     && a.AppointmentDate.Date >= today);

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

            // Only show requests for the service category the specialist is assigned to
            if (provider.ServiceId.HasValue)
            {
                query = query.Where(a => a.ServiceId == provider.ServiceId.Value);
            }

            // Fetch provider's active bookings to avoid showing conflicting slots
            var providerBookings = await _unitOfWork.Repository<Appointment>().Query()
                .Where(a => a.ServiceProviderId == providerId.Value
                         && a.AppointmentDate.Date >= today
                         && a.Status != AppointmentStatus.Cancelled
                         && a.Status != AppointmentStatus.Rescheduled
                         && a.Status != AppointmentStatus.Rejected)
                .Select(a => new { a.AppointmentDate, a.StartTime, a.EndTime })
                .ToListAsync(cancellationToken);

            var openRequests = await query
                .OrderBy(a => a.AppointmentDate)
                .ThenBy(a => a.StartTime)
                .ToListAsync(cancellationToken);

            // Filter out past slots for today and any slots where provider is already busy
            var validRequests = openRequests.Where(a =>
            {
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

            if (appointment.Status == AppointmentStatus.Cancelled || appointment.Status == AppointmentStatus.Rejected)
            {
                await _unitOfWork.RollbackTransactionAsync();
                return ApiResponse<AppointmentDto>.Fail("This appointment has already been cancelled.");
            }

            if (appointment.ServiceId.HasValue && provider.ServiceId.HasValue && appointment.ServiceId.Value != provider.ServiceId.Value)
            {
                await _unitOfWork.RollbackTransactionAsync();
                return ApiResponse<AppointmentDto>.Fail("You are not assigned to perform this service category.");
            }

            // Verify no time conflict with this provider's existing schedule
            bool hasConflict = await _unitOfWork.Repository<Appointment>().Query()
                .AnyAsync(a => a.ServiceProviderId == providerId
                            && a.AppointmentDate.Date == appointment.AppointmentDate.Date
                            && a.Status != AppointmentStatus.Cancelled
                            && a.Status != AppointmentStatus.Rescheduled
                            && a.Status != AppointmentStatus.Rejected
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

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync();

            // Reload with relations for DTO
            var updated = await _unitOfWork.Repository<Appointment>().Query()
                .Include(a => a.ServiceProvider).ThenInclude(sp => sp!.User)
                .Include(a => a.Service)
                .Include(a => a.Customer)
                .FirstAsync(a => a.Id == appointment.Id, cancellationToken);

            var providerName = provider.User != null ? $"{provider.User.FirstName} {provider.User.LastName}".Trim() : "Specialist";
            return ApiResponse<AppointmentDto>.Ok(_mapper.Map<AppointmentDto>(updated), $"Appointment successfully accepted and confirmed by {providerName}!");
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
        bool isAdmin, 
        string? customerContact)
    {
        // 1. Admins and SuperAdmins have authority to manage bookings
        if (isAdmin)
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
