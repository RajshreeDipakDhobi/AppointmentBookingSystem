using AppointmentBooking.DomainLayer.Common;
using AppointmentBooking.DomainLayer.Enums;

namespace AppointmentBooking.DomainLayer.Entities;

public class Appointment : BaseEntity
{
    public int TenantId { get; set; }

    // Human-readable unique appointment ID (e.g., "APT-20261014-4821")
    public string AppointmentNumber { get; set; } = string.Empty;

    // Service Provider relation (Nullable when broadcasted to all specialists until accepted)
    public int? ServiceProviderId { get; set; }
    public virtual ServiceProvider? ServiceProvider { get; set; }

    // Service relation (Linked to master Service catalog)
    public int? ServiceId { get; set; }
    public virtual Service? Service { get; set; }


    // Customer relation (Single source of truth in User entity - Required)
    public int CustomerId { get; set; }
    public virtual User Customer { get; set; } = null!;

    public string? Reason { get; set; }

    // Appointment Schedule Timing
    public DateTime AppointmentDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    // Current Appointment Lifecycle Status
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Booked;

    // Cancellation, Rescheduling & Approval/Rejection Audit Details
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }

    public int? RescheduledFromAppointmentId { get; set; }
    public virtual Appointment? RescheduledFromAppointment { get; set; }

    // Provider-Initiated Reschedule Proposal Details
    public DateTime? ProposedDate { get; set; }
    public TimeSpan? ProposedStartTime { get; set; }
    public TimeSpan? ProposedEndTime { get; set; }
    public string? RescheduleNotes { get; set; }

    // Concurrency token to prevent duplicate bookings during simultaneous requests
    public byte[]? RowVersion { get; set; }
}
