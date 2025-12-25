namespace bixo_api.Services.Interfaces;

public interface IPaymentService
{
    // === Authorization Flow (New) ===

    /// <summary>
    /// Initiate payment authorization for a shortlist request.
    /// Does NOT capture funds - only authorizes/escrows.
    /// </summary>
    Task<PaymentInitiationResult> InitiatePaymentAsync(PaymentInitiationRequest request);

    /// <summary>
    /// Confirm payment authorization (after customer approves).
    /// For Stripe: Called after PaymentIntent confirmation.
    /// For PayPal: Called after customer returns from approval.
    /// For USDC: Called after on-chain transfer verification.
    /// </summary>
    Task<bool> ConfirmAuthorizationAsync(Guid paymentId, string? providerReference = null);

    /// <summary>
    /// Finalize payment based on shortlist outcome.
    /// Captures full/partial or releases authorization.
    /// </summary>
    Task<PaymentFinalizationResult> FinalizePaymentAsync(Guid shortlistRequestId, ShortlistOutcome outcome);

    /// <summary>
    /// Get payment status for a shortlist request.
    /// </summary>
    Task<PaymentStatusResponse?> GetPaymentStatusAsync(Guid shortlistRequestId);

    /// <summary>
    /// Check if authorization is still valid (not expired).
    /// Stripe authorizations expire after ~7 days.
    /// </summary>
    Task<bool> IsAuthorizationValidAsync(Guid paymentId);

    /// <summary>
    /// Handle expired authorization - requires new approval + authorization.
    /// </summary>
    Task HandleExpiredAuthorizationAsync(Guid paymentId);

    // === Legacy Methods (Subscriptions) ===

    Task<string> CreateSubscriptionSessionAsync(Guid companyId, Guid planId, bool yearly);
    Task HandleWebhookAsync(string payload, string signature);
    Task<SubscriptionStatusResponse> GetSubscriptionStatusAsync(Guid companyId);
    Task CancelSubscriptionAsync(Guid companyId);

    // Deprecated - use InitiatePaymentAsync instead
    [Obsolete("Use InitiatePaymentAsync for new shortlist payments")]
    Task<string> CreateShortlistPaymentSessionAsync(Guid companyId, Guid shortlistId, Guid pricingId);
}

public class PaymentInitiationRequest
{
    public Guid CompanyId { get; set; }
    public Guid ShortlistRequestId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Provider { get; set; } = "stripe"; // stripe | paypal | usdc
    public string? Description { get; set; }
}

public class PaymentInitiationResult
{
    public bool Success { get; set; }
    public Guid? PaymentId { get; set; }
    public string? ClientSecret { get; set; } // For Stripe frontend confirmation
    public string? ApprovalUrl { get; set; } // For PayPal redirect
    public string? EscrowAddress { get; set; } // For USDC transfer
    public string? ErrorMessage { get; set; }
}

public class ShortlistOutcome
{
    public string Status { get; set; } = "fulfilled"; // fulfilled | partial | no_match
    public int CandidatesDelivered { get; set; }
    public int CandidatesRequested { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? FinalAmount { get; set; }
}

public class PaymentFinalizationResult
{
    public bool Success { get; set; }
    public string Action { get; set; } = string.Empty; // captured | partial | released
    public decimal AmountCaptured { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PaymentStatusResponse
{
    public Guid PaymentId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal AmountAuthorized { get; set; }
    public decimal AmountCaptured { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SubscriptionStatusResponse
{
    public bool IsActive { get; set; }
    public string? PlanName { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int MessagesRemaining { get; set; }
}
