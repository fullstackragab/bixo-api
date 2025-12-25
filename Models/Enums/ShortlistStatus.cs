namespace bixo_api.Models.Enums;

/// <summary>
/// Shortlist business lifecycle states:
/// Submitted → Processing → PricingPending → Approved → Delivered → Completed
///
/// Rules:
/// - Admin sets price, company approves
/// - Delivery allowed after approval (payment is out-of-band)
/// - Completion requires: status=Delivered AND paymentStatus=Paid
/// </summary>
public enum ShortlistStatus
{
    /// <summary>Company submitted shortlist request, no price yet</summary>
    Submitted = 0,

    /// <summary>Admin is processing (ranking/approving candidates)</summary>
    Processing = 1,

    /// <summary>Admin set price, awaiting company approval</summary>
    PricingPending = 2,

    /// <summary>Company approved pricing, ready for delivery</summary>
    Approved = 3,

    /// <summary>Shortlist delivered to company, candidates exposed</summary>
    Delivered = 5,

    /// <summary>Delivery complete and payment confirmed</summary>
    Completed = 6,

    /// <summary>Cancelled at any stage</summary>
    Cancelled = 7
}

/// <summary>
/// Payment settlement status (separate from business lifecycle).
/// Designed for manual out-of-band payment (PayPal, crypto, bank).
/// Compatible with future Stripe automation.
/// </summary>
public enum PaymentSettlementStatus
{
    /// <summary>No payment required for this shortlist</summary>
    NotRequired = 0,

    /// <summary>Payment pending (default for new shortlists)</summary>
    Pending = 1,

    /// <summary>Payment confirmed by admin</summary>
    Paid = 2
}

/// <summary>
/// Helper for validating shortlist state transitions
/// </summary>
public static class ShortlistStatusTransitions
{
    private static readonly Dictionary<ShortlistStatus, HashSet<ShortlistStatus>> ValidTransitions = new()
    {
        [ShortlistStatus.Submitted] = new() { ShortlistStatus.Processing, ShortlistStatus.PricingPending, ShortlistStatus.Cancelled },
        [ShortlistStatus.Processing] = new() { ShortlistStatus.PricingPending, ShortlistStatus.Cancelled },
        [ShortlistStatus.PricingPending] = new() { ShortlistStatus.Approved, ShortlistStatus.Processing, ShortlistStatus.Cancelled },
        [ShortlistStatus.Approved] = new() { ShortlistStatus.Delivered, ShortlistStatus.Cancelled },
        [ShortlistStatus.Delivered] = new() { ShortlistStatus.Completed },
        [ShortlistStatus.Completed] = new(),
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
