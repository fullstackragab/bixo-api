namespace bixo_api.Models.Enums;

/// <summary>
/// Payment status following the state machine:
/// Initiated → Authorized/Escrowed → Captured/Partial/Released → Failed
/// </summary>
public enum PaymentStatus
{
    /// <summary>Payment record created, awaiting provider action</summary>
    Initiated = 0,

    /// <summary>Funds authorized but not captured (Stripe/PayPal)</summary>
    Authorized = 1,

    /// <summary>Funds held in escrow (USDC)</summary>
    Escrowed = 2,

    /// <summary>Full amount captured</summary>
    Captured = 3,

    /// <summary>Partial amount captured (discounted shortlist)</summary>
    Partial = 4,

    /// <summary>Authorization released without capture (no candidates)</summary>
    Released = 5,

    /// <summary>Payment failed at any stage</summary>
    Failed = 6
}
