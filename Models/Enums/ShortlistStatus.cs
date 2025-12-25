namespace bixo_api.Models.Enums;

/// <summary>
/// Shortlist request status following the business flow:
/// PendingScope → ScopeProposed → ScopeApproved → Processing → Delivered
///
/// Payment authorization ONLY happens after ScopeApproved.
/// </summary>
public enum ShortlistStatus
{
    /// <summary>Request submitted, awaiting admin review of scope</summary>
    PendingScope = 0,

    /// <summary>Admin reviewed, scope and price proposed to company</summary>
    ScopeProposed = 1,

    /// <summary>Company approved scope and price, payment authorized</summary>
    ScopeApproved = 2,

    /// <summary>Shortlist being curated</summary>
    Processing = 3,

    /// <summary>Shortlist delivered, payment captured</summary>
    Delivered = 4,

    /// <summary>Canceled at any stage</summary>
    Canceled = 5,

    /// <summary>No suitable candidates found, authorization released</summary>
    NoCharge = 6
}
