using AppointmentBooking.DomainLayer.Common;
using AppointmentBooking.DomainLayer.Enums;

namespace AppointmentBooking.DomainLayer.Entities;

public class User : BaseEntity
{
    public int? TenantId { get; set; }
    public virtual Tenant? Tenant { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Admin;
    public bool IsActive { get; set; } = true;
    public string? PhoneNumber { get; set; }
    // Profile & Address Details
    public string? ProfilePictureUrl { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    // Navigations (Only strictly required)
    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public virtual ICollection<Appointment> CustomerAppointments { get; set; } = new List<Appointment>();
    public virtual ServiceProvider? ServiceProvider { get; set; }
}
