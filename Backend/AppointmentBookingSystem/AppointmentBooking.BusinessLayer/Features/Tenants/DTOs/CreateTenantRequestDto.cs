using System.ComponentModel.DataAnnotations;

namespace AppointmentBooking.BusinessLayer.Features.Tenants.DTOs;

public class CreateTenantRequestDto
{
    [Required(ErrorMessage = "Organization / Tenant name is required")]
    [StringLength(100, MinimumLength = 2)]
    public string TenantName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Contact email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    public string ContactEmail { get; set; } = string.Empty;

    [Phone]
    public string? ContactPhone { get; set; }

    public string? Address { get; set; }

    // Initial Tenant Admin User Credentials
    [Required(ErrorMessage = "Admin first name is required")]
    [StringLength(75, MinimumLength = 1)]
    public string AdminFirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Admin last name is required")]
    [StringLength(75, MinimumLength = 1)]
    public string AdminLastName { get; set; } = string.Empty;
}
