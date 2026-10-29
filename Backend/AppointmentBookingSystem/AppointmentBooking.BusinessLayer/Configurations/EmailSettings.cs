namespace AppointmentBooking.BusinessLayer.Configurations;

public class EmailSettings
{
    public const string SectionName = "EmailSettings";

    public string SmtpServer { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public string SenderName { get; set; } = "AppointEase";
    public string SenderEmail { get; set; } = "rajshree.ncoresoft@gmail.com";
    public string SmtpUsername { get; set; } = "rajshree.ncoresoft@gmail.com";
    public string SmtpPassword { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
    public string ClientLoginUrl { get; set; } = "http://localhost:4200/auth/login";
}

