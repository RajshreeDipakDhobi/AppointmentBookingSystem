namespace AppointmentBooking.BusinessLayer.Configurations;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "AppointmentBookingAPI";
    public string Audience { get; set; } = "AppointmentBookingClient";
    public int AccessTokenExpiryMinutes { get; set; } = 60;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}
