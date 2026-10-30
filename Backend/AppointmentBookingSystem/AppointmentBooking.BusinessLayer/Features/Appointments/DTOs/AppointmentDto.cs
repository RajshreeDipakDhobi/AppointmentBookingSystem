using AppointmentBooking.DomainLayer.Enums;

namespace AppointmentBooking.BusinessLayer.Features.Appointments.DTOs;

public class AppointmentDto
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string AppointmentNumber { get; set; } = string.Empty;

    public int? ServiceProviderId { get; set; }
    public string ServiceProviderName { get; set; } = string.Empty;
    public string ServiceProviderType { get; set; } = string.Empty;
    public string? ServiceProviderProfilePictureUrl { get; set; }

    public int? ServiceId { get; set; }
    public string? ServiceName { get; set; }
    public decimal? ServicePrice { get; set; }
    public int? ServiceDurationMinutes { get; set; }
    public bool IsBroadcast => !ServiceProviderId.HasValue;

    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string? CustomerAddress { get; set; }
    public string? Reason { get; set; }

    public DateTime AppointmentDate { get; set; }
    public string FormattedDate => AppointmentDate.ToString("yyyy-MM-dd");
    public string StartTime { get; set; } = string.Empty; // "HH:mm"
    public string EndTime { get; set; } = string.Empty;   // "HH:mm"
    public string TimeSlotText => $"{StartTime} – {EndTime}";

    public AppointmentStatus Status { get; set; }
    public string StatusText => Status switch
    {
        AppointmentStatus.Pending when !ServiceProviderId.HasValue => "Broadcasting",
        AppointmentStatus.Pending => "Booked",
        AppointmentStatus.Approved => "Approved",
        AppointmentStatus.Completed => "Completed",
        AppointmentStatus.Cancelled => "Cancelled",
        AppointmentStatus.Rescheduled => "Rescheduled",
        AppointmentStatus.Rejected => "Rejected",
        AppointmentStatus.RescheduleProposed => "Reschedule Proposed",
        _ => Status.ToString()
    };

    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }

    public int? RescheduledFromAppointmentId { get; set; }
    public DateTime CreatedAt { get; set; }

    // Provider-Initiated Reschedule Proposal Details
    public DateTime? ProposedDate { get; set; }
    public string? ProposedDateText => ProposedDate?.ToString("yyyy-MM-dd");
    public string? ProposedStartTime { get; set; }
    public string? ProposedEndTime { get; set; }
    public string? ProposedTimeSlotText => (!string.IsNullOrEmpty(ProposedStartTime) && !string.IsNullOrEmpty(ProposedEndTime)) ? $"{ProposedStartTime} – {ProposedEndTime}" : null;
    public string? RescheduleNotes { get; set; }

    public string? CancelledByName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CancellationReason)) return null;
            var marker = "(Cancelled by ";
            var idx = CancellationReason.LastIndexOf(marker);
            if (idx >= 0)
            {
                var end = CancellationReason.IndexOf(')', idx);
                if (end > idx)
                {
                    return CancellationReason.Substring(idx + marker.Length, end - (idx + marker.Length)).Trim();
                }
            }
            return null;
        }
    }

    public string? CleanCancellationReason
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CancellationReason)) return null;
            var marker = "(Cancelled by ";
            var idx = CancellationReason.LastIndexOf(marker);
            if (idx == 0) return null;
            return idx > 0 ? CancellationReason.Substring(0, idx).Trim() : CancellationReason.Trim();
        }
    }
}
