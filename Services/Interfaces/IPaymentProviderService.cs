namespace bixo_api.Services.Interfaces;

/// <summary>
/// Provider-agnostic payment operations interface.
/// Each provider (Stripe, PayPal, USDC) implements this interface.
/// </summary>
public interface IPaymentProviderService
{
    string ProviderName { get; }

    /// <summary>
    /// Authorize/escrow funds without capturing.
    /// For Stripe: Creates PaymentIntent with manual capture.
    /// For PayPal: Creates Order with AUTHORIZE intent.
    /// For USDC: Verifies escrow transfer.
    /// </summary>
    Task<PaymentAuthorizationResult> AuthorizeAsync(PaymentAuthorizationRequest request);

    /// <summary>
    /// Capture the full authorized amount.
    /// </summary>
    Task<PaymentCaptureResult> CaptureFullAsync(string providerReference, decimal amount);

    /// <summary>
    /// Capture a partial amount (for discounted shortlists).
    /// </summary>
    Task<PaymentCaptureResult> CapturePartialAsync(string providerReference, decimal originalAmount, decimal captureAmount);

    /// <summary>
    /// Release/cancel authorization without capturing (for no-match shortlists).
    /// </summary>
    Task<PaymentReleaseResult> ReleaseAsync(string providerReference);

    /// <summary>
    /// Check if an authorization is still valid (not expired).
    /// </summary>
    Task<bool> IsAuthorizationValidAsync(string providerReference);
}

public class PaymentAuthorizationRequest
{
    public Guid CompanyId { get; set; }
    public Guid ShortlistRequestId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? CustomerEmail { get; set; }
    public string? CustomerId { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public class PaymentAuthorizationResult
{
    public bool Success { get; set; }
    public string? ProviderReference { get; set; }
    public string? ClientSecret { get; set; } // For frontend confirmation (Stripe)
    public string? ApprovalUrl { get; set; } // For redirect flows (PayPal)
    public string? EscrowAddress { get; set; } // For crypto transfers (USDC)
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
}

public class PaymentCaptureResult
{
    public bool Success { get; set; }
    public decimal AmountCaptured { get; set; }
    public string? ProviderReference { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PaymentReleaseResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
