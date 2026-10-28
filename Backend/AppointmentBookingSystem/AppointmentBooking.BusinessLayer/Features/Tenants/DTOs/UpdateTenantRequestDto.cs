using System.ComponentModel.DataAnnotations;

namespace AppointmentBooking.BusinessLayer.Features.Tenants.DTOs;

public class UpdateTenantRequestDto
{
    [Required(ErrorMessage = "Organization / Tenant name is required")]
    [StringLength(100, MinimumLength = 2)]
    public string TenantName { get; set; } = string.Empty;

    [EmailAddress]
    public string? ContactEmail { get; set; }

    [Phone]
    public string? ContactPhone { get; set; }

    public string? Address { get; set; }
    public string? LogoUrl { get; set; }

    public string? AdminFirstName { get; set; }
    public string? AdminLastName { get; set; }
    [EmailAddress]
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
}
