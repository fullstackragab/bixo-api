namespace bixo_api.Models.Enums;

/// <summary>
/// Shortlist lifecycle states following the trust-first, gated monetization flow:
/// Submitted → Processing → PricingPending → PricingApproved → Authorized → Delivered → Completed
///
/// Rules:
/// - Admin sets price, cannot trigger payment or delivery
/// - Company approves pricing before authorization
/// - Payment authorization before delivery
/// - Payment capture only after delivery
/// </summary>
public enum ShortlistStatus
{
    /// <summary>Company submitted shortlist request, no price yet</summary>
    Submitted = 0,

    /// <summary>Admin is processing (ranking/approving candidates)</summary>
    Processing = 1,

    /// <summary>Admin set price, awaiting company approval</summary>
    PricingPending = 2,

    /// <summary>Company approved pricing</summary>
    PricingApproved = 3,

    /// <summary>Payment authorized, ready for delivery</summary>
    Authorized = 4,

    /// <summary>Shortlist delivered to company, candidates exposed</summary>
    Delivered = 5,

    /// <summary>Payment captured, flow complete</summary>
    Completed = 6,

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
        [ShortlistStatus.Submitted] = new() { ShortlistStatus.Processing, ShortlistStatus.PricingPending, ShortlistStatus.Cancelled },
        [ShortlistStatus.Processing] = new() { ShortlistStatus.PricingPending, ShortlistStatus.Cancelled },
        [ShortlistStatus.PricingPending] = new() { ShortlistStatus.PricingApproved, ShortlistStatus.Processing, ShortlistStatus.Cancelled },
        [ShortlistStatus.PricingApproved] = new() { ShortlistStatus.Authorized, ShortlistStatus.Cancelled },
        [ShortlistStatus.Authorized] = new() { ShortlistStatus.Delivered, ShortlistStatus.Cancelled },
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
