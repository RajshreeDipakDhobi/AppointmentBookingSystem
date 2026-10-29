using AppointmentBooking.BusinessLayer.Common.Interfaces;
using AppointmentBooking.BusinessLayer.Features.Tenants.DTOs;
using AppointmentBooking.DomainLayer.Entities;
using AppointmentBooking.DomainLayer.Enums;
using AppointmentBooking.InfrastructureLayer.Repositories;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using AppointmentBooking.BusinessLayer.Common.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AppointmentBooking.BusinessLayer.Features.Tenants;

public class TenantService : ITenantService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMapper _mapper;
    private readonly IEmailService _emailService;
    private readonly Microsoft.Extensions.Logging.ILogger<TenantService> _logger;

    public TenantService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IMapper mapper,
        IEmailService emailService,
        Microsoft.Extensions.Logging.ILogger<TenantService> logger)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _mapper = mapper;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TenantDto>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _unitOfWork.Repository<Tenant>()
            .Query()
            .AsNoTracking()
            .Include(t => t.Users)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        return _mapper.Map<IReadOnlyList<TenantDto>>(tenants);
    }

    public async Task<PagedResult<TenantDto>> GetTenantsPagedAsync(PaginationParams paginationParams, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Tenant>()
            .Query()
            .AsNoTracking()
            .Include(t => t.Users)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(paginationParams.Search))
        {
            var search = paginationParams.Search.Trim().ToLower();
            query = query.Where(t => t.Name.ToLower().Contains(search) 
                                  || (t.ContactEmail != null && t.ContactEmail.ToLower().Contains(search))
                                  || (t.ContactPhone != null && t.ContactPhone.Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var tenants = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((paginationParams.PageNumber - 1) * paginationParams.PageSize)
            .Take(paginationParams.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<IReadOnlyList<TenantDto>>(tenants);
        return new PagedResult<TenantDto>(dtos, totalCount, paginationParams.PageNumber, paginationParams.PageSize);
    }

    public async Task<TenantDto?> GetTenantByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var tenant = await _unitOfWork.Repository<Tenant>()
            .Query()
            .AsNoTracking()
            .Include(t => t.Users)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        return tenant == null ? null : _mapper.Map<TenantDto>(tenant);
    }

    public async Task<ApiResponse<TenantDto>> CreateTenantAsync(CreateTenantRequestDto request, string createdBy, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Single email check (ContactEmail is the official login email)
            var targetEmail = request.ContactEmail?.Trim().ToLower();

            if (string.IsNullOrWhiteSpace(targetEmail))
            {
                return ApiResponse<TenantDto>.Fail("A valid contact email is required.");
            }

            bool emailExists = await _unitOfWork.Repository<User>()
                .ExistsAsync(u => u.Email.ToLower() == targetEmail, cancellationToken);

            if (emailExists)
            {
                return ApiResponse<TenantDto>.Fail("A user account with this email already exists.");
            }

            // 2. Auto-generate unique TenantCode
            var baseSlug = Regex.Replace(request.TenantName.ToLower().Trim(), @"[^a-z0-9]+", "-").Trim('-');

            if (string.IsNullOrWhiteSpace(baseSlug))
            {
                baseSlug = "tenant";
            }

            var uniqueTenantCode = baseSlug;
            int counter = 1;
            while (await _unitOfWork.Repository<Tenant>().ExistsAsync(t => t.TenantCode == uniqueTenantCode, cancellationToken))
            {
                uniqueTenantCode = $"{baseSlug}-{counter}";
                counter++;
            }

            // 3. Default password (123456)
            var rawPassword = "123456";

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                // 4. Create Tenant (Organization)
                var tenant = _mapper.Map<Tenant>(request);
                tenant.TenantCode = uniqueTenantCode;
                tenant.ContactEmail = targetEmail;
                tenant.ContactPhone = request.ContactPhone?.Trim();
                tenant.CreatedAt = DateTime.UtcNow;
                tenant.CreatedBy = createdBy;

                await _unitOfWork.Repository<Tenant>().AddAsync(tenant, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // 5. Create Tenant Admin (User) with First & Last Name
                var adminUser = new User
                {
                    TenantId = tenant.Id,
                    FirstName = request.AdminFirstName.Trim(),
                    LastName = request.AdminLastName.Trim(),
                    Email = targetEmail,
                    PhoneNumber = tenant.ContactPhone,
                    PasswordHash = _passwordHasher.HashPassword(rawPassword),
                    Role = UserRole.Admin,
                    IsActive = true,
                    IsPasswordAutoGenerated = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                };

                await _unitOfWork.Repository<User>().AddAsync(adminUser, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                // Dispatch Welcome & Credentials Email to newly provisioned Tenant Admin
                try
                {
                    var adminFullName = $"{request.AdminFirstName} {request.AdminLastName}".Trim();
                    await _emailService.SendAccountCreatedEmailAsync(
                        targetEmail, 
                        adminFullName, 
                        rawPassword, 
                        cancellationToken);
                }
                catch (Exception emailEx)
                {
                    _logger.LogError(emailEx, "Failed to send welcome credentials email to Tenant Admin {Email}", targetEmail);
                }

                return ApiResponse<TenantDto>.Ok(_mapper.Map<TenantDto>(tenant), "Tenant created successfully.");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                _logger.LogError(ex, "Database error creating tenant organization {TenantName}", request.TenantName);
                return ApiResponse<TenantDto>.Fail($"Failed to create tenant: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tenant organization {TenantName}", request.TenantName);
            return ApiResponse<TenantDto>.Fail($"Failed to create tenant: {ex.Message}");
        }
    }

    public async Task<TenantDto?> UpdateTenantAsync(int id, UpdateTenantRequestDto request, CancellationToken cancellationToken = default)
    {
        var tenant = await _unitOfWork.Repository<Tenant>()
            .Query()
            .Include(t => t.Users)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (tenant == null)
        {
            return null;
        }

        _mapper.Map(request, tenant);
        tenant.UpdatedAt = DateTime.UtcNow;

        // Also update Admin user if details provided
        var adminUser = tenant.Users.FirstOrDefault(u => u.Role == UserRole.Admin);
        if (adminUser != null)
        {
            if (!string.IsNullOrWhiteSpace(request.AdminFirstName))
                adminUser.FirstName = request.AdminFirstName.Trim();
            if (!string.IsNullOrWhiteSpace(request.AdminLastName))
                adminUser.LastName = request.AdminLastName.Trim();
            if (!string.IsNullOrWhiteSpace(request.ContactEmail))
                adminUser.Email = request.ContactEmail.Trim().ToLower();
            adminUser.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.Repository<User>().Update(adminUser);
        }

        _unitOfWork.Repository<Tenant>().Update(tenant);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<TenantDto>(tenant);
    }

    public async Task<bool> UpdateTenantStatusAsync(int id, TenantStatus status, CancellationToken cancellationToken = default)
    {
        var tenant = await _unitOfWork.Repository<Tenant>().GetByIdAsync(id, cancellationToken);
        if (tenant == null)
        {
            return false;
        }

        tenant.Status = status;
        tenant.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<Tenant>().Update(tenant);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
