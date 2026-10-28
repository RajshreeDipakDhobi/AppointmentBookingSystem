using System.ComponentModel.DataAnnotations;

namespace AppointmentBooking.BusinessLayer.Features.Tenants.DTOs;

public class CreateTenantRequestDto
{
    [Required(ErrorMessage = "Organization / Tenant name is required")]
    [StringLength(100, MinimumLength = 2)]
    public string TenantName { get; set; } = string.Empty;

    [RegularExpression(@"^[a-z0-9\-]+$", ErrorMessage = "TenantCode must contain only lowercase letters, numbers, and hyphens")]
    [StringLength(50, MinimumLength = 2)]
    public string? TenantCode { get; set; }

    [EmailAddress]
    public string? ContactEmail { get; set; }

    [Phone]
    public string? ContactPhone { get; set; }

    public string? Address { get; set; }
    public string? LogoUrl { get; set; }

    // Initial Tenant Admin User Credentials
    [Required(ErrorMessage = "Admin first name is required")]
    public string AdminFirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Admin last name is required")]
    public string AdminLastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Admin email is required")]
    [EmailAddress(ErrorMessage = "Invalid admin email")]
    public string AdminEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Admin password is required")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters")]
    public string AdminPassword { get; set; } = string.Empty;

    [Phone]
    public string? AdminPhoneNumber { get; set; }
}
