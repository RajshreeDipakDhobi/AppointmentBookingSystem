using AppointmentBooking.BusinessLayer.Features.Appointments.DTOs;

namespace AppointmentBooking.BusinessLayer.Features.Calendar.DTOs;

public class CalendarAppointmentItemDto
{
    public int Id { get; set; }
    public string AppointmentNumber { get; set; } = string.Empty;
    public string? BookingGroupId { get; set; }
    public int? ServiceProviderId { get; set; }
    public string ServiceProviderName { get; set; } = string.Empty;
    public string? ServiceProviderProfilePictureUrl { get; set; }
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public List<string>? ServiceNames { get; set; }
    public List<AppointmentItemDto> Items { get; set; } = new();
    public int TotalServicesCount { get; set; } = 1;
    public string AppointmentDate { get; set; } = string.Empty; // YYYY-MM-DD
    public string StartTime { get; set; } = string.Empty; // HH:mm
    public string EndTime { get; set; } = string.Empty; // HH:mm
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public int Status { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public bool IsMultiService { get; set; }
    public bool CanManage { get; set; } // Whether requesting user can approve/reschedule/cancel this specific appointment
    public bool IsBroadcastRequest { get; set; } // True if open customer broadcast request awaiting specialist acceptance
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledByName { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProposedDate { get; set; }
    public string? ProposedStartTime { get; set; }
    public string? ProposedEndTime { get; set; }
    public string? RescheduleNotes { get; set; }
    public int? RescheduledFromAppointmentId { get; set; }
}

