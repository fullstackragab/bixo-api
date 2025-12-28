namespace bixo_api.Services.Interfaces;

public interface IEmailService
{
    Task SendSupportNotificationAsync(SupportNotification notification);
    Task SendCompanyWelcomeEmailAsync(CompanyWelcomeNotification notification);
    Task SendCandidateWelcomeEmailAsync(CandidateWelcomeNotification notification);
    Task SendCandidateProfileActiveEmailAsync(CandidateProfileActiveNotification notification);
    Task SendCandidateProfileRejectedEmailAsync(CandidateProfileRejectedNotification notification);
    Task SendAdminNewCandidateNotificationAsync(AdminNewCandidateNotification notification);
    Task SendAdminNewCompanyNotificationAsync(AdminNewCompanyNotification notification);
    Task SendAdminNewShortlistNotificationAsync(AdminNewShortlistNotification notification);

    /// <summary>Generic admin notification for issues that need attention</summary>
    Task SendAdminNotificationAsync(string subject, string message);

    // === Shortlist Status Email Events (idempotent, sent once per event type) ===

    /// <summary>Sent when pricing is ready for company review</summary>
    Task SendShortlistPricingReadyEmailAsync(ShortlistPricingReadyNotification notification);

    /// <summary>Sent when payment authorization is required</summary>
    Task SendShortlistAuthorizationRequiredEmailAsync(ShortlistAuthorizationRequiredNotification notification);

    /// <summary>Sent when shortlist has been delivered</summary>
    Task SendShortlistDeliveredEmailAsync(ShortlistDeliveredNotification notification);

    /// <summary>Sent when no suitable candidates found</summary>
    Task SendShortlistNoMatchEmailAsync(ShortlistNoMatchNotification notification);

    /// <summary>Sent when admin suggests adjusting the brief</summary>
    Task SendShortlistAdjustmentSuggestedEmailAsync(ShortlistAdjustmentSuggestedNotification notification);

    /// <summary>Sent when admin extends the search window</summary>
    Task SendShortlistSearchExtendedEmailAsync(ShortlistSearchExtendedNotification notification);

    /// <summary>Sent when admin starts processing a shortlist</summary>
    Task SendShortlistProcessingStartedEmailAsync(ShortlistProcessingStartedNotification notification);

    /// <summary>Sent when company approves pricing</summary>
    Task SendShortlistPricingApprovedEmailAsync(ShortlistPricingApprovedNotification notification);

    /// <summary>Sent when shortlist is completed (delivered + paid)</summary>
    Task SendShortlistCompletedEmailAsync(ShortlistCompletedNotification notification);

    /// <summary>Sent when company declines pricing</summary>
    Task SendShortlistPricingDeclinedEmailAsync(ShortlistPricingDeclinedNotification notification);

    // === Recommendation Emails ===

    /// <summary>Sent to recommender when candidate requests a recommendation</summary>
    Task SendRecommendationRequestEmailAsync(RecommendationRequestNotification notification);

    /// <summary>Sent to candidate when a recommendation is submitted</summary>
    Task SendRecommendationReceivedEmailAsync(RecommendationReceivedNotification notification);

    // === Public Work Summary Emails ===

    /// <summary>Sent to candidate when their public work summary request is fulfilled</summary>
    Task SendPublicWorkSummaryReadyEmailAsync(PublicWorkSummaryReadyNotification notification);
}

public class SupportNotification
{
    public string UserType { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ReplyToEmail { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CompanyWelcomeNotification
{
    public string Email { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string DashboardUrl { get; set; } = string.Empty;
}

/// <summary>Sent when pricing is ready for company review</summary>
public class ShortlistPricingReadyNotification
{
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public Guid ShortlistId { get; set; }
    public string ShortlistUrl { get; set; } = string.Empty;
}

/// <summary>Sent when payment authorization is required</summary>
public class ShortlistAuthorizationRequiredNotification
{
    public string Email { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public Guid ShortlistId { get; set; }
    public string ShortlistUrl { get; set; } = string.Empty;
}

/// <summary>Sent when shortlist has been delivered</summary>
public class ShortlistDeliveredNotification
{
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public Guid ShortlistId { get; set; }
    public string ShortlistUrl { get; set; } = string.Empty;
}

/// <summary>Sent when no suitable candidates found</summary>
public class ShortlistNoMatchNotification
{
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Guid ShortlistId { get; set; }
    public string ShortlistUrl { get; set; } = string.Empty;
}

public class CandidateWelcomeNotification
{
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string ProfileUrl { get; set; } = string.Empty;
}

public class CandidateProfileActiveNotification
{
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string ProfileUrl { get; set; } = string.Empty;
}

public class CandidateProfileRejectedNotification
{
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string ProfileUrl { get; set; } = string.Empty;
}

public class AdminNewCandidateNotification
{
    public Guid CandidateId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminNewCompanyNotification
{
    public Guid CompanyId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminNewShortlistNotification
{
    public Guid ShortlistId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public List<string> TechStack { get; set; } = new();
    public string? Seniority { get; set; }
    public string? Location { get; set; }
    public bool IsRemote { get; set; }
    public string? AdditionalNotes { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Sent to recommender when candidate requests a recommendation</summary>
public class RecommendationRequestNotification
{
    public string Email { get; set; } = string.Empty;
    public string RecommenderName { get; set; } = string.Empty;
    public string CandidateName { get; set; } = string.Empty;
    public string RecommendationUrl { get; set; } = string.Empty;
}

/// <summary>Sent to candidate when a recommendation is submitted</summary>
public class RecommendationReceivedNotification
{
    public string Email { get; set; } = string.Empty;
    public string CandidateFirstName { get; set; } = string.Empty;
    public string RecommenderName { get; set; } = string.Empty;
}

/// <summary>Sent when admin suggests adjusting the brief</summary>
public class ShortlistAdjustmentSuggestedNotification
{
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid ShortlistId { get; set; }
    public string EditUrl { get; set; } = string.Empty;
    public string CloseUrl { get; set; } = string.Empty;
}

/// <summary>Sent when admin extends the search window</summary>
public class ShortlistSearchExtendedNotification
{
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid ShortlistId { get; set; }
}

/// <summary>Sent when admin starts processing a shortlist request</summary>
public class ShortlistProcessingStartedNotification
{
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public Guid ShortlistId { get; set; }
    public string ShortlistUrl { get; set; } = string.Empty;
}

/// <summary>Sent when company approves pricing</summary>
public class ShortlistPricingApprovedNotification
{
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public Guid ShortlistId { get; set; }
    public string ShortlistUrl { get; set; } = string.Empty;
}

/// <summary>Sent when shortlist is completed (delivered + paid)</summary>
public class ShortlistCompletedNotification
{
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public Guid ShortlistId { get; set; }
    public string ShortlistUrl { get; set; } = string.Empty;
}

/// <summary>Sent when company declines pricing</summary>
public class ShortlistPricingDeclinedNotification
{
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Guid ShortlistId { get; set; }
    public string ShortlistUrl { get; set; } = string.Empty;
}

/// <summary>Sent to candidate when their public work summary request is fulfilled</summary>
public class PublicWorkSummaryReadyNotification
{
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string ProfileUrl { get; set; } = string.Empty;
}
