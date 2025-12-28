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

    public async Task SendCompanyWelcomeEmailAsync(CompanyWelcomeNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping company welcome email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.WelcomeFromEmail)
                ? _settings.WelcomeFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = "Welcome to Bixo";
            var htmlContent = BuildCompanyWelcomeEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);
            msg.SetReplyTo(new EmailAddress(fromEmail, _settings.FromName));

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Company welcome email sent to: {Email}", notification.Email);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send company welcome email to: {Email}", notification.Email);
        }
    }

    private static string BuildCompanyWelcomeEmailBody(CompanyWelcomeNotification notification)
    {
        var greeting = !string.IsNullOrEmpty(notification.CompanyName)
            ? $"Hi {notification.CompanyName}"
            : "Hi";

        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <p>{greeting},</p>

                <p>Welcome to <strong>Bixo</strong> 👋<br />
                We're glad to have you on board.</p>

                <p>Bixo helps companies discover the <em>right</em> candidates — not just more candidates.<br />
                No noise, no endless CVs — just focused shortlists you can trust.</p>

                <h2 style=""color: #1f2937; margin-top: 32px; font-size: 18px;"">What you can do next</h2>

                <p>Here's how to get value quickly:</p>

                <ul style=""padding-left: 20px;"">
                    <li>🔍 <strong>Browse talent</strong> that matches your needs</li>
                    <li>⭐ <strong>Shortlist candidates</strong> you're interested in</li>
                    <li>🤝 <strong>Connect directly</strong> when you're ready</li>
                </ul>

                <p>You're always in control — explore at your own pace.</p>

                <p style=""margin-top: 32px;"">
                    <a href=""{notification.DashboardUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">Get started</a>
                </p>

                <p style=""margin-top: 32px;"">If you have questions or need help finding the right profiles, just reply to this email.<br />
                We're here to help.</p>

                <p style=""margin-top: 32px;"">Welcome again,<br />
                <strong>The Bixo Team</strong><br />
                <a href=""mailto:welcome@bixo.io"" style=""color: #6b7280;"">welcome@bixo.io</a></p>
            </body>
            </html>";
    }

    // === Shortlist Status Email Events ===

    public async Task SendShortlistPricingReadyEmailAsync(ShortlistPricingReadyNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping pricing ready email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.ShortlistFromEmail)
                ? _settings.ShortlistFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = $"Your {notification.RoleTitle} shortlist is ready";
            var htmlContent = BuildShortlistPricingReadyEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Pricing ready email sent to: {Email} for shortlist {ShortlistId}",
                    notification.Email, notification.ShortlistId);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send pricing ready email to: {Email}", notification.Email);
        }
    }

    private static string BuildShortlistPricingReadyEmailBody(ShortlistPricingReadyNotification notification)
    {
        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">Your shortlist is ready</h1>

                <p>Hi {notification.CompanyName},</p>

                <p>We've prepared a focused shortlist of candidates for your <strong>{notification.RoleTitle}</strong> role, prioritizing quality over volume.</p>

                <p style=""margin-top: 24px;"">Each candidate has been:</p>
                <ul style=""padding-left: 20px; color: #4b5563;"">
                    <li>Hand-reviewed by our team</li>
                    <li>Matched to your specific requirements</li>
                    <li>Verified for availability</li>
                </ul>

                <p style=""margin-top: 24px;"">Review the shortlist and decide if it's right for you.</p>

                <p style=""margin-top: 32px;"">
                    <a href=""{notification.ShortlistUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">Review shortlist</a>
                </p>

                <p style=""margin-top: 24px; color: #6b7280; font-size: 14px;"">If this shortlist doesn't feel right, you can decline — no charge.</p>

                <p style=""margin-top: 32px; color: #6b7280;"">— The Bixo Team</p>
            </body>
            </html>";
    }

    public async Task SendShortlistAuthorizationRequiredEmailAsync(ShortlistAuthorizationRequiredNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping authorization required email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.ShortlistFromEmail)
                ? _settings.ShortlistFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = "Approval needed to continue your shortlist request";
            var htmlContent = BuildShortlistAuthorizationRequiredEmailBody(notification.ShortlistUrl);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Authorization required email sent to: {Email} for shortlist {ShortlistId}",
                    notification.Email, notification.ShortlistId);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send authorization required email to: {Email}", notification.Email);
        }
    }

    private static string BuildShortlistAuthorizationRequiredEmailBody(string shortlistUrl)
    {
        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">Approval needed to continue</h1>

                <p>To proceed with delivering your shortlist, we need your approval to authorize payment.</p>

                <p>This is a temporary authorization — you will only be charged once the shortlist is delivered.</p>

                <p style=""margin-top: 32px;"">
                    <a href=""{shortlistUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">Review &amp; approve</a>
                </p>

                <p style=""margin-top: 32px; color: #6b7280;"">— Bixo</p>
            </body>
            </html>";
    }

    public async Task SendShortlistDeliveredEmailAsync(ShortlistDeliveredNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping shortlist delivered email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.ShortlistFromEmail)
                ? _settings.ShortlistFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = $"Your {notification.RoleTitle} candidates are ready";
            var htmlContent = BuildShortlistDeliveredEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Shortlist delivered email sent to: {Email} for shortlist {ShortlistId}",
                    notification.Email, notification.ShortlistId);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send shortlist delivered email to: {Email}", notification.Email);
        }
    }

    private static string BuildShortlistDeliveredEmailBody(ShortlistDeliveredNotification notification)
    {
        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">Your candidates are ready</h1>

                <p>Hi {notification.CompanyName},</p>

                <p>Your <strong>{notification.RoleTitle}</strong> shortlist has been delivered. Full candidate profiles are now unlocked.</p>

                <p style=""margin-top: 24px;"">What you can do now:</p>
                <ul style=""padding-left: 20px; color: #4b5563;"">
                    <li>View complete candidate profiles</li>
                    <li>See contact details and availability</li>
                    <li>Reach out directly to candidates you're interested in</li>
                </ul>

                <p style=""margin-top: 32px;"">
                    <a href=""{notification.ShortlistUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">View candidates</a>
                </p>

                <p style=""margin-top: 32px;"">Good luck with your hiring!</p>

                <p style=""margin-top: 24px; color: #6b7280;"">— The Bixo Team</p>
            </body>
            </html>";
    }

    public async Task SendCandidateWelcomeEmailAsync(CandidateWelcomeNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping candidate welcome email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.WelcomeFromEmail)
                ? _settings.WelcomeFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = "Welcome to Bixo";
            var htmlContent = BuildCandidateWelcomeEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);
            msg.SetReplyTo(new EmailAddress(fromEmail, _settings.FromName));

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Candidate welcome email sent to: {Email}", notification.Email);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send candidate welcome email to: {Email}", notification.Email);
        }
    }

    private static string BuildCandidateWelcomeEmailBody(CandidateWelcomeNotification notification)
    {
        var greeting = !string.IsNullOrEmpty(notification.FirstName)
            ? $"Hi {notification.FirstName}"
            : "Hi";

        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <p>{greeting},</p>

                <p>Welcome to <strong>Bixo</strong> 👋<br />
                We're happy you're here.</p>

                <p>Bixo is built to help candidates get discovered for the <em>right opportunities</em> — without spam, pressure, or endless applications.</p>

                <h2 style=""color: #1f2937; margin-top: 32px; font-size: 18px;"">How Bixo works for you</h2>

                <ul style=""padding-left: 20px;"">
                    <li>🧭 Companies <strong>browse profiles</strong>, not the other way around</li>
                    <li>✨ A strong profile increases your chances of being shortlisted</li>
                    <li>🔔 You'll know when companies show interest</li>
                </ul>

                <h2 style=""color: #1f2937; margin-top: 32px; font-size: 18px;"">Your next step</h2>

                <p>Take a few minutes to complete your profile — it makes a big difference.</p>

                <p style=""margin-top: 32px;"">
                    <a href=""{notification.ProfileUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">Complete your profile</a>
                </p>

                <p style=""margin-top: 32px;"">If you ever need help or have feedback, just reply to this email.</p>

                <p style=""margin-top: 32px;"">Welcome again,<br />
                <strong>The Bixo Team</strong><br />
                <a href=""mailto:welcome@bixo.io"" style=""color: #6b7280;"">welcome@bixo.io</a></p>
            </body>
            </html>";
    }

    public async Task SendCandidateProfileActiveEmailAsync(CandidateProfileActiveNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping candidate profile active email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.WelcomeFromEmail)
                ? _settings.WelcomeFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = "Your Bixo profile is approved 🎉";
            var htmlContent = BuildCandidateProfileActiveEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);
            msg.SetReplyTo(new EmailAddress(fromEmail, _settings.FromName));

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Candidate profile active email sent to: {Email}", notification.Email);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send candidate profile active email to: {Email}", notification.Email);
        }
    }

    private static string BuildCandidateProfileActiveEmailBody(CandidateProfileActiveNotification notification)
    {
        var greeting = !string.IsNullOrEmpty(notification.FirstName)
            ? $"Hi {notification.FirstName}"
            : "Hi";

        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <p>{greeting},</p>

                <p>Good news — your <strong>Bixo profile has been approved</strong> and is now visible to companies 🎉</p>

                <p style=""margin-top: 24px;"">What this means:</p>

                <ul style=""padding-left: 20px;"">
                    <li>Companies can now view your profile</li>
                    <li>You may start receiving interest and shortlist notifications</li>
                </ul>

                <p style=""margin-top: 24px;"">To improve your chances, make sure your profile is complete and up to date.</p>

                <p style=""margin-top: 32px;"">
                    <a href=""{notification.ProfileUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">View your profile</a>
                </p>

                <p style=""margin-top: 32px;"">If you have questions, just reply to this email — we're happy to help.</p>

                <p style=""margin-top: 32px;"">Best,<br />
                <strong>The Bixo Team</strong><br />
                <a href=""mailto:welcome@bixo.io"" style=""color: #6b7280;"">welcome@bixo.io</a></p>
            </body>
            </html>";
    }

    public async Task SendCandidateProfileRejectedEmailAsync(CandidateProfileRejectedNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping candidate profile rejected email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.WelcomeFromEmail)
                ? _settings.WelcomeFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = "Update on your Bixo profile";
            var htmlContent = BuildCandidateProfileRejectedEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);
            msg.SetReplyTo(new EmailAddress(fromEmail, _settings.FromName));

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Candidate profile rejected email sent to: {Email}", notification.Email);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send candidate profile rejected email to: {Email}", notification.Email);
        }
    }

    private static string BuildCandidateProfileRejectedEmailBody(CandidateProfileRejectedNotification notification)
    {
        var greeting = !string.IsNullOrEmpty(notification.FirstName)
            ? $"Hi {notification.FirstName}"
            : "Hi";

        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <p>{greeting},</p>

                <p>Thank you for your interest in Bixo.</p>

                <p>After reviewing your profile, we've decided not to activate it on our platform at this time.</p>

                <p style=""margin-top: 24px;"">This could be due to:</p>

                <ul style=""padding-left: 20px;"">
                    <li>Incomplete or unclear profile information</li>
                    <li>CV quality or formatting issues</li>
                    <li>Experience level not matching our current demand</li>
                </ul>

                <p style=""margin-top: 24px;"">If you believe this was a mistake or would like to update your profile, please reply to this email and we'll be happy to review it again.</p>

                <p style=""margin-top: 32px;"">
                    <a href=""{notification.ProfileUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">Update your profile</a>
                </p>

                <p style=""margin-top: 32px;"">Best,<br />
                <strong>The Bixo Team</strong><br />
                <a href=""mailto:welcome@bixo.io"" style=""color: #6b7280;"">welcome@bixo.io</a></p>
            </body>
            </html>";
    }

    public async Task SendAdminNewCandidateNotificationAsync(AdminNewCandidateNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey) || string.IsNullOrEmpty(_settings.AdminInboxEmail))
            {
                _logger.LogWarning("Email settings not configured, skipping admin new candidate notification");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.RegisterFromEmail)
                ? _settings.RegisterFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(_settings.AdminInboxEmail);
            var name = $"{notification.FirstName} {notification.LastName}".Trim();
            var subject = !string.IsNullOrEmpty(name)
                ? $"New candidate joined: {name}"
                : "New candidate joined Bixo";
            var htmlContent = BuildAdminNewCandidateEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Admin new candidate notification sent for: {Email}", notification.Email);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send admin new candidate notification for: {Email}", notification.Email);
        }
    }

    private static string BuildAdminNewCandidateEmailBody(AdminNewCandidateNotification notification)
    {
        var name = $"{notification.FirstName} {notification.LastName}".Trim();
        var displayName = string.IsNullOrEmpty(name) ? "A new candidate" : name;

        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">New candidate registration</h1>

                <p><strong>{displayName}</strong> just signed up on Bixo.</p>

                <div style=""background-color: #f9fafb; padding: 20px; border-radius: 8px; margin: 24px 0;"">
                    <p style=""margin: 0 0 8px 0;""><strong>Email:</strong> {notification.Email}</p>
                    <p style=""margin: 0;""><strong>Registered:</strong> {notification.CreatedAt:MMMM dd, yyyy} at {notification.CreatedAt:HH:mm} UTC</p>
                </div>

                <p style=""color: #6b7280;"">Their profile will need CV upload and approval before becoming active.</p>

                <p style=""margin-top: 32px; color: #6b7280;"">— Bixo</p>
            </body>
            </html>";
    }

    public async Task SendAdminNewCompanyNotificationAsync(AdminNewCompanyNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey) || string.IsNullOrEmpty(_settings.AdminInboxEmail))
            {
                _logger.LogWarning("Email settings not configured, skipping admin new company notification");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.RegisterFromEmail)
                ? _settings.RegisterFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(_settings.AdminInboxEmail);
            var subject = $"New company joined: {notification.CompanyName}";
            var htmlContent = BuildAdminNewCompanyEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Admin new company notification sent for: {CompanyName}", notification.CompanyName);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send admin new company notification for: {CompanyName}", notification.CompanyName);
        }
    }

    private static string BuildAdminNewCompanyEmailBody(AdminNewCompanyNotification notification)
    {
        var industry = !string.IsNullOrEmpty(notification.Industry) ? notification.Industry : "Not specified";

        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">New company registration</h1>

                <p><strong>{notification.CompanyName}</strong> just signed up on Bixo.</p>

                <div style=""background-color: #f9fafb; padding: 20px; border-radius: 8px; margin: 24px 0;"">
                    <p style=""margin: 0 0 8px 0;""><strong>Contact:</strong> {notification.Email}</p>
                    <p style=""margin: 0 0 8px 0;""><strong>Industry:</strong> {industry}</p>
                    <p style=""margin: 0;""><strong>Registered:</strong> {notification.CreatedAt:MMMM dd, yyyy} at {notification.CreatedAt:HH:mm} UTC</p>
                </div>

                <p style=""color: #6b7280;"">They can now request shortlists for their open roles.</p>

                <p style=""margin-top: 32px; color: #6b7280;"">— Bixo</p>
            </body>
            </html>";
    }

    public async Task SendAdminNewShortlistNotificationAsync(AdminNewShortlistNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey) || string.IsNullOrEmpty(_settings.AdminInboxEmail))
            {
                _logger.LogWarning("Email settings not configured, skipping admin new shortlist notification");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.ShortlistFromEmail)
                ? _settings.ShortlistFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(_settings.AdminInboxEmail);
            var subject = $"New shortlist request: {notification.RoleTitle} at {notification.CompanyName}";
            var htmlContent = BuildAdminNewShortlistEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Admin new shortlist notification sent for: {RoleTitle}", notification.RoleTitle);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send admin new shortlist notification for: {RoleTitle}", notification.RoleTitle);
        }
    }

    public async Task SendAdminNotificationAsync(string subject, string message)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey) || string.IsNullOrEmpty(_settings.AdminInboxEmail))
            {
                _logger.LogWarning("Email settings not configured, skipping admin notification: {Subject}", subject);
                return;
            }

            var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
            var to = new EmailAddress(_settings.AdminInboxEmail);
            var emailSubject = $"Bixo alert: {subject}";
            var htmlContent = $@"
                <html>
                <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                    <h1 style=""color: #dc2626; margin-bottom: 24px;"">System alert</h1>

                    <p><strong>{subject}</strong></p>

                    <div style=""background-color: #fef2f2; border: 1px solid #fecaca; padding: 20px; border-radius: 8px; margin: 24px 0;"">
                        <pre style=""white-space: pre-wrap; font-family: monospace; margin: 0; color: #991b1b;"">{message}</pre>
                    </div>

                    <p style=""margin-top: 32px; color: #6b7280;"">— Bixo</p>
                </body>
                </html>";

            var msg = MailHelper.CreateSingleEmail(from, to, emailSubject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Admin notification sent: {Subject}", subject);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send admin notification: {Subject}", subject);
        }
    }

    public async Task SendShortlistNoMatchEmailAsync(ShortlistNoMatchNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping shortlist no-match email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.ShortlistFromEmail)
                ? _settings.ShortlistFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = "Update on your shortlist request";
            var htmlContent = BuildShortlistNoMatchEmailBody(notification.ShortlistUrl);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Shortlist no-match email sent to: {Email} for shortlist {ShortlistId}",
                    notification.Email, notification.ShortlistId);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send shortlist no-match email to: {Email}", notification.Email);
        }
    }

    private static string BuildShortlistNoMatchEmailBody(string shortlistUrl)
    {
        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">Update on your shortlist request</h1>

                <p>We've reviewed your request and searched our current candidate pool, but we're not confident we can deliver a shortlist that meets the quality bar.</p>

                <p style=""font-size: 16px; font-weight: 600; color: #059669;"">You will not be charged for this request.</p>

                <p>You can review next steps from your dashboard.</p>

                <p style=""margin-top: 32px;"">
                    <a href=""{shortlistUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">View request</a>
                </p>

                <p style=""margin-top: 32px; color: #6b7280;"">— Bixo</p>
            </body>
            </html>";
    }

    public async Task SendShortlistAdjustmentSuggestedEmailAsync(ShortlistAdjustmentSuggestedNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping adjustment suggestion email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.ShortlistFromEmail)
                ? _settings.ShortlistFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = $"Suggestion for your {notification.RoleTitle} search";
            var htmlContent = BuildShortlistAdjustmentSuggestedEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Shortlist adjustment suggestion email sent to: {Email} for shortlist {ShortlistId}",
                    notification.Email, notification.ShortlistId);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send adjustment suggestion email to: {Email}", notification.Email);
        }
    }

    private static string BuildShortlistAdjustmentSuggestedEmailBody(ShortlistAdjustmentSuggestedNotification notification)
    {
        var escapedMessage = notification.Message.Replace("\n", "<br />");
        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">Suggestion for your {notification.RoleTitle} search</h1>

                <p>Hi {notification.CompanyName},</p>

                <div style=""background-color: #f9fafb; padding: 20px; border-radius: 8px; margin: 24px 0;"">
                    <p style=""margin: 0;"">{escapedMessage}</p>
                </div>

                <p>You can:</p>
                <ul>
                    <li><a href=""{notification.EditUrl}"" style=""color: #2563eb;"">Update your requirements</a></li>
                    <li><a href=""{notification.CloseUrl}"" style=""color: #2563eb;"">Close this request</a></li>
                </ul>

                <p style=""margin-top: 24px;"">If you have questions, reply to this email.</p>

                <p style=""margin-top: 32px; color: #6b7280;"">— The Bixo Team</p>
            </body>
            </html>";
    }

    public async Task SendShortlistSearchExtendedEmailAsync(ShortlistSearchExtendedNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping search extended email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.ShortlistFromEmail)
                ? _settings.ShortlistFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = $"Update on your {notification.RoleTitle} search";
            var htmlContent = BuildShortlistSearchExtendedEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Shortlist search extended email sent to: {Email} for shortlist {ShortlistId}",
                    notification.Email, notification.ShortlistId);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send search extended email to: {Email}", notification.Email);
        }
    }

    private static string BuildShortlistSearchExtendedEmailBody(ShortlistSearchExtendedNotification notification)
    {
        var escapedMessage = notification.Message.Replace("\n", "<br />");
        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">Update on your {notification.RoleTitle} search</h1>

                <p>Hi {notification.CompanyName},</p>

                <div style=""background-color: #f9fafb; padding: 20px; border-radius: 8px; margin: 24px 0;"">
                    <p style=""margin: 0;"">{escapedMessage}</p>
                </div>

                <p style=""font-weight: 600;"">No action is needed from you.</p>
                <p>We'll be in touch when we have candidates to share.</p>

                <p style=""margin-top: 32px; color: #6b7280;"">— The Bixo Team</p>
            </body>
            </html>";
    }

    public async Task SendShortlistProcessingStartedEmailAsync(ShortlistProcessingStartedNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping processing started email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.ShortlistFromEmail)
                ? _settings.ShortlistFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = $"We're working on your {notification.RoleTitle} shortlist";
            var htmlContent = BuildShortlistProcessingStartedEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Processing started email sent to: {Email} for shortlist {ShortlistId}",
                    notification.Email, notification.ShortlistId);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send processing started email to: {Email}", notification.Email);
        }
    }

    private static string BuildShortlistProcessingStartedEmailBody(ShortlistProcessingStartedNotification notification)
    {
        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">We're working on your shortlist</h1>

                <p>Hi {notification.CompanyName},</p>

                <p>We've started processing your request for a <strong>{notification.RoleTitle}</strong>.</p>

                <p>Our team is reviewing candidates and will have a proposal ready for you soon.</p>

                <p style=""margin-top: 32px;"">
                    <a href=""{notification.ShortlistUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">View request</a>
                </p>

                <p style=""margin-top: 32px; color: #6b7280;"">— The Bixo Team</p>
            </body>
            </html>";
    }

    public async Task SendShortlistPricingApprovedEmailAsync(ShortlistPricingApprovedNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping pricing approved email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.ShortlistFromEmail)
                ? _settings.ShortlistFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = $"Your {notification.RoleTitle} shortlist is confirmed";
            var htmlContent = BuildShortlistPricingApprovedEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Pricing approved email sent to: {Email} for shortlist {ShortlistId}",
                    notification.Email, notification.ShortlistId);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send pricing approved email to: {Email}", notification.Email);
        }
    }

    private static string BuildShortlistPricingApprovedEmailBody(ShortlistPricingApprovedNotification notification)
    {
        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">Your shortlist is confirmed</h1>

                <p>Hi {notification.CompanyName},</p>

                <p>Thanks for approving the pricing for your <strong>{notification.RoleTitle}</strong> shortlist.</p>

                <p>We're now preparing your shortlist for delivery. You'll receive another email once it's ready.</p>

                <p style=""margin-top: 32px;"">
                    <a href=""{notification.ShortlistUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">View request</a>
                </p>

                <p style=""margin-top: 32px; color: #6b7280;"">— The Bixo Team</p>
            </body>
            </html>";
    }

    public async Task SendShortlistCompletedEmailAsync(ShortlistCompletedNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping completed email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.ShortlistFromEmail)
                ? _settings.ShortlistFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = $"Your {notification.RoleTitle} shortlist is complete";
            var htmlContent = BuildShortlistCompletedEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Completed email sent to: {Email} for shortlist {ShortlistId}",
                    notification.Email, notification.ShortlistId);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send completed email to: {Email}", notification.Email);
        }
    }

    private static string BuildShortlistCompletedEmailBody(ShortlistCompletedNotification notification)
    {
        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">Your shortlist is complete</h1>

                <p>Hi {notification.CompanyName},</p>

                <p>Your <strong>{notification.RoleTitle}</strong> shortlist has been completed.</p>

                <p>Thank you for using Bixo. We hope you found great candidates!</p>

                <p style=""margin-top: 24px;"">Ready to hire again? You can request a new shortlist anytime.</p>

                <p style=""margin-top: 32px;"">
                    <a href=""{notification.ShortlistUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">View shortlist</a>
                </p>

                <p style=""margin-top: 32px; color: #6b7280;"">— The Bixo Team</p>
            </body>
            </html>";
    }

    public async Task SendShortlistPricingDeclinedEmailAsync(ShortlistPricingDeclinedNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping pricing declined email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.ShortlistFromEmail)
                ? _settings.ShortlistFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = $"Update on your {notification.RoleTitle} request";
            var htmlContent = BuildShortlistPricingDeclinedEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Pricing declined email sent to: {Email} for shortlist {ShortlistId}",
                    notification.Email, notification.ShortlistId);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send pricing declined email to: {Email}", notification.Email);
        }
    }

    private static string BuildShortlistPricingDeclinedEmailBody(ShortlistPricingDeclinedNotification notification)
    {
        var reasonSection = !string.IsNullOrEmpty(notification.Reason)
            ? $@"<div style=""background-color: #f9fafb; padding: 20px; border-radius: 8px; margin: 24px 0;"">
                    <p style=""margin: 0;""><strong>Your feedback:</strong> {notification.Reason}</p>
                </div>"
            : "";

        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">We've received your feedback</h1>

                <p>Hi {notification.CompanyName},</p>

                <p>We've noted that the pricing proposal for your <strong>{notification.RoleTitle}</strong> shortlist didn't work for you.</p>

                {reasonSection}

                <p>Our team will review and come back with an adjusted proposal.</p>

                <p style=""margin-top: 32px;"">
                    <a href=""{notification.ShortlistUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">View request</a>
                </p>

                <p style=""margin-top: 32px; color: #6b7280;"">— The Bixo Team</p>
            </body>
            </html>";
    }

    private static string BuildAdminNewShortlistEmailBody(AdminNewShortlistNotification notification)
    {
        var techStack = notification.TechStack.Count > 0
            ? string.Join(", ", notification.TechStack)
            : "Not specified";

        var location = notification.IsRemote
            ? "Remote"
            : (!string.IsNullOrEmpty(notification.Location) ? notification.Location : "Not specified");

        var seniority = !string.IsNullOrEmpty(notification.Seniority) ? notification.Seniority : "Not specified";

        var notes = !string.IsNullOrEmpty(notification.AdditionalNotes)
            ? $@"<div style=""margin-top: 16px;"">
                    <p style=""margin: 0 0 8px 0; font-weight: 600;"">Additional notes:</p>
                    <p style=""margin: 0; color: #4b5563;"">{notification.AdditionalNotes.Replace("\n", "<br />")}</p>
                </div>"
            : "";

        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">New shortlist request</h1>

                <p><strong>{notification.CompanyName}</strong> is looking for a <strong>{notification.RoleTitle}</strong>.</p>

                <div style=""background-color: #f9fafb; padding: 20px; border-radius: 8px; margin: 24px 0;"">
                    <p style=""margin: 0 0 8px 0;""><strong>Role:</strong> {notification.RoleTitle}</p>
                    <p style=""margin: 0 0 8px 0;""><strong>Seniority:</strong> {seniority}</p>
                    <p style=""margin: 0 0 8px 0;""><strong>Location:</strong> {location}</p>
                    <p style=""margin: 0 0 8px 0;""><strong>Tech stack:</strong> {techStack}</p>
                    <p style=""margin: 0;""><strong>Requested:</strong> {notification.CreatedAt:MMMM dd, yyyy} at {notification.CreatedAt:HH:mm} UTC</p>
                    {notes}
                </div>

                <p style=""color: #6b7280;"">This request needs to be reviewed and priced before the company can proceed.</p>

                <p style=""margin-top: 32px; color: #6b7280;"">— Bixo</p>
            </body>
            </html>";
    }

    // === Recommendation Emails ===

    public async Task SendRecommendationRequestEmailAsync(RecommendationRequestNotification notification)
    {
        try
        {
            var subject = "Private recommendation request – Bixo";
            var body = BuildRecommendationRequestEmailBody(notification);

            // Use recommendations@ sender for higher trust, with Reply-To to support
            var fromEmail = !string.IsNullOrEmpty(_settings.RecommendationsFromEmail)
                ? _settings.RecommendationsFromEmail
                : _settings.FromEmail;

            var msg = new SendGridMessage();
            msg.SetFrom(new EmailAddress(fromEmail, "Bixo Recommendations"));
            msg.SetReplyTo(new EmailAddress(_settings.SupportInboxEmail, "Bixo Support"));
            msg.AddTo(new EmailAddress(notification.Email, notification.RecommenderName));
            msg.SetSubject(subject);
            msg.AddContent(MimeType.Html, body);

            var response = await _client.SendEmailAsync(msg);

            if (response.StatusCode != System.Net.HttpStatusCode.OK &&
                response.StatusCode != System.Net.HttpStatusCode.Accepted)
            {
                _logger.LogWarning("Failed to send recommendation request email: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send recommendation request email to: {Email}", notification.Email);
        }
    }

    private static string BuildRecommendationRequestEmailBody(RecommendationRequestNotification notification)
    {
        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">Private recommendation request</h1>

                <p><strong>{notification.CandidateName}</strong> is building a private profile on Bixo, a curated hiring platform for senior engineers.</p>

                <p>Would you be willing to write a short private recommendation?</p>

                <p style=""color: #6b7280;"">This recommendation is private and will only be shared with companies with {notification.CandidateName}'s explicit approval.</p>

                <p style=""margin-top: 32px;"">
                    <a href=""{notification.RecommendationUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">Write recommendation</a>
                </p>

                <p style=""margin-top: 32px; color: #6b7280; font-size: 14px;"">This link will expire in 30 days.</p>

                <p style=""margin-top: 32px; color: #6b7280;"">— Bixo</p>
            </body>
            </html>";
    }

    public async Task SendRecommendationReceivedEmailAsync(RecommendationReceivedNotification notification)
    {
        try
        {
            var subject = "New recommendation received";
            var body = BuildRecommendationReceivedEmailBody(notification);

            // Use recommendations@ sender for consistency, with Reply-To to support
            var fromEmail = !string.IsNullOrEmpty(_settings.RecommendationsFromEmail)
                ? _settings.RecommendationsFromEmail
                : _settings.FromEmail;

            var msg = new SendGridMessage();
            msg.SetFrom(new EmailAddress(fromEmail, "Bixo Recommendations"));
            msg.SetReplyTo(new EmailAddress(_settings.SupportInboxEmail, "Bixo Support"));
            msg.AddTo(new EmailAddress(notification.Email));
            msg.SetSubject(subject);
            msg.AddContent(MimeType.Html, body);

            var response = await _client.SendEmailAsync(msg);

            if (response.StatusCode != System.Net.HttpStatusCode.OK &&
                response.StatusCode != System.Net.HttpStatusCode.Accepted)
            {
                _logger.LogWarning("Failed to send recommendation received email: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send recommendation received email to: {Email}", notification.Email);
        }
    }

    private static string BuildRecommendationReceivedEmailBody(RecommendationReceivedNotification notification)
    {
        var greeting = string.IsNullOrEmpty(notification.CandidateFirstName)
            ? "Hi"
            : $"Hi {notification.CandidateFirstName}";

        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">New recommendation received</h1>

                <p>{greeting},</p>

                <p>You've received a new recommendation from <strong>{notification.RecommenderName}</strong>.</p>

                <p>Review and approve it from your profile to make it visible to companies.</p>

                <p style=""margin-top: 32px; color: #6b7280;"">— Bixo</p>
            </body>
            </html>";
    }

    public async Task SendPublicWorkSummaryReadyEmailAsync(PublicWorkSummaryReadyNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping public work summary ready email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.PublicSummaryFromEmail)
                ? _settings.PublicSummaryFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = "Your public work summary is ready";
            var htmlContent = BuildPublicWorkSummaryReadyEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);
            msg.SetReplyTo(new EmailAddress(fromEmail, _settings.FromName));

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Public work summary ready email sent to: {Email}", notification.Email);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send public work summary ready email to: {Email}", notification.Email);
        }
    }

    private static string BuildPublicWorkSummaryReadyEmailBody(PublicWorkSummaryReadyNotification notification)
    {
        var greeting = !string.IsNullOrEmpty(notification.FirstName)
            ? $"Hi {notification.FirstName}"
            : "Hi";

        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <p>{greeting},</p>

                <p>Good news — your <strong>public work summary</strong> is ready for review.</p>

                <p>This summary is based on publicly available project documentation and is intended as optional supporting context for your profile.</p>

                <p style=""margin-top: 24px;"">What you can do now:</p>

                <ul style=""padding-left: 20px;"">
                    <li>Review and edit the summary if needed</li>
                    <li>Choose whether to include it in your profile</li>
                    <li>Enable it to make it visible to companies</li>
                </ul>

                <p style=""margin-top: 32px;"">
                    <a href=""{notification.ProfileUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">Review your summary</a>
                </p>

                <p style=""margin-top: 32px;"">If you have any questions, just reply to this email.</p>

                <p style=""margin-top: 32px;"">Best,<br />
                <strong>The Bixo Team</strong></p>
            </body>
            </html>";
    }

    // === Admin Scope Notification Emails ===

    public async Task SendAdminScopeApprovedNotificationAsync(AdminScopeApprovedNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey) || string.IsNullOrEmpty(_settings.AdminInboxEmail))
            {
                _logger.LogWarning("Email settings not configured, skipping admin scope approved notification");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.ShortlistFromEmail)
                ? _settings.ShortlistFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(_settings.AdminInboxEmail);
            var subject = $"Scope approved: {notification.RoleTitle} at {notification.CompanyName}";
            var htmlContent = BuildAdminScopeApprovedEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Admin scope approved notification sent for shortlist {ShortlistId}", notification.ShortlistId);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send admin scope approved notification for shortlist {ShortlistId}", notification.ShortlistId);
        }
    }

    private static string BuildAdminScopeApprovedEmailBody(AdminScopeApprovedNotification notification)
    {
        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #059669; margin-bottom: 24px;"">Scope approved</h1>

                <p><strong>{notification.CompanyName}</strong> has approved the scope and pricing for their <strong>{notification.RoleTitle}</strong> shortlist.</p>

                <div style=""background-color: #f0fdf4; border: 1px solid #bbf7d0; padding: 20px; border-radius: 8px; margin: 24px 0;"">
                    <p style=""margin: 0 0 8px 0;""><strong>Role:</strong> {notification.RoleTitle}</p>
                    <p style=""margin: 0 0 8px 0;""><strong>Approved price:</strong> ${notification.ApprovedPrice:F2}</p>
                    <p style=""margin: 0 0 8px 0;""><strong>Proposed candidates:</strong> {notification.ProposedCandidates}</p>
                    <p style=""margin: 0;""><strong>Approved at:</strong> {notification.ApprovedAt:MMMM dd, yyyy} at {notification.ApprovedAt:HH:mm} UTC</p>
                </div>

                <p style=""color: #6b7280;"">The shortlist is now ready for delivery when candidates are finalized.</p>

                <p style=""margin-top: 32px; color: #6b7280;"">— Bixo</p>
            </body>
            </html>";
    }

    public async Task SendAdminScopeDeclinedNotificationAsync(AdminScopeDeclinedNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey) || string.IsNullOrEmpty(_settings.AdminInboxEmail))
            {
                _logger.LogWarning("Email settings not configured, skipping admin scope declined notification");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.ShortlistFromEmail)
                ? _settings.ShortlistFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(_settings.AdminInboxEmail);
            var subject = $"Scope declined: {notification.RoleTitle} at {notification.CompanyName}";
            var htmlContent = BuildAdminScopeDeclinedEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Admin scope declined notification sent for shortlist {ShortlistId}", notification.ShortlistId);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send admin scope declined notification for shortlist {ShortlistId}", notification.ShortlistId);
        }
    }

    private static string BuildAdminScopeDeclinedEmailBody(AdminScopeDeclinedNotification notification)
    {
        var reasonSection = !string.IsNullOrEmpty(notification.DeclineReason)
            ? $@"<div style=""background-color: #fef2f2; border: 1px solid #fecaca; padding: 20px; border-radius: 8px; margin: 24px 0;"">
                    <p style=""margin: 0;""><strong>Reason:</strong> {notification.DeclineReason}</p>
                </div>"
            : "";

        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #dc2626; margin-bottom: 24px;"">Scope declined</h1>

                <p><strong>{notification.CompanyName}</strong> has declined the scope and pricing for their <strong>{notification.RoleTitle}</strong> shortlist.</p>

                {reasonSection}

                <div style=""background-color: #f9fafb; padding: 20px; border-radius: 8px; margin: 24px 0;"">
                    <p style=""margin: 0 0 8px 0;""><strong>Role:</strong> {notification.RoleTitle}</p>
                    <p style=""margin: 0;""><strong>Declined at:</strong> {notification.DeclinedAt:MMMM dd, yyyy} at {notification.DeclinedAt:HH:mm} UTC</p>
                </div>

                <p style=""color: #6b7280;"">The shortlist has been returned to Processing status. Please review and propose a new scope/price.</p>

                <p style=""margin-top: 32px; color: #6b7280;"">— Bixo</p>
            </body>
            </html>";
    }

    // === Password Reset Emails ===

    public async Task SendPasswordResetEmailAsync(PasswordResetNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping password reset email");
                return;
            }

            var fromEmail = !string.IsNullOrEmpty(_settings.RegisterFromEmail)
                ? _settings.RegisterFromEmail
                : _settings.FromEmail;
            var from = new EmailAddress(fromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = "Reset your Bixo password";
            var htmlContent = BuildPasswordResetEmailBody(notification);

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);
            msg.SetReplyTo(new EmailAddress(fromEmail, _settings.FromName));

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Password reset email sent to: {Email}", notification.Email);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to: {Email}", notification.Email);
        }
    }

    private static string BuildPasswordResetEmailBody(PasswordResetNotification notification)
    {
        var greeting = !string.IsNullOrEmpty(notification.FirstName)
            ? $"Hi {notification.FirstName}"
            : "Hi";

        return $@"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">Reset your password</h1>

                <p>{greeting},</p>

                <p>We received a request to reset your Bixo password. Click the button below to create a new password:</p>

                <p style=""margin: 32px 0;"">
                    <a href=""{notification.ResetUrl}"" style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;"">Reset password</a>
                </p>

                <p style=""color: #6b7280;"">This link will expire in <strong>1 hour</strong> for security reasons.</p>

                <p style=""margin-top: 24px;"">If you didn't request a password reset, you can safely ignore this email. Your password will remain unchanged.</p>

                <p style=""margin-top: 32px;"">— The Bixo Team</p>

                <hr style=""border: none; border-top: 1px solid #e5e7eb; margin: 32px 0;"" />

                <p style=""color: #9ca3af; font-size: 12px;"">If the button doesn't work, copy and paste this link into your browser:<br />
                <a href=""{notification.ResetUrl}"" style=""color: #6b7280; word-break: break-all;"">{notification.ResetUrl}</a></p>
            </body>
            </html>";
    }
}
