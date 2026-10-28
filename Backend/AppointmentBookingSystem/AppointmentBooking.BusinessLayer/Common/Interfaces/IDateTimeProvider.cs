namespace AppointmentBooking.BusinessLayer.Common.Interfaces;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateTime GetCurrentLocalTime();
    DateTime Today { get; }
    TimeZoneInfo CurrentTimeZone { get; }
    DateTime ConvertUtcToLocal(DateTime utcDateTime);
    DateTime ConvertLocalToUtc(DateTime localDateTime);
}
