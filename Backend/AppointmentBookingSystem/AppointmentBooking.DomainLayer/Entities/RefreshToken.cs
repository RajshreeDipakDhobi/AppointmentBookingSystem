namespace AppointmentBooking.DomainLayer.Entities;

public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public virtual User? User { get; set; }

    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt != null;
    public bool IsActive => !IsRevoked && !IsExpired;
}
