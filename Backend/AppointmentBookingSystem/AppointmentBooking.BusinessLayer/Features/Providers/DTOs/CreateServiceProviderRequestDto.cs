using System.ComponentModel.DataAnnotations;

namespace AppointmentBooking.BusinessLayer.Features.Providers.DTOs;

public class CreateServiceProviderRequestDto
{
    [Required(ErrorMessage = "Provider name is required")]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Specialization / Service Type is required")]
    [StringLength(100)]
    public string ServiceType { get; set; } = string.Empty;

    public int? ServiceId { get; set; }
    public List<int>? ServiceIds { get; set; } = new();

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

    /// <summary>
    /// Optional list of off days (0 = Sunday, 1 = Monday, ..., 6 = Saturday).
    /// If null or empty, defaults to Sunday off ([0]), Monday to Saturday working.
    /// </summary>
    public List<int>? OffDays { get; set; }

    /// <summary>
    /// Default shift start time (e.g. "10:00"). Defaults to "10:00:00".
    /// </summary>
    public string? StartTime { get; set; } = "10:00";

    /// <summary>
    /// Default shift end time (e.g. "17:00"). Defaults to "17:00:00".
    /// </summary>
    public string? EndTime { get; set; } = "17:00";

    /// <summary>
    /// Default break window start time (e.g. "13:00"). Defaults to "13:00".
    /// </summary>
    public string? BreakStartTime { get; set; } = "13:00";

    /// <summary>
    /// Default break window end time (e.g. "14:00"). Defaults to "14:00".
    /// </summary>
    public string? BreakEndTime { get; set; } = "14:00";

    /// <summary>
    /// Optional slot duration step in minutes (e.g. 15, 30, 45, 60, 90).
    /// If null or 0, defaults to assigned primary service duration or 30 minutes.
    /// </summary>
    public int? SlotDurationMinutes { get; set; }
}
