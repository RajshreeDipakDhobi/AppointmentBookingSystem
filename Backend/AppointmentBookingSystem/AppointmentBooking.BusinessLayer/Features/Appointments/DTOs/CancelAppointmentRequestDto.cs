using System.ComponentModel.DataAnnotations;
using AppointmentBooking.DomainLayer.Enums;

namespace AppointmentBooking.BusinessLayer.Features.Appointments.DTOs;

public class CancelAppointmentRequestDto
{
    [StringLength(500, ErrorMessage = "CancellationReason cannot exceed 500 characters.")]
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Verification contact (Phone or Email) for guest tracking requests to prevent unauthorized changes
    /// </summary>
    public string? CustomerContact { get; set; }
}

public class UpdateAppointmentStatusDto
{
    [Required]
    public AppointmentStatus Status { get; set; }

    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
    public string? Notes { get; set; }

    // Optional fields used when proposing a reschedule (Status = RescheduleProposed)
    public DateTime? ProposedDate { get; set; }
    public string? ProposedStartTime { get; set; } // "HH:mm"
    public string? ProposedEndTime { get; set; }   // "HH:mm"
}
