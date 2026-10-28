using System.ComponentModel.DataAnnotations;

namespace AppointmentBooking.BusinessLayer.Features.Auth.DTOs;

public class RegisterCustomerRequestDto
{
    [Required]
    [StringLength(100, ErrorMessage = "Full name cannot exceed 100 characters.")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters.")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [Phone]
    [StringLength(50, ErrorMessage = "Phone number cannot exceed 50 characters.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
    public string Password { get; set; } = string.Empty;

    [StringLength(250, ErrorMessage = "Address cannot exceed 250 characters.")]
    public string? Address { get; set; }
}
