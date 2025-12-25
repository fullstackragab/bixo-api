namespace bixo_api.Models.Enums;

/// <summary>
/// Shortlist lifecycle states following the business flow:
/// Draft → Matching → ReadyForPricing → PricingRequested → PricingApproved → Delivered → PaymentCaptured
///
/// Rules:
/// - Payment authorization happens at PricingApproved
/// - Payment capture ONLY happens after Delivered
/// - Admins cannot set prices or capture payments directly
/// </summary>
public enum ShortlistStatus
{
    /// <summary>Request created, not yet being processed</summary>
    Draft = 0,

    /// <summary>System is matching candidates to the request</summary>
    Matching = 1,

    /// <summary>Candidates matched, ready for admin to set pricing</summary>
    ReadyForPricing = 2,

    /// <summary>Pricing set by system, awaiting company approval</summary>
    PricingRequested = 3,

    /// <summary>Company approved pricing, payment authorized</summary>
    PricingApproved = 4,

    /// <summary>Shortlist delivered to company</summary>
    Delivered = 5,

    /// <summary>Payment captured after delivery</summary>
    PaymentCaptured = 6,

    /// <summary>Cancelled at any stage</summary>
    Cancelled = 7
}

/// <summary>
/// Helper for validating shortlist state transitions
/// </summary>
public static class ShortlistStatusTransitions
{
    private static readonly Dictionary<ShortlistStatus, HashSet<ShortlistStatus>> ValidTransitions = new()
    {
        [ShortlistStatus.Draft] = new() { ShortlistStatus.Matching, ShortlistStatus.Cancelled },
        [ShortlistStatus.Matching] = new() { ShortlistStatus.ReadyForPricing, ShortlistStatus.Cancelled },
        [ShortlistStatus.ReadyForPricing] = new() { ShortlistStatus.PricingRequested, ShortlistStatus.Cancelled },
        [ShortlistStatus.PricingRequested] = new() { ShortlistStatus.PricingApproved, ShortlistStatus.Cancelled },
        [ShortlistStatus.PricingApproved] = new() { ShortlistStatus.Delivered, ShortlistStatus.Cancelled },
        [ShortlistStatus.Delivered] = new() { ShortlistStatus.PaymentCaptured },
        [ShortlistStatus.PaymentCaptured] = new(),
        [ShortlistStatus.Cancelled] = new()
    };

    /// <summary>
    /// Check if a state transition is allowed
    /// </summary>
    public static bool IsValidTransition(ShortlistStatus from, ShortlistStatus to)
    {
        return ValidTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    /// <summary>
    /// Get allowed next states from current state
    /// </summary>
    public static IReadOnlySet<ShortlistStatus> GetAllowedTransitions(ShortlistStatus from)
    {
        return ValidTransitions.TryGetValue(from, out var allowed) ? allowed : new HashSet<ShortlistStatus>();
    }

    /// <summary>
    /// Validate and throw if transition is not allowed
    /// </summary>
    public static void ValidateTransition(ShortlistStatus from, ShortlistStatus to)
    {
        if (!IsValidTransition(from, to))
        {
            throw new InvalidOperationException(
                $"Invalid state transition: {from} → {to}. Allowed transitions from {from}: {string.Join(", ", GetAllowedTransitions(from))}");
        }
    }
}
