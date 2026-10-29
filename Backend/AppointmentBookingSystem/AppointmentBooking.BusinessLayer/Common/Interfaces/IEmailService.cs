namespace AppointmentBooking.BusinessLayer.Common.Interfaces;

public interface IEmailService
{
    /// <summary>
    /// Generic method to send an HTML email asynchronously
    /// </summary>
    Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a branded AppointEase welcome email containing the user's login email and temporary credentials
    /// </summary>
    Task<bool> SendAccountCreatedEmailAsync(string toEmail, string recipientName, string tempPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overload for backwards compatibility
    /// </summary>
    Task<bool> SendAccountCreatedEmailAsync(string toEmail, string recipientName, string role, string tempPassword, CancellationToken cancellationToken = default);
}
