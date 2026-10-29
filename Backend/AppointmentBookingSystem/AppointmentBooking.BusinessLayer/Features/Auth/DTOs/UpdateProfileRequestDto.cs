using System.ComponentModel.DataAnnotations;

namespace AppointmentBooking.BusinessLayer.Features.Auth.DTOs;

public class UpdateProfileRequestDto
{
    [Required(ErrorMessage = "First name is required.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "First name must be between 2 and 50 characters.")]
    public string FirstName { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters.")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    [RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Please enter a valid 10-digit mobile number.")]
    public string PhoneNumber { get; set; } = string.Empty;

    public string? ProfilePictureUrl { get; set; }

    [StringLength(200, ErrorMessage = "Address cannot exceed 200 characters.")]
    public string? Address { get; set; }

    [StringLength(100, ErrorMessage = "City cannot exceed 100 characters.")]
    public string? City { get; set; }

    [StringLength(100, ErrorMessage = "State cannot exceed 100 characters.")]
    public string? State { get; set; }

    [StringLength(20, ErrorMessage = "Postal code cannot exceed 20 characters.")]
    public string? PostalCode { get; set; }
}
