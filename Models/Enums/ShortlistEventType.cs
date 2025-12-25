namespace bixo_api.Models.Enums;

/// <summary>
/// Auditable shortlist events for billing and compliance
/// </summary>
public enum ShortlistEventType
{
    /// <summary>Shortlist request created</summary>
    Created = 0,

    /// <summary>Matching started</summary>
    MatchingStarted = 1,

    /// <summary>Matching completed, candidates found</summary>
    MatchingCompleted = 2,

    /// <summary>Pricing set for the shortlist</summary>
    PricingSet = 3,

    /// <summary>Pricing approved by company</summary>
    PricingApproved = 4,

    /// <summary>Payment authorized</summary>
    PaymentAuthorized = 5,

    /// <summary>Shortlist delivered to company</summary>
    Delivered = 6,

    /// <summary>Payment captured after delivery</summary>
    PaymentCaptured = 7,

    /// <summary>Payment released (no charge)</summary>
    PaymentReleased = 8,

    /// <summary>Shortlist cancelled</summary>
    Cancelled = 9,

    /// <summary>State transition occurred</summary>
    StateChanged = 10
}
