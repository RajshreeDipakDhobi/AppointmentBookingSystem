using AppointmentBooking.BusinessLayer.Common.Interfaces;
using AppointmentBooking.BusinessLayer.Common.Models;
using AppointmentBooking.BusinessLayer.Features.Auth.DTOs;
using AppointmentBooking.DomainLayer.Entities;
using AppointmentBooking.DomainLayer.Enums;
using AppointmentBooking.InfrastructureLayer.Repositories;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AppointmentBooking.BusinessLayer.Features.Auth;

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IMapper _mapper;
    private readonly IEmailService _emailService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IMapper mapper,
        IEmailService emailService,
        ILogger<AuthService> logger)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _mapper = mapper;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<ApiResponse<AuthResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var input = request.Email.Trim();
            var normalizedEmail = input.ToLower();
            var digitsOnly = new string(input.Where(char.IsDigit).ToArray());

            var user = await _unitOfWork.Repository<User>()
                .Query()
                .Include(u => u.Tenant)
                .Include(u => u.ServiceProvider)
                .FirstOrDefaultAsync(u => 
                    u.Email.ToLower() == normalizedEmail ||
                    (digitsOnly.Length >= 10 && u.PhoneNumber != null && u.PhoneNumber.Contains(digitsOnly.Substring(digitsOnly.Length - 10))),
                    cancellationToken);

            if (user == null)
            {
                return ApiResponse<AuthResponseDto>.Fail("Invalid email or password.");
            }

            if (!user.IsActive)
            {
                return ApiResponse<AuthResponseDto>.Fail("Your user account has been deactivated. Please contact support.");
            }

            if (user.TenantId.HasValue && user.Tenant != null && user.Tenant.Status != TenantStatus.Active)
            {
                return ApiResponse<AuthResponseDto>.Fail($"Your organization is currently {user.Tenant.Status.ToString().ToLower()}. Access denied.");
            }

            // 1. Try standard password verification
            bool isPasswordValid = false;
            if (!string.IsNullOrEmpty(user.PasswordHash))
            {
                isPasswordValid = _passwordHasher.VerifyPassword(request.Password, user.PasswordHash);
            }

            // 2. If password verification fails, check if customer used their mobile number as password
            if (!isPasswordValid && !string.IsNullOrWhiteSpace(user.PhoneNumber))
            {
                var phoneDigits = new string(user.PhoneNumber.Where(char.IsDigit).ToArray());
                var pwdDigits = new string(request.Password.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(pwdDigits) && pwdDigits.Length >= 10 && phoneDigits.EndsWith(pwdDigits.Substring(pwdDigits.Length - 10)))
                {
                    isPasswordValid = true;
                    if (string.IsNullOrEmpty(user.PasswordHash))
                    {
                        user.PasswordHash = _passwordHasher.HashPassword(pwdDigits);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                    }
                }
            }

            if (!isPasswordValid)
            {
                return ApiResponse<AuthResponseDto>.Fail("Invalid email or password.");
            }

            var authDto = await GenerateAuthResponseAsync(user, cancellationToken);
            return ApiResponse<AuthResponseDto>.Ok(authDto, "Login successful.");
        }
        catch (Exception ex)
        {
            return ApiResponse<AuthResponseDto>.Fail(ex.Message);
        }
    }

    public async Task<ApiResponse<AuthResponseDto>> RegisterCustomerAsync(RegisterCustomerRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var existingUser = await _unitOfWork.Repository<User>()
                .Query()
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

            if (existingUser != null)
            {
                if (string.IsNullOrEmpty(existingUser.PasswordHash))
                {
                    // Shadow guest account being activated with password
                    existingUser.PasswordHash = _passwordHasher.HashPassword(request.Password);
                    if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
                    {
                        existingUser.PhoneNumber = request.PhoneNumber.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(request.Address))
                    {
                        existingUser.Address = request.Address.Trim();
                    }
                    var namePartsUpdate = request.FullName.Trim().Split(' ', 2);
                    existingUser.FirstName = namePartsUpdate[0];
                    existingUser.LastName = namePartsUpdate.Length > 1 ? namePartsUpdate[1] : string.Empty;
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    // Dispatch Welcome & Credentials Email to upgraded Customer
                    try
                    {
                        await _emailService.SendAccountCreatedEmailAsync(
                            existingUser.Email,
                            request.FullName.Trim(),
                            request.Password,
                            cancellationToken);
                    }
                    catch (Exception emailEx)
                    {
                        _logger.LogError(emailEx, "Failed to send welcome email to Customer {Email}", existingUser.Email);
                    }

                    var updatedAuthDto = await GenerateAuthResponseAsync(existingUser, cancellationToken);
                    return ApiResponse<AuthResponseDto>.Ok(updatedAuthDto, "Registration successful.");
                }

                return ApiResponse<AuthResponseDto>.Fail("An account with this email address already exists. Please login instead.");
            }

            var nameParts = request.FullName.Trim().Split(' ', 2);
            string firstName = nameParts[0];
            string lastName = nameParts.Length > 1 ? nameParts[1] : string.Empty;

            var newUser = new User
            {
                FirstName = firstName,
                LastName = lastName,
                Email = normalizedEmail,
                PasswordHash = _passwordHasher.HashPassword(request.Password),
                PhoneNumber = request.PhoneNumber.Trim(),
                Address = request.Address?.Trim(),
                Role = UserRole.Customer,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Repository<User>().AddAsync(newUser, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Dispatch Welcome & Credentials Email to newly registered Customer
            try
            {
                await _emailService.SendAccountCreatedEmailAsync(
                    newUser.Email,
                    request.FullName.Trim(),
                    request.Password,
                    cancellationToken);
            }
            catch (Exception emailEx)
            {
                _logger.LogError(emailEx, "Failed to send welcome credentials email to Customer {Email}", newUser.Email);
            }

            var authDto = await GenerateAuthResponseAsync(newUser, cancellationToken);
            return ApiResponse<AuthResponseDto>.Ok(authDto, "Registration successful.");
        }
        catch (Exception ex)
        {
            return ApiResponse<AuthResponseDto>.Fail(ex.Message);
        }
    }

    public async Task<ApiResponse<AuthResponseDto>> RefreshTokenAsync(RefreshTokenRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenHash = _jwtTokenService.HashToken(request.RefreshToken ?? string.Empty);

            var storedToken = await _unitOfWork.Repository<RefreshToken>()
                .Query()
                .Include(r => r.User)
                .ThenInclude(u => u!.Tenant)
                .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, cancellationToken);

            if (storedToken == null || !storedToken.IsActive)
            {
                return ApiResponse<AuthResponseDto>.Fail("Invalid or expired refresh token.");
            }

            var user = storedToken.User;
            if (user == null || !user.IsActive)
            {
                return ApiResponse<AuthResponseDto>.Fail("User is inactive.");
            }

            // Revoke the old refresh token (Token Rotation)
            storedToken.RevokedAt = DateTime.UtcNow;
            _unitOfWork.Repository<RefreshToken>().Update(storedToken);

            var authDto = await GenerateAuthResponseAsync(user, cancellationToken);
            return ApiResponse<AuthResponseDto>.Ok(authDto, "Token refreshed successfully.");
        }
        catch (Exception ex)
        {
            return ApiResponse<AuthResponseDto>.Fail(ex.Message);
        }
    }

    public async Task<bool> RevokeTokenAsync(string rawRefreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = _jwtTokenService.HashToken(rawRefreshToken);

        var storedToken = await _unitOfWork.Repository<RefreshToken>()
            .FindAsync(r => r.TokenHash == tokenHash, cancellationToken);

        if (storedToken == null || storedToken.IsRevoked)
        {
            return false;
        }

        storedToken.RevokedAt = DateTime.UtcNow;
        _unitOfWork.Repository<RefreshToken>().Update(storedToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ApiResponse<UserDto>> GetCurrentUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _unitOfWork.Repository<User>()
                .Query()
                .Include(u => u.Tenant)
                .Include(u => u.ServiceProvider)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user == null)
            {
                return ApiResponse<UserDto>.Fail("User not found.");
            }

            return ApiResponse<UserDto>.Ok(_mapper.Map<UserDto>(user));
        }
        catch (Exception ex)
        {
            return ApiResponse<UserDto>.Fail(ex.Message);
        }
    }

    public async Task<ApiResponse<UserDto>> UpdateProfileAsync(int userId, UpdateProfileRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _unitOfWork.Repository<User>()
                .Query()
                .Include(u => u.Tenant)
                .Include(u => u.ServiceProvider)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user == null)
            {
                return ApiResponse<UserDto>.Fail("User account not found.");
            }

            user.FirstName = request.FirstName.Trim();
            user.LastName = request.LastName?.Trim() ?? string.Empty;
            user.PhoneNumber = request.PhoneNumber.Trim();
            user.ProfilePictureUrl = string.IsNullOrWhiteSpace(request.ProfilePictureUrl) ? null : request.ProfilePictureUrl.Trim();
            user.Address = request.Address?.Trim();
            user.City = request.City?.Trim();
            user.State = request.State?.Trim();
            user.PostalCode = request.PostalCode?.Trim();

            _unitOfWork.Repository<User>().Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Profile updated successfully for User #{UserId} ({Email})", user.Id, user.Email);
            return ApiResponse<UserDto>.Ok(_mapper.Map<UserDto>(user), "Profile updated successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile for User #{UserId}", userId);
            return ApiResponse<UserDto>.Fail(ex.Message);
        }
    }

    public async Task<ApiResponse<bool>> ChangePasswordAsync(int userId, ChangePasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _unitOfWork.Repository<User>()
                .GetByIdAsync(userId, cancellationToken);

            if (user == null)
            {
                return ApiResponse<bool>.Fail("User account not found.");
            }

            // Verify current password if user has a password hash set
            if (!string.IsNullOrEmpty(user.PasswordHash))
            {
                var isCurrentPasswordValid = _passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash);
                
                // Allow fallback for digits-only migration if applicable
                if (!isCurrentPasswordValid && request.CurrentPassword.Length >= 6)
                {
                    var digitsOnly = new string(request.CurrentPassword.Where(char.IsDigit).ToArray());
                    if (digitsOnly.Length == 6 && _passwordHasher.VerifyPassword(digitsOnly, user.PasswordHash))
                    {
                        isCurrentPasswordValid = true;
                    }
                }

                if (!isCurrentPasswordValid)
                {
                    return ApiResponse<bool>.Fail("The current password you entered is incorrect.");
                }
            }

            if (request.CurrentPassword == request.NewPassword)
            {
                return ApiResponse<bool>.Fail("Your new password cannot be the same as your current password.");
            }

            user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
            user.IsPasswordAutoGenerated = false;
            _unitOfWork.Repository<User>().Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Password successfully updated for User #{UserId} ({Email})", user.Id, user.Email);
            return ApiResponse<bool>.Ok(true, "Password changed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password for User #{UserId}", userId);
            return ApiResponse<bool>.Fail(ex.Message);
        }
    }

    private async Task<AuthResponseDto> GenerateAuthResponseAsync(User user, CancellationToken cancellationToken)
    {
        var (accessToken, accessExpiry) = _jwtTokenService.GenerateAccessToken(user);
        var (rawRefreshToken, tokenHash, refreshExpiry) = _jwtTokenService.GenerateRefreshToken();

        var refreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = refreshExpiry,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<RefreshToken>().AddAsync(refreshTokenEntity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            ExpiresAt = accessExpiry,
            User = _mapper.Map<UserDto>(user)
        };
    }
}
