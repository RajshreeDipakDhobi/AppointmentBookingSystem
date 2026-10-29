using AppointmentBooking.BusinessLayer.Common.Models;
using AppointmentBooking.BusinessLayer.Features.Auth.DTOs;

namespace AppointmentBooking.BusinessLayer.Features.Auth;

public interface IAuthService
{
    Task<ApiResponse<AuthResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AuthResponseDto>> RegisterCustomerAsync(RegisterCustomerRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AuthResponseDto>> RefreshTokenAsync(RefreshTokenRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> RevokeTokenAsync(string rawRefreshToken, CancellationToken cancellationToken = default);
    Task<ApiResponse<UserDto>> GetCurrentUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<ApiResponse<UserDto>> UpdateProfileAsync(int userId, UpdateProfileRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> ChangePasswordAsync(int userId, ChangePasswordRequestDto request, CancellationToken cancellationToken = default);
}
