namespace bixo_api.Models.Enums;

/// <summary>
/// Payment status for shortlist payments.
/// Flow: None → Authorized → Captured
/// </summary>
public enum PaymentStatus
{
    /// <summary>No payment record or not yet authorized</summary>
    None = 0,

    /// <summary>Payment authorized (funds held, not captured)</summary>
    Authorized = 1,

    /// <summary>Payment captured after delivery</summary>
    Captured = 2,

    /// <summary>Payment failed</summary>
    Failed = 3,

    /// <summary>Authorization expired before capture</summary>
    Expired = 4,

    /// <summary>Authorization released without capture</summary>
    Released = 5
}
