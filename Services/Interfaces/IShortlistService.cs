using bixo_api.Models.DTOs.Shortlist;

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

    // === Scope Confirmation Flow ===

    /// <summary>
    /// Admin proposes scope and price for a shortlist request.
    /// Moves status from PendingScope to ScopeProposed.
    /// </summary>
    Task<ScopeProposalResult> ProposeScopeAsync(Guid shortlistRequestId, ScopeProposalRequest request);

    /// <summary>
    /// Company approves the proposed scope and price.
    /// Moves status from ScopeProposed to ScopeApproved.
    /// This is the ONLY point where payment authorization may occur.
    /// </summary>
    Task<ScopeApprovalResult> ApproveScopeAsync(Guid companyId, Guid shortlistRequestId, ScopeApprovalRequest request);

    /// <summary>
    /// Get pending scope proposals for a company.
    /// </summary>
    Task<List<ScopeProposalResponse>> GetPendingScopeProposalsAsync(Guid companyId);
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
