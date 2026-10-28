using AppointmentBooking.DomainLayer.Enums;
using System.ComponentModel.DataAnnotations;

namespace AppointmentBooking.BusinessLayer.Features.Providers.DTOs;

public class UpdateServiceProviderRequestDto
{
    [Required(ErrorMessage = "Provider name is required")]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Specialization / Service Type is required")]
    [StringLength(100)]
    public string ServiceType { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required")]
    [Phone(ErrorMessage = "Invalid phone number")]
    public string PhoneNumber { get; set; } = string.Empty;

    public string? Address { get; set; }
    public string? Bio { get; set; }
    public string? ProfilePictureUrl { get; set; }

    [Range(0, 1000000, ErrorMessage = "Charges amount must be a positive value")]
    public decimal ChargesAmount { get; set; } = 0.00m;

    public ProviderStatus Status { get; set; } = ProviderStatus.Active;
}
