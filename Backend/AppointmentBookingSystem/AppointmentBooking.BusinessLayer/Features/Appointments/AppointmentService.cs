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

namespace AppointmentBooking.BusinessLayer.Features.Appointments;

public class AppointmentService : IAppointmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AppointmentService(
        IUnitOfWork unitOfWork, 
        IMapper mapper, 
        IPasswordHasher passwordHasher,
        IDateTimeProvider dateTimeProvider)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _passwordHasher = passwordHasher;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<ProviderDailyAvailabilityResponseDto?> GetAvailableSlotsAsync(int providerId, DateTime date, CancellationToken cancellationToken = default)
    {
        var provider = await _unitOfWork.Repository<ServiceProvider>().GetByIdAsync(providerId, cancellationToken);
        if (provider == null)
        {
            return null;
        }

        var response = new ProviderDailyAvailabilityResponseDto
        {
            ServiceProviderId = provider.Id,
            ServiceProviderName = provider.Name,
            ServiceType = provider.ServiceType,
            Date = date.Date,
            DayName = date.DayOfWeek.ToString(),
            IsWorkingDay = false,
            WorkingHours = "Closed",
            SlotDurationMinutes = 30,
            Slots = new List<AvailableSlotDto>()
        };

        // If provider is inactive, they do not accept bookings
        if (provider.Status != ProviderStatus.Active)
        {
            return response;
        }

        var availability = await _unitOfWork.Repository<ProviderAvailability>().Query()
            .FirstOrDefaultAsync(pa => pa.ServiceProviderId == providerId && pa.DayOfWeek == date.DayOfWeek, cancellationToken);

        if (availability == null || !availability.IsAvailable)
        {
            return response;
        }

        response.IsWorkingDay = true;
        response.WorkingHours = $"{availability.StartTime:hh\\:mm} – {availability.EndTime:hh\\:mm}";
        response.SlotDurationMinutes = availability.SlotDurationMinutes;

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
        var duration = TimeSpan.FromMinutes(availability.SlotDurationMinutes);

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
            Role = customer.Role
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
            Role = customer.Role
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
        // 1. Validate Provider
        var provider = await _unitOfWork.Repository<ServiceProvider>().GetByIdAsync(request.ServiceProviderId, cancellationToken);
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

        var resolvedTenantId = tenantId ?? provider.TenantId;

        // 2. Parse and Validate Times
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

        // 3. Verify Provider Availability configuration
        var availability = await _unitOfWork.Repository<ProviderAvailability>().Query()
            .FirstOrDefaultAsync(pa => pa.ServiceProviderId == request.ServiceProviderId && pa.DayOfWeek == appointmentDate.DayOfWeek);

        if (availability == null || !availability.IsAvailable)
        {
            return ApiResponse<AppointmentDto>.Fail($"Service provider {provider.Name} is not available on {appointmentDate.DayOfWeek}.");
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

        // 4. Concurrency Guard / Prevent Duplicate Bookings (Module 6)
        // Execute inside transaction to prevent simultaneous race conditions
        await _unitOfWork.BeginTransactionAsync();
        try
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

            if (customerUser == null)
            {
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
                ServiceProviderId = request.ServiceProviderId,
                CustomerId = customerId.Value,
                Reason = request.Reason?.Trim(),
                AppointmentDate = appointmentDate,
                StartTime = startTime,
                EndTime = endTime,
                Status = isBookedByProvider ? AppointmentStatus.Approved : AppointmentStatus.Booked,
                ApprovedAt = isBookedByProvider ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = !string.IsNullOrWhiteSpace(createdByName) 
                    ? createdByName.Trim() 
                    : (isBookedByProvider ? "Service Provider" : $"{customerUser.FirstName} {customerUser.LastName}".Trim())
            };

            await _unitOfWork.Repository<Appointment>().AddAsync(appointment);
            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();

            // Load navigation properties for return
            var created = await _unitOfWork.Repository<Appointment>().Query()
                .Include(a => a.ServiceProvider).ThenInclude(s => s.User)
                .Include(a => a.Customer)
                .FirstAsync(a => a.Id == appointment.Id);

            return ApiResponse<AppointmentDto>.Ok(_mapper.Map<AppointmentDto>(created), "Appointment booked successfully.");
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

        // Verify provider availability for new schedule
        var availability = await _unitOfWork.Repository<ProviderAvailability>().Query()
            .FirstOrDefaultAsync(pa => pa.ServiceProviderId == appointment.ServiceProviderId && pa.DayOfWeek == newDate.DayOfWeek);

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
