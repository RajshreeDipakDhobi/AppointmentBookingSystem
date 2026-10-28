using System.ComponentModel.DataAnnotations;

namespace AppointmentBooking.BusinessLayer.Features.Appointments.DTOs;

public class RescheduleAppointmentRequestDto
{
    [Required]
    public DateTime NewDate { get; set; }

    [Required]
    [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "NewStartTime must be in HH:mm format.")]
    public string NewStartTime { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "NewEndTime must be in HH:mm format.")]
    public string NewEndTime { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Reason cannot exceed 500 characters.")]
    public string? Reason { get; set; }

    /// <summary>
    /// Verification contact (Phone or Email) for guest tracking requests to prevent unauthorized changes
    /// </summary>
    public string? CustomerContact { get; set; }
}
