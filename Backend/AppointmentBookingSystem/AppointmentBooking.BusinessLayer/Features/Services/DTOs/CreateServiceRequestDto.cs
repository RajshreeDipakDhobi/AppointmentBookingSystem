using System.ComponentModel.DataAnnotations;

namespace AppointmentBooking.BusinessLayer.Features.Services.DTOs;

public class CreateServiceRequestDto
{
    [Required(ErrorMessage = "Service name is required.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Service name must be between 2 and 150 characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters.")]
    public string? Description { get; set; }

    [Range(5, 480, ErrorMessage = "Duration must be between 5 and 480 minutes.")]
    public int DurationMinutes { get; set; } = 30;

    [Range(0, 1000000, ErrorMessage = "Price must be a valid positive amount.")]
    public decimal Price { get; set; } = 0.00m;

    public bool IsActive { get; set; } = true;
}

