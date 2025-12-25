using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;
using bixo_api.Configuration;
using bixo_api.Services.Interfaces;

namespace bixo_api.Services;

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;
    private readonly SendGridClient _client;

    public EmailService(IOptions<EmailSettings> settings, ILogger<EmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        if (!string.IsNullOrEmpty(_settings.ApiKey))
        {
            var options = new SendGridClientOptions { ApiKey = _settings.ApiKey };

            // Set EU data residency if configured
            if (_settings.DataResidency?.ToLower() == "eu")
            {
                options.SetDataResidency("eu");
            }

            _client = new SendGridClient(options);
        }
        else
        {
            _client = null!;
        }
    }

    public async Task SendSupportNotificationAsync(SupportNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey) || string.IsNullOrEmpty(_settings.SupportInboxEmail))
            {
                _logger.LogWarning("Email settings not configured, skipping support notification email");
                return;
            }

            var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
            var to = new EmailAddress(_settings.SupportInboxEmail);
            var subject = $"[Support] {notification.Subject}";
            var htmlContent = BuildEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            if (!string.IsNullOrEmpty(notification.ReplyToEmail))
            {
                msg.SetReplyTo(new EmailAddress(notification.ReplyToEmail));
            }

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Support notification email sent successfully for subject: {Subject}", notification.Subject);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send support notification email for subject: {Subject}", notification.Subject);
            // Do not rethrow - email failure should not block the support message creation
        }
    }

    public async Task SendShortlistCreatedNotificationAsync(ShortlistCreatedNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey) || string.IsNullOrEmpty(_settings.SupportInboxEmail))
            {
                _logger.LogWarning("Email settings not configured, skipping shortlist notification email");
                return;
            }

            var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
            var to = new EmailAddress(_settings.SupportInboxEmail);
            var subject = $"[New Shortlist] {notification.RoleTitle} - {notification.CompanyName}";
            var htmlContent = BuildShortlistEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Shortlist notification email sent for: {RoleTitle}", notification.RoleTitle);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send shortlist notification email for: {RoleTitle}", notification.RoleTitle);
        }
    }

    private static string BuildEmailBody(SupportNotification notification)
    {
        var userInfo = notification.UserId.HasValue
            ? $"<p><strong>User ID:</strong> {notification.UserId}</p>"
            : "<p><strong>User ID:</strong> N/A</p>";

        var replyTo = !string.IsNullOrEmpty(notification.ReplyToEmail)
            ? $"<p><strong>Reply-to:</strong> <a href=\"mailto:{notification.ReplyToEmail}\">{notification.ReplyToEmail}</a></p>"
            : "<p><strong>Reply-to:</strong> Not provided</p>";

        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333;"">
                <h2 style=""color: #2563eb;"">New Support Message</h2>
                <hr style=""border: 1px solid #e5e7eb;"" />
                <p><strong>User Type:</strong> {notification.UserType}</p>
                {userInfo}
                {replyTo}
                <p><strong>Timestamp:</strong> {notification.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC</p>
                <hr style=""border: 1px solid #e5e7eb;"" />
                <h3>Subject</h3>
                <p>{notification.Subject}</p>
                <h3>Message</h3>
                <div style=""background-color: #f9fafb; padding: 15px; border-radius: 5px;"">
                    <p>{notification.Message.Replace("\n", "<br />")}</p>
                </div>
            </body>
            </html>";
    }

    private static string BuildShortlistEmailBody(ShortlistCreatedNotification notification)
    {
        var techStack = notification.TechStack.Count > 0
            ? string.Join(", ", notification.TechStack)
            : "Not specified";

        var location = notification.IsRemote
            ? "Remote"
            : (!string.IsNullOrEmpty(notification.Location) ? notification.Location : "Not specified");

        var notes = !string.IsNullOrEmpty(notification.AdditionalNotes)
            ? $@"<h3>Additional Notes</h3>
                <div style=""background-color: #f9fafb; padding: 15px; border-radius: 5px;"">
                    <p>{notification.AdditionalNotes.Replace("\n", "<br />")}</p>
                </div>"
            : "";

        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333;"">
                <h2 style=""color: #2563eb;"">New Shortlist Request</h2>
                <hr style=""border: 1px solid #e5e7eb;"" />
                <p><strong>Company:</strong> {notification.CompanyName}</p>
                <p><strong>Role:</strong> {notification.RoleTitle}</p>
                <p><strong>Seniority:</strong> {notification.Seniority ?? "Not specified"}</p>
                <p><strong>Location:</strong> {location}</p>
                <p><strong>Tech Stack:</strong> {techStack}</p>
                <p><strong>Shortlist ID:</strong> {notification.ShortlistId}</p>
                <p><strong>Created:</strong> {notification.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC</p>
                {notes}
            </body>
            </html>";
    }
}
