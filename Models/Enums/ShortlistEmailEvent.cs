namespace bixo_api.Models.Enums;

/// <summary>
/// Email events that can be sent for a shortlist request.
/// Each event can be sent only once per shortlist (idempotent).
/// </summary>
public enum ShortlistEmailEvent
{
    /// <summary>Pricing is ready for company review</summary>
    PricingReady = 1,

    /// <summary>Payment authorization is required to proceed</summary>
    AuthorizationRequired = 2,

    /// <summary>Shortlist has been delivered</summary>
    Delivered = 3,

    /// <summary>No suitable candidates found</summary>
    NoMatch = 4
}
