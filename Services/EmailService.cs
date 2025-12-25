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
            var shortlistFrom = !string.IsNullOrEmpty(_settings.ShortlistFromEmail)
                ? _settings.ShortlistFromEmail
                : _settings.FromEmail;

            if (string.IsNullOrEmpty(_settings.ApiKey) || string.IsNullOrEmpty(_settings.SupportInboxEmail))
            {
                _logger.LogWarning("Email settings not configured, skipping shortlist notification email");
                return;
            }

            var from = new EmailAddress(shortlistFrom, _settings.FromName);
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

    public async Task SendCompanyWelcomeEmailAsync(CompanyWelcomeNotification notification)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                _logger.LogWarning("Email settings not configured, skipping company welcome email");
                return;
            }

            var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = "Welcome to Bixo";
            var htmlContent = BuildCompanyWelcomeEmailBody();

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

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

    private static string BuildCompanyWelcomeEmailBody()
    {
        return @"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">Welcome to Bixo</h1>

                <p>We don't do job posts.</p>

                <p>Instead, we deliver <strong>paid, curated shortlists</strong> — so you can talk to relevant candidates without CV overload.</p>

                <h2 style=""color: #1f2937; margin-top: 32px; font-size: 18px;"">How it works</h2>
                <ol style=""padding-left: 20px;"">
                    <li>You request a shortlist for a role</li>
                    <li>We curate and rank 5–15 relevant candidates</li>
                    <li>You unlock full profiles only for that shortlist</li>
                    <li>Messaging is limited to shortlisted candidates only</li>
                </ol>

                <h2 style=""color: #1f2937; margin-top: 32px; font-size: 18px;"">Pricing &amp; payment</h2>
                <ul style=""padding-left: 20px;"">
                    <li>You authorize payment upfront</li>
                    <li>We only capture <strong>after</strong> the shortlist is delivered</li>
                    <li>No shortlist = no charge</li>
                    <li>Partial matches = discounted automatically</li>
                </ul>

                <h2 style=""color: #1f2937; margin-top: 32px; font-size: 18px;"">What to expect</h2>
                <ul style=""padding-left: 20px;"">
                    <li>Candidates on Bixo are passive — most are currently employed</li>
                    <li>Some may decline to engage, and that's normal</li>
                    <li>The value is relevance and access, not guaranteed acceptance</li>
                    <li>Shortlists are snapshots in time, not ongoing pipelines</li>
                </ul>

                <p style=""margin-top: 32px;"">When you're ready, request your first shortlist — we'll handle the rest.</p>

                <p style=""margin-top: 32px; color: #6b7280;"">— The Bixo Team</p>
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

            var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = $"Your shortlist is ready: {notification.RoleTitle}";
            var htmlContent = BuildShortlistDeliveredEmailBody();

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Shortlist delivered email sent to: {Email}", notification.Email);
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

    private static string BuildShortlistDeliveredEmailBody()
    {
        return @"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">Your shortlist is ready</h1>

                <p>Inside, you'll find:</p>

                <ul style=""padding-left: 20px;"">
                    <li>A ranked list of curated candidates</li>
                    <li>Full profiles and CVs</li>
                    <li>Messaging enabled for this shortlist only</li>
                </ul>

                <p style=""margin-top: 24px;"">This shortlist is a snapshot in time.<br />
                If you request a follow-up later, previously delivered candidates will be excluded by default.</p>

                <p style=""margin-top: 24px;""><strong>A note on responses:</strong><br />
                These candidates are passive. Some may decline or not respond — that's expected.<br />
                The value is access to relevant people, not guaranteed engagement.</p>

                <p style=""margin-top: 24px;"">If you need adjustments or a follow-up request, just let us know.</p>

                <p style=""margin-top: 32px; color: #6b7280;"">— Bixo</p>
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

            var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = "Welcome to Bixo";
            var htmlContent = BuildCandidateWelcomeEmailBody();

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

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

    private static string BuildCandidateWelcomeEmailBody()
    {
        return @"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">Thanks for joining Bixo</h1>

                <p>Bixo works differently from traditional job platforms.</p>

                <p>You won't apply for jobs here.<br />
                You won't write cover letters.<br />
                You won't get spammed.</p>

                <p style=""margin-top: 24px;"">Instead:</p>

                <ul style=""padding-left: 20px;"">
                    <li>Companies request <strong>paid, curated shortlists</strong></li>
                    <li>Only shortlisted candidates can be contacted</li>
                    <li>Messaging is limited, intentional, and role-specific</li>
                </ul>

                <h2 style=""color: #1f2937; margin-top: 32px; font-size: 18px;"">What to do next</h2>
                <ol style=""padding-left: 20px;"">
                    <li>Upload your CV</li>
                    <li>Add a short role preference (one sentence is enough)</li>
                    <li>Set your visibility status</li>
                </ol>

                <p style=""margin-top: 24px;"">That's it.</p>

                <p>You'll stay passive unless a company explicitly includes you in a shortlist.</p>

                <p>If you're shortlisted, you'll be notified before any message is sent.<br />
                You can decline at any time — no explanation required, no impact on future visibility.</p>

                <p style=""margin-top: 32px;"">Welcome — and thanks for trusting us with your profile.</p>

                <p style=""margin-top: 32px; color: #6b7280;"">— The Bixo Team</p>
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

            var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
            var to = new EmailAddress(notification.Email);
            var subject = "Your Bixo profile is now active";
            var htmlContent = BuildCandidateProfileActiveEmailBody();

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

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

    private static string BuildCandidateProfileActiveEmailBody()
    {
        return @"
            <html>
            <body style=""font-family: Arial, sans-serif; line-height: 1.8; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
                <h1 style=""color: #2563eb; margin-bottom: 24px;"">Your Bixo profile is now active</h1>

                <p>You don't need to do anything else.</p>

                <p style=""margin-top: 24px;"">If a company requests a shortlist that matches your profile:</p>

                <ul style=""padding-left: 20px;"">
                    <li>Your profile may be reviewed</li>
                    <li>You'll be notified if you're shortlisted</li>
                    <li>Companies can only message you <em>after</em> that point</li>
                </ul>

                <p style=""margin-top: 24px;"">Being shortlisted creates no obligation.<br />
                If a role isn't relevant or the timing isn't right, you can decline — no explanation needed.</p>

                <p style=""margin-top: 24px;"">You remain in control of your visibility at all times.</p>

                <p style=""margin-top: 24px;"">We'll keep this quiet, relevant, and respectful.</p>

                <p style=""margin-top: 32px; color: #6b7280;"">— Bixo</p>
            </body>
            </html>";
    }
}
