namespace AppointmentBooking.BusinessLayer.Features.Appointments.DTOs;

public class AppointmentStatsDto
{
    public int TotalAppointments { get; set; }
    public int TodayAppointments { get; set; }
    public int UpcomingAppointments { get; set; }
    public int CompletedAppointments { get; set; }
    public int CancelledAppointments { get; set; }
}
