using AppointmentBooking.DomainLayer.Enums;

namespace AppointmentBooking.BusinessLayer.Features.Appointments.DTOs;

public class AppointmentDto
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string AppointmentNumber { get; set; } = string.Empty;
    public string? BookingGroupId { get; set; }

    public int? ServiceProviderId { get; set; }
    public string ServiceProviderName { get; set; } = string.Empty;
    public string ServiceProviderType { get; set; } = string.Empty;
    public string? ServiceProviderProfilePictureUrl { get; set; }

    public int? ServiceId { get; set; }
    public string? ServiceName { get; set; }
    public decimal? ServicePrice { get; set; }
    public int? ServiceDurationMinutes { get; set; }

    // Multi-Service Cart Items & Aggregates (Urban Company model)
    public decimal TotalAmount { get; set; }
    public int TotalDurationMinutes { get; set; }
    public List<AppointmentItemDto> Items { get; set; } = new();
    public List<string> ServiceNames => Items.Count > 0 
        ? Items.Select(i => i.ServiceName).Distinct().ToList() 
        : (ServiceName != null ? new List<string> { ServiceName } : new List<string>());

    public bool IsBroadcast => !ServiceProviderId.HasValue;
    
    private DateTime UtcCreatedAt => CreatedAt.Kind == DateTimeKind.Utc 
        ? CreatedAt 
        : DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc);

    public bool IsExpired => Status == AppointmentStatus.Booked && !ServiceProviderId.HasValue && UtcCreatedAt.AddMinutes(10) < DateTime.UtcNow;
    public bool IsAcceptanceWindowActive => !ServiceProviderId.HasValue && Status == AppointmentStatus.Booked && UtcCreatedAt.AddMinutes(10) >= DateTime.UtcNow;
    public int? RemainingSeconds => (!ServiceProviderId.HasValue && Status == AppointmentStatus.Booked)
        ? Math.Max(0, (int)(UtcCreatedAt.AddMinutes(10) - DateTime.UtcNow).TotalSeconds)
        : null;

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
    public string TimeSlotText => FormatSlotTimeRange(StartTime, EndTime);

    public AppointmentStatus Status { get; set; }
    private string? _customStatusText;
    public string StatusText
    {
        get => _customStatusText ?? (Status switch
        {
            AppointmentStatus.Cancelled when CancellationReason != null && (CancellationReason.Contains("10 minutes") || CancellationReason.Contains("specialist") || CancellationReason.Contains("unavailable") || CancellationReason.Contains("System")) => "Specialist Unavailable",
            AppointmentStatus.Pending when !ServiceProviderId.HasValue && UtcCreatedAt.AddMinutes(10) < DateTime.UtcNow => "Specialist Unavailable",
            AppointmentStatus.Pending when !ServiceProviderId.HasValue => "Awaiting Specialist",
            AppointmentStatus.Pending => "Booked",
            AppointmentStatus.Approved => "Approved",
            AppointmentStatus.Completed => "Completed",
            AppointmentStatus.Cancelled => "Cancelled",
            AppointmentStatus.Rescheduled => "Rescheduled",
            AppointmentStatus.RescheduleProposed => "Reschedule Proposed",
            _ => Status.ToString()
        });
        set => _customStatusText = value;
    }

    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }

    public int? RescheduledFromAppointmentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }

    // Provider-Initiated Reschedule Proposal Details
    public DateTime? ProposedDate { get; set; }
    public string? ProposedDateText => ProposedDate?.ToString("yyyy-MM-dd");
    public string? ProposedStartTime { get; set; }
    public string? ProposedEndTime { get; set; }
    public string? ProposedTimeSlotText => (!string.IsNullOrEmpty(ProposedStartTime) && !string.IsNullOrEmpty(ProposedEndTime)) 
        ? FormatSlotTimeRange(ProposedStartTime, ProposedEndTime) 
        : null;
    public string? RescheduleNotes { get; set; }

    // Multi-Part / Multi-Session breakdown & partial acceptance tracking
    public List<AppointmentDto>? JourneyParts { get; set; }
    public bool HasPartialApproval { get; set; }
    public int? ApprovedPartsCount { get; set; }
    public int? PendingPartsCount { get; set; }

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

    private static string FormatSlotTimeRange(string? start, string? end)
    {
        if (string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(end))
            return $"{start} – {end}";
        return $"{FormatTo12Hour(start)} – {FormatTo12Hour(end)}";
    }

    private static string FormatTo12Hour(string time)
    {
        if (string.IsNullOrWhiteSpace(time)) return string.Empty;
        if (time.Contains("AM", StringComparison.OrdinalIgnoreCase) || time.Contains("PM", StringComparison.OrdinalIgnoreCase))
            return time;
        if (TimeOnly.TryParse(time, out var t))
            return t.ToString("hh:mm tt");
        if (DateTime.TryParse(time, out var dt))
            return dt.ToString("hh:mm tt");
        return time;
    }
}
