namespace bixo_api.Models.Enums;

/// <summary>
/// Payment status following the business flow:
/// PendingApproval → Authorized → Captured/Partial/Released
///
/// Authorization ONLY happens after explicit company approval of scope and price.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Price proposed, awaiting company approval to authorize</summary>
    PendingApproval = 0,

    /// <summary>Company approved, funds authorized with provider (Stripe/PayPal/USDC)</summary>
    Authorized = 1,

    /// <summary>Full amount captured after delivery</summary>
    Captured = 2,

    /// <summary>Partial amount captured (discounted for partial delivery)</summary>
    Partial = 3,

    /// <summary>Authorization released without capture (no candidates / no charge)</summary>
    Released = 4,

    /// <summary>Payment canceled before authorization</summary>
    Canceled = 5,

    /// <summary>Payment failed at any stage</summary>
    Failed = 6
}
