using AppointmentBooking.BusinessLayer.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace AppointmentBooking.BusinessLayer.Common.Services;

public class DateTimeProvider : IDateTimeProvider
{
    private readonly TimeZoneInfo _timeZoneInfo;

    public DateTimeProvider(IConfiguration configuration)
    {
        var configuredTz = configuration["TimeZoneSettings:DefaultTimeZoneId"];
        _timeZoneInfo = ResolveTimeZone(configuredTz);
    }

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime GetCurrentLocalTime() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZoneInfo);

    public DateTime Today => GetCurrentLocalTime().Date;

    public TimeZoneInfo CurrentTimeZone => _timeZoneInfo;

    public DateTime ConvertUtcToLocal(DateTime utcDateTime)
    {
        var utc = utcDateTime.Kind == DateTimeKind.Utc 
            ? utcDateTime 
            : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, _timeZoneInfo);
    }

    public DateTime ConvertLocalToUtc(DateTime localDateTime)
    {
        var unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, _timeZoneInfo);
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            timeZoneId = "India Standard Time";
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Linux / IANA fallback mapping
            if (timeZoneId.Equals("India Standard Time", StringComparison.OrdinalIgnoreCase))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); } catch { }
            }
            else if (timeZoneId.Equals("Asia/Kolkata", StringComparison.OrdinalIgnoreCase))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); } catch { }
            }

            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
