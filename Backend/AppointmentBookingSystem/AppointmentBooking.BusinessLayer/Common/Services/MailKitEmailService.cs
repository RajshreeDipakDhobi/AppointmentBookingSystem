using AppointmentBooking.BusinessLayer.Common.Interfaces;
using AppointmentBooking.BusinessLayer.Configurations;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using System.Text;

namespace AppointmentBooking.BusinessLayer.Common.Services;

public class MailKitEmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<MailKitEmailService> _logger;

    public MailKitEmailService(EmailSettings settings, ILogger<MailKitEmailService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<bool> SendEmailAsync(
        string toEmail, 
        string toName, 
        string subject, 
        string htmlBody, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            _logger.LogWarning("Email sending aborted: Recipient email address is null or empty.");
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
            message.To.Add(new MailboxAddress(toName ?? toEmail, toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();

            // Accept all SSL certificates if needed for local test resilience, or rely on system trust
            client.ServerCertificateValidationCallback = (s, c, h, e) => true;

            var cleanPassword = _settings.SmtpPassword?.Replace(" ", "").Trim() ?? string.Empty;

            _logger.LogInformation("Connecting to SMTP server {Host}:{Port} with STARTTLS...", _settings.SmtpServer, _settings.SmtpPort);
            
            await client.ConnectAsync(_settings.SmtpServer, _settings.SmtpPort, SecureSocketOptions.StartTls, cancellationToken);
            await client.AuthenticateAsync(_settings.SmtpUsername, cleanPassword, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Email successfully dispatched to {ToEmail} with Subject: \"{Subject}\"", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver email to {ToEmail}. Reason: {ErrorMessage}", toEmail, ex.Message);
            return false;
        }
    }

    public Task<bool> SendAccountCreatedEmailAsync(
        string toEmail, 
        string recipientName, 
        string tempPassword, 
        CancellationToken cancellationToken = default)
    {
        return SendAccountCreatedEmailAsync(toEmail, recipientName, string.Empty, tempPassword, cancellationToken);
    }

    public async Task<bool> SendAccountCreatedEmailAsync(
        string toEmail, 
        string recipientName, 
        string role, 
        string tempPassword, 
        CancellationToken cancellationToken = default)
    {
        var subject = "Welcome to AppointEase - Your Account Credentials";
        var htmlContent = BuildAccountCreatedHtmlTemplate(recipientName, toEmail, tempPassword, _settings.ClientLoginUrl);

        return await SendEmailAsync(toEmail, recipientName, subject, htmlContent, cancellationToken);
    }

    private string BuildAccountCreatedHtmlTemplate(
        string name, 
        string email, 
        string password, 
        string loginUrl)
    {
        var sb = new StringBuilder();

        sb.Append($@"
<!DOCTYPE html PUBLIC ""-//W3C//DTD XHTML 1.0 Transitional//EN"" ""http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd"">
<html xmlns=""http://www.w3.org/1999/xhtml"" lang=""en"">
<head>
  <meta http-equiv=""Content-Type"" content=""text/html; charset=UTF-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
  <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"" />
  <meta name=""x-apple-disable-message-reformatting"" />
  <meta name=""format-detection"" content=""telephone=no,address=no,email=no,date=no,url=no"" />
  <title>Welcome to AppointEase</title>
  <style type=""text/css"">
    html, body {{
      margin: 0 !important;
      padding: 0 !important;
      height: 100% !important;
      width: 100% !important;
      background-color: #f4f7f5;
      -webkit-text-size-adjust: 100%;
      -ms-text-size-adjust: 100%;
    }}
    table, td {{
      border-collapse: collapse !important;
      mso-table-lspace: 0pt;
      mso-table-rspace: 0pt;
    }}
    img {{
      border: 0;
      height: auto;
      line-height: 100%;
      outline: none;
      text-decoration: none;
      -ms-interpolation-mode: bicubic;
    }}
    * {{
      box-sizing: border-box;
    }}
    @media only screen and (max-width: 600px) {{
      .email-container {{
        width: 100% !important;
        max-width: 100% !important;
        border-radius: 8px !important;
      }}
      .email-wrapper-td {{
        padding: 10px 8px !important;
      }}
      .email-header-td {{
        padding: 22px 14px !important;
      }}
      .email-header-title {{
        font-size: 20px !important;
      }}
      .email-body-td {{
        padding: 18px 14px !important;
      }}
      .credentials-box-td {{
        padding: 14px 12px !important;
      }}
      .btn-responsive {{
        display: block !important;
        width: 100% !important;
        text-align: center !important;
        box-sizing: border-box !important;
        padding: 12px 16px !important;
      }}
    }}
  </style>
</head>
<body style=""margin: 0; padding: 0; background-color: #f4f7f5; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">
  <!-- Outer Wrapper Table -->
  <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""background-color: #f4f7f5; width: 100% !important; min-width: 100%; margin: 0; padding: 0; table-layout: fixed;"">
    <tr>
      <td align=""center"" class=""email-wrapper-td"" style=""padding: 24px 12px; background-color: #f4f7f5;"">
        
        <!-- Center Card Container (Max Width 580px, 100% Fluid on Mobile) -->
        <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" class=""email-container"" style=""max-width: 580px; width: 100%; background-color: #ffffff; border-radius: 14px; overflow: hidden; border: 1px solid #e2ece6; box-shadow: 0 4px 20px rgba(24, 59, 50, 0.06);"">
          
          <!-- Header -->
          <tr>
            <td align=""center"" class=""email-header-td"" style=""background: linear-gradient(135deg, #183b32 0%, #1e5142 100%); background-color: #1e5142; padding: 30px 24px; text-align: center; color: #ffffff;"">
              <h1 class=""email-header-title"" style=""margin: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 24px; font-weight: 700; color: #ffffff; letter-spacing: -0.5px;"">AppointEase</h1>
              <p style=""margin: 6px 0 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 12px; color: #b8dacb; letter-spacing: 0.5px; text-transform: uppercase; font-weight: 600;"">Luxury Practice &amp; Appointment Booking</p>
            </td>
          </tr>

          <!-- Main Body -->
          <tr>
            <td class=""email-body-td"" style=""padding: 28px 24px; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; color: #1e3a34; font-size: 14px; line-height: 1.6;"">
              
              <div style=""font-size: 17px; font-weight: 700; color: #183b32; margin-bottom: 10px;"">
                Hello {System.Net.WebUtility.HtmlEncode(name)},
              </div>

              <p style=""margin: 0 0 20px; font-size: 14px; color: #4b635c; line-height: 1.6;"">
                Your new AppointEase account has been successfully provisioned. You can now access your dashboard and schedule appointments using the credentials below:
              </p>

              <!-- Credentials Card -->
              <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""background-color: #f8faf9; border: 1px solid #e2ece6; border-left: 4px solid #1e5142; border-radius: 10px; margin: 0 0 20px; width: 100%; table-layout: fixed;"">
                <tr>
                  <td class=""credentials-box-td"" style=""padding: 18px 18px;"">
                    <div style=""font-size: 12px; font-weight: 700; color: #1e5142; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 14px;"">
                      Account Credentials
                    </div>

                    <!-- Email Block -->
                    <div style=""margin-bottom: 14px;"">
                      <div style=""font-size: 11px; font-weight: 700; color: #5b756c; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 3px;"">
                        Registered Email:
                      </div>
                      <div style=""font-size: 14px; font-weight: 700; color: #183b32; word-break: break-all; overflow-wrap: anywhere;"">
                        {System.Net.WebUtility.HtmlEncode(email)}
                      </div>
                    </div>

                    <!-- Password Block -->
                    <div>
                      <div style=""font-size: 11px; font-weight: 700; color: #5b756c; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 3px;"">
                        Temporary Password:
                      </div>
                      <div>
                        <span style=""display: inline-block; font-family: Consolas, 'Courier New', monospace; font-size: 15px; font-weight: 700; background-color: #eef5f1; color: #1e5142; border: 1px dashed #b8dacb; padding: 4px 12px; border-radius: 6px; letter-spacing: 1px; word-break: break-all; max-width: 100%; box-sizing: border-box;"">
                          {System.Net.WebUtility.HtmlEncode(password)}
                        </span>
                      </div>
                    </div>
                  </td>
                </tr>
              </table>

              <!-- Security Notice -->
              <div style=""background-color: #fffbeb; border: 1px solid #fef3c7; border-radius: 8px; padding: 12px 14px; font-size: 13px; color: #92400e; margin: 0 0 22px; line-height: 1.5;"">
                <strong style=""color: #78350f;"">Security Notice:</strong> Please log in to your account and change your temporary password immediately from your profile settings.
              </div>

              <!-- CTA Button -->
              <div style=""text-align: center; margin: 0 0 22px;"">
                <a href=""{System.Net.WebUtility.HtmlEncode(loginUrl)}"" class=""btn-responsive"" target=""_blank"" style=""display: inline-block; background-color: #1e5142; color: #ffffff !important; text-decoration: none; font-size: 14px; font-weight: 700; padding: 12px 28px; border-radius: 8px; box-shadow: 0 4px 14px rgba(30, 81, 66, 0.25); text-align: center;"">
                  Log In to AppointEase &rarr;
                </a>
              </div>

              <p style=""margin: 0; font-size: 12px; color: #7b968e; line-height: 1.5;"">
                If you did not request or expect this account creation, please contact our support team immediately.
              </p>
            </td>
          </tr>

          <!-- Footer -->
          <tr>
            <td align=""center"" style=""background-color: #f8faf9; border-top: 1px solid #e2ece6; padding: 18px 20px; text-align: center; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 12px; color: #7b968e; line-height: 1.5;"">
              <p style=""margin: 0 0 6px;"">
                Need assistance? Contact us at <a href=""mailto:{_settings.SenderEmail}"" style=""color: #1e5142; text-decoration: underline;"">{_settings.SenderEmail}</a>
              </p>
              <p style=""margin: 0;"">
                &copy; {DateTime.UtcNow.Year} AppointEase Platform. All rights reserved.
              </p>
            </td>
          </tr>

        </table>

      </td>
    </tr>
  </table>
</body>
</html>");

        return sb.ToString();
    }
}

