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
