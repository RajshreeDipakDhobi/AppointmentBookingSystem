using AppointmentBooking.BusinessLayer.Common.Interfaces;
using AppointmentBooking.BusinessLayer.Common.Models;
using AppointmentBooking.BusinessLayer.Features.Providers.DTOs;
using AppointmentBooking.DomainLayer.Entities;
using AppointmentBooking.DomainLayer.Enums;
using AppointmentBooking.InfrastructureLayer.Repositories;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AppointmentBooking.BusinessLayer.Features.Providers;

public class ServiceProviderService : IServiceProviderService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMapper _mapper;
    private readonly IEmailService _emailService;
    private readonly ILogger<ServiceProviderService> _logger;

    public ServiceProviderService(
        IUnitOfWork unitOfWork, 
        IPasswordHasher passwordHasher, 
        IMapper mapper,
        IEmailService emailService,
        ILogger<ServiceProviderService> logger)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _mapper = mapper;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ServiceProviderDto>> GetProvidersByTenantAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        var providers = await _unitOfWork.Repository<ServiceProvider>()
            .Query()
            .Include(p => p.User)
            .Include(p => p.Service)
            .Include(p => p.Appointments)
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return _mapper.Map<IReadOnlyList<ServiceProviderDto>>(providers);
    }

    public async Task<AppointmentBooking.BusinessLayer.Common.Models.PagedResult<ServiceProviderDto>> GetProvidersPagedAsync(
        int tenantId, 
        AppointmentBooking.BusinessLayer.Common.Models.PaginationParams paginationParams, 
        ProviderStatus? status = null, 
        CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<ServiceProvider>()
            .Query()
            .Include(p => p.User)
            .Include(p => p.Service)
            .Include(p => p.Appointments)
            .Where(p => p.TenantId == tenantId);

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(paginationParams.Search))
        {
            var search = paginationParams.Search.Trim().ToLower();
            query = query.Where(p => (p.User.FirstName + " " + p.User.LastName).ToLower().Contains(search)
                                  || (p.Service != null && p.Service.Name.ToLower().Contains(search))
                                  || p.User.Email.ToLower().Contains(search)
                                  || (p.User.PhoneNumber != null && p.User.PhoneNumber.Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var providers = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((paginationParams.PageNumber - 1) * paginationParams.PageSize)
            .Take(paginationParams.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<IReadOnlyList<ServiceProviderDto>>(providers);
        return new AppointmentBooking.BusinessLayer.Common.Models.PagedResult<ServiceProviderDto>(
            dtos, totalCount, paginationParams.PageNumber, paginationParams.PageSize);
    }

    public async Task<IReadOnlyList<ServiceProviderDto>> GetActiveProvidersAsync(int? tenantId = null, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<ServiceProvider>()
            .Query()
            .Include(p => p.User)
            .Include(p => p.Service)
            .Where(p => p.Status == ProviderStatus.Active);

        if (tenantId.HasValue && tenantId.Value > 0)
        {
            query = query.Where(p => p.TenantId == tenantId.Value);
        }

        var providers = await query
            .OrderBy(p => p.User.FirstName)
            .ToListAsync(cancellationToken);

        return _mapper.Map<IReadOnlyList<ServiceProviderDto>>(providers);
    }

    public async Task<ServiceProviderDto?> GetProviderByIdAsync(int id, int tenantId, CancellationToken cancellationToken = default)
    {
        var provider = await _unitOfWork.Repository<ServiceProvider>()
            .Query()
            .Include(p => p.User)
            .Include(p => p.Service)
            .Include(p => p.Appointments)
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, cancellationToken);

        return provider == null ? null : _mapper.Map<ServiceProviderDto>(provider);
    }

    public async Task<ApiResponse<ServiceProviderDto>> CreateProviderAsync(int tenantId, CreateServiceProviderRequestDto request, string createdBy, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            // 1. Check if user with this email already exists
            var emailExists = await _unitOfWork.Repository<User>()
                .ExistsAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

            if (emailExists)
            {
                return ApiResponse<ServiceProviderDto>.Fail("A user account with this email address already exists.");
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                // 2. Create User account for Service Provider login
                var names = request.Name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var firstName = names.Length > 0 ? names[0] : request.Name.Trim();
                var lastName = names.Length > 1 ? names[1] : string.Empty;

                var rawPassword = "123456";

                var user = new User
                {
                    TenantId = tenantId,
                    FirstName = firstName,
                    LastName = lastName,
                    Email = normalizedEmail,
                    PhoneNumber = request.PhoneNumber.Trim(),
                    PasswordHash = _passwordHasher.HashPassword(rawPassword),
                    Role = UserRole.ServiceProvider,
                    IsActive = true,
                    IsPasswordAutoGenerated = true,
                    Address = request.Address,
                    ProfilePictureUrl = request.ProfilePictureUrl,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                };

                await _unitOfWork.Repository<User>().AddAsync(user, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // 3. Create ServiceProvider domain profile linked to the User
                var provider = _mapper.Map<ServiceProvider>(request);
                provider.TenantId = tenantId;
                provider.UserId = user.Id;
                provider.CreatedAt = DateTime.UtcNow;
                provider.CreatedBy = createdBy;

                if (!provider.ServiceId.HasValue && !string.IsNullOrWhiteSpace(request.ServiceType))
                {
                    var matchingService = await _unitOfWork.Repository<Service>()
                        .Query()
                        .FirstOrDefaultAsync(s => s.Name.ToLower() == request.ServiceType.Trim().ToLower() && !s.IsDeleted, cancellationToken);
                    if (matchingService != null)
                    {
                        provider.ServiceId = matchingService.Id;
                    }
                }

                await _unitOfWork.Repository<ServiceProvider>().AddAsync(provider, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                // Dispatch Welcome & Credentials Email to newly created Service Provider
                try
                {
                    await _emailService.SendAccountCreatedEmailAsync(
                        user.Email, 
                        request.Name.Trim(), 
                        rawPassword, 
                        cancellationToken);
                }
                catch (Exception emailEx)
                {
                    _logger.LogError(emailEx, "Failed to send welcome credentials email to Service Provider {Email}", user.Email);
                }

                return ApiResponse<ServiceProviderDto>.Ok(_mapper.Map<ServiceProviderDto>(provider), "Service provider created successfully.");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                _logger.LogError(ex, "Database error creating service provider {ProviderName}", request.Name);
                return ApiResponse<ServiceProviderDto>.Fail($"Failed to create service provider: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating service provider {ProviderName}", request.Name);
            return ApiResponse<ServiceProviderDto>.Fail($"Failed to create service provider: {ex.Message}");
        }
    }

    public async Task<ServiceProviderDto?> UpdateProviderAsync(int id, int tenantId, UpdateServiceProviderRequestDto request, CancellationToken cancellationToken = default)
    {
        var provider = await _unitOfWork.Repository<ServiceProvider>()
            .Query()
            .Include(p => p.Appointments)
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, cancellationToken);

        if (provider == null)
        {
            return null;
        }

        _mapper.Map(request, provider);
        provider.UpdatedAt = DateTime.UtcNow;

        if (request.ServiceId.HasValue)
        {
            provider.ServiceId = request.ServiceId.Value > 0 ? request.ServiceId.Value : null;
        }
        else if (!string.IsNullOrWhiteSpace(request.ServiceType))
        {
            var matchingService = await _unitOfWork.Repository<Service>()
                .Query()
                .FirstOrDefaultAsync(s => s.Name.ToLower() == request.ServiceType.Trim().ToLower() && !s.IsDeleted, cancellationToken);
            if (matchingService != null)
            {
                provider.ServiceId = matchingService.Id;
            }
        }

        if (provider.UserId > 0)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(provider.UserId, cancellationToken);
            if (user != null)
            {
                var names = request.Name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                user.FirstName = names.Length > 0 ? names[0] : request.Name.Trim();
                user.LastName = names.Length > 1 ? names[1] : string.Empty;
                user.Email = request.Email.Trim().ToLowerInvariant();
                user.PhoneNumber = request.PhoneNumber.Trim();
                user.Address = request.Address;
                user.ProfilePictureUrl = request.ProfilePictureUrl;
                user.UpdatedAt = DateTime.UtcNow;
                _unitOfWork.Repository<User>().Update(user);
            }
        }

        _unitOfWork.Repository<ServiceProvider>().Update(provider);

        if (provider.ServiceId.HasValue)
        {
            var newService = await _unitOfWork.Repository<Service>().GetByIdAsync(provider.ServiceId.Value, cancellationToken);
            if (newService != null && newService.DurationMinutes > 0)
            {
                var availabilities = await _unitOfWork.Repository<ProviderAvailability>()
                    .Query()
                    .Where(pa => pa.ServiceProviderId == provider.Id)
                    .ToListAsync(cancellationToken);

                foreach (var pa in availabilities)
                {
                    pa.SlotDurationMinutes = newService.DurationMinutes;
                    pa.UpdatedAt = DateTime.UtcNow;
                    _unitOfWork.Repository<ProviderAvailability>().Update(pa);
                }
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<ServiceProviderDto>(provider);
    }

    public async Task<bool> UpdateProviderStatusAsync(int id, int tenantId, ProviderStatus status, CancellationToken cancellationToken = default)
    {
        var provider = await _unitOfWork.Repository<ServiceProvider>()
            .FindAsync(p => p.Id == id && p.TenantId == tenantId, cancellationToken);

        if (provider == null)
        {
            return false;
        }

        provider.Status = status;
        provider.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Repository<ServiceProvider>().Update(provider);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteProviderAsync(int id, int tenantId, CancellationToken cancellationToken = default)
    {
        var provider = await _unitOfWork.Repository<ServiceProvider>()
            .FindAsync(p => p.Id == id && p.TenantId == tenantId, cancellationToken);

        if (provider == null)
        {
            return false;
        }

        _unitOfWork.Repository<ServiceProvider>().Remove(provider);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
