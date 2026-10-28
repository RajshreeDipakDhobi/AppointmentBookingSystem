using AppointmentBooking.DomainLayer.Entities;

namespace AppointmentBooking.BusinessLayer.Common.Interfaces;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) GenerateAccessToken(User user);
    (string RawToken, string TokenHash, DateTime ExpiresAt) GenerateRefreshToken();
    string HashToken(string token);
}
