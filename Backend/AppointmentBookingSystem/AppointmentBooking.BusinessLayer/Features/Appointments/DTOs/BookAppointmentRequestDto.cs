using System.ComponentModel.DataAnnotations;

namespace AppointmentBooking.BusinessLayer.Features.Appointments.DTOs;

public class BookAppointmentRequestDto
{
    [Required]
    public int ServiceProviderId { get; set; }

    [Required]
    public DateTime AppointmentDate { get; set; }

    [Required]
    [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "StartTime must be in HH:mm format.")]
    public string StartTime { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "EndTime must be in HH:mm format.")]
    public string EndTime { get; set; } = string.Empty;

    [Required]
    [StringLength(150, ErrorMessage = "CustomerName cannot exceed 150 characters.")]
    public string CustomerName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256, ErrorMessage = "CustomerEmail cannot exceed 256 characters.")]
    public string CustomerEmail { get; set; } = string.Empty;

    [Required]
    [Phone]
    [StringLength(50, ErrorMessage = "CustomerPhone cannot exceed 50 characters.")]
    public string CustomerPhone { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "CustomerAddress cannot exceed 500 characters.")]
    public string? CustomerAddress { get; set; }

    [StringLength(500, ErrorMessage = "Reason cannot exceed 500 characters.")]
    public string? Reason { get; set; }

    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
    public string? Password { get; set; }
}
