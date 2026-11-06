namespace AppointmentBooking.BusinessLayer.Features.Calendar.DTOs;

public class CalendarPermissionsDto
{
    /// <summary>
    /// Can view schedule and appointments across all specialists in the organization
    /// </summary>
    public bool CanViewAllProviders { get; set; }

    /// <summary>
    /// Can click empty slot to initiate a new appointment booking
    /// </summary>
    public bool CanBook { get; set; }

    /// <summary>
    /// Can modify lifecycle: Approve, Reschedule, Cancel appointments
    /// (True for ServiceProvider on their appointments; False for Admin who only views & books in the calendar)
    /// </summary>
    public bool CanManageAppointments { get; set; }

    /// <summary>
    /// Can edit provider availability / working hours / breaks
    /// </summary>
    public bool CanEditAvailability { get; set; }
}

