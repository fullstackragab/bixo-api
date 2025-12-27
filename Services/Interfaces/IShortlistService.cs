using bixo_api.Models.DTOs.Shortlist;
using bixo_api.Models.Enums;

namespace bixo_api.Services.Interfaces;

public interface IShortlistService
{
    Task<List<ShortlistPricingResponse>> GetPricingAsync();
    Task<ShortlistResponse> CreateRequestAsync(Guid companyId, CreateShortlistRequest request);
    Task<ShortlistDetailResponse?> GetShortlistAsync(Guid companyId, Guid shortlistId);
    Task<List<ShortlistResponse>> GetCompanyShortlistsAsync(Guid companyId);
    Task ProcessShortlistAsync(Guid shortlistId);

    /// <summary>
    /// Calculate the estimated price for a shortlist before payment authorization.
    /// </summary>
    Task<ShortlistPriceEstimate> GetPriceEstimateAsync(Guid shortlistRequestId);

    /// <summary>
    /// Complete/deliver a shortlist and finalize payment based on outcome.
    /// </summary>
    Task<ShortlistDeliveryResult> DeliverShortlistAsync(Guid shortlistRequestId, ShortlistDeliveryRequest request);

    // === Pricing & Payment Flow ===

    /// <summary>
    /// Admin sets price for a shortlist (status: Processing → PricingPending).
    /// </summary>
    Task<ScopeProposalResult> ProposeScopeAsync(Guid shortlistRequestId, ScopeProposalRequest request);

    /// <summary>
    /// Company approves pricing (status: PricingPending → Approved).
    /// Ready for delivery after approval.
    /// </summary>
    Task ApprovePricingAsync(Guid companyId, Guid shortlistRequestId);

    /// <summary>
    /// Company declines pricing (status: PricingPending → Processing).
    /// Admin can propose a new price.
    /// </summary>
    Task DeclinePricingAsync(Guid companyId, Guid shortlistRequestId, string? reason);

    /// <summary>
    /// Get shortlists with pending pricing for a company.
    /// </summary>
    Task<List<ScopeProposalResponse>> GetPendingScopeProposalsAsync(Guid companyId);

    // === Manual Payment Settlement ===

    /// <summary>
    /// Admin marks shortlist as having no suitable candidates.
    /// Sets outcome to NoMatch, payment_status to NotRequired, and closes the shortlist.
    /// This is irreversible - company will not be charged.
    /// </summary>
    Task<NoMatchResult> MarkAsNoMatchAsync(Guid shortlistRequestId, Guid adminUserId, string reason);

    /// <summary>
    /// Admin suggests adjustments to the brief when no suitable candidates found.
    /// Sets status to AwaitingAdjustment and sends email to company.
    /// </summary>
    Task<SuggestAdjustmentResult> SuggestAdjustmentAsync(Guid shortlistRequestId, Guid adminUserId, string message);

    /// <summary>
    /// Admin extends the search window when more time is needed.
    /// Keeps status as Processing and sends email to company.
    /// </summary>
    Task<ExtendSearchResult> ExtendSearchAsync(Guid shortlistRequestId, Guid adminUserId, string message, int extendDays);

    /// <summary>
    /// Admin marks shortlist as paid (sets paymentStatus = paid).
    /// Allowed only when status = Delivered.
    /// </summary>
    Task MarkAsPaidAsync(Guid shortlistRequestId, Guid adminUserId, string? paymentNote);

    /// <summary>
    /// Complete shortlist after delivery and payment confirmed.
    /// Called automatically or manually when status=Delivered AND paymentStatus=Paid.
    /// </summary>
    Task CompleteShortlistAsync(Guid shortlistRequestId);

    // === Legacy/Future Stripe Support ===

    /// <summary>
    /// [DEPRECATED - for future Stripe automation]
    /// Authorize payment after pricing approval.
    /// </summary>
    [Obsolete("Use manual payment flow for now. Stripe automation coming later.")]
    Task<PaymentAuthorizationResult> AuthorizePaymentAsync(Guid companyId, Guid shortlistRequestId, string provider);

    // Legacy method - kept for compatibility during migration
    Task<ScopeApprovalResult> ApproveScopeAsync(Guid companyId, Guid shortlistRequestId, ScopeApprovalRequest request);

    // === Email Event Tracking (idempotent) ===

    /// <summary>
    /// Send email event for shortlist if not already sent.
    /// Returns true if email was sent, false if already sent.
    /// </summary>
    Task<bool> TrySendEmailEventAsync(Guid shortlistRequestId, ShortlistEmailEvent emailEvent);

    /// <summary>
    /// Admin manually resends the last email for a shortlist.
    /// Creates a new record with is_resend = true.
    /// </summary>
    Task ResendLastEmailAsync(Guid shortlistRequestId, Guid adminUserId);

    /// <summary>
    /// Get email history for a shortlist (for admin UI).
    /// </summary>
    Task<List<ShortlistEmailRecord>> GetEmailHistoryAsync(Guid shortlistRequestId);
}

public class ShortlistPriceEstimate
{
    public Guid ShortlistRequestId { get; set; }
    public decimal BasePrice { get; set; }
    public decimal FollowUpDiscount { get; set; }
    public decimal FinalPrice { get; set; }
    public string PricingType { get; set; } = "new";
    public int CandidatesRequested { get; set; }
}

public class ShortlistDeliveryRequest
{
    /// <summary>Admin user who is delivering the shortlist</summary>
    public Guid AdminUserId { get; set; }

    public int CandidatesRequested { get; set; }
    public int CandidatesDelivered { get; set; }
    public decimal? OverridePrice { get; set; }
    public string? Notes { get; set; }
}

public class ShortlistDeliveryResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string PaymentAction { get; set; } = string.Empty; // captured | partial | released | no_payment
    public decimal AmountCaptured { get; set; }
}

// === Scope Confirmation DTOs ===

public class ScopeProposalRequest
{
    /// <summary>Expected number of candidates (e.g. 5-10)</summary>
    public int ProposedCandidates { get; set; }

    /// <summary>Exact price for this shortlist</summary>
    public decimal ProposedPrice { get; set; }

    /// <summary>Optional notes about the scope</summary>
    public string? Notes { get; set; }
}

public class ScopeProposalResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ScopeApprovalRequest
{
    /// <summary>Payment provider to use: stripe | paypal | usdc</summary>
    public string Provider { get; set; } = "stripe";

    /// <summary>Explicit confirmation text (must match expected format)</summary>
    public bool ConfirmApproval { get; set; }
}

public class ScopeApprovalResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? PaymentId { get; set; }
    public string? ClientSecret { get; set; } // For Stripe
    public string? ApprovalUrl { get; set; } // For PayPal
    public string? EscrowAddress { get; set; } // For USDC
}

public class ScopeProposalResponse
{
    public Guid ShortlistId { get; set; }
    public string RoleTitle { get; set; } = string.Empty;
    public int ProposedCandidates { get; set; }
    public decimal ProposedPrice { get; set; }
    public DateTime ProposedAt { get; set; }
    public string? Notes { get; set; }
}

public class NoMatchResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SuggestAdjustmentResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ExtendSearchResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? NewDeadline { get; set; }
}

public class ShortlistEmailRecord
{
    public Guid Id { get; set; }
    public ShortlistEmailEvent EmailEvent { get; set; }
    public DateTime SentAt { get; set; }
    public string SentTo { get; set; } = string.Empty;
    public Guid? SentBy { get; set; }
    public bool IsResend { get; set; }
}
