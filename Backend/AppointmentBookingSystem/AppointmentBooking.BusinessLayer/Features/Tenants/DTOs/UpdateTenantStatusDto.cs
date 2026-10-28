using AppointmentBooking.DomainLayer.Enums;
using System.ComponentModel.DataAnnotations;

namespace AppointmentBooking.BusinessLayer.Features.Tenants.DTOs;

public class UpdateTenantStatusDto
{
    [Required]
    public TenantStatus Status { get; set; }
}
