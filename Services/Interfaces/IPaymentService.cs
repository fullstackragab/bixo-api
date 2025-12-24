namespace bixo_api.Services.Interfaces;

public interface IPaymentService
{
    Task<string> CreateShortlistPaymentSessionAsync(Guid companyId, Guid shortlistId, Guid pricingId);
    Task<string> CreateSubscriptionSessionAsync(Guid companyId, Guid planId, bool yearly);
    Task HandleWebhookAsync(string payload, string signature);
    Task<SubscriptionStatusResponse> GetSubscriptionStatusAsync(Guid companyId);
    Task CancelSubscriptionAsync(Guid companyId);
}

public class SubscriptionStatusResponse
{
    public bool IsActive { get; set; }
    public string? PlanName { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int MessagesRemaining { get; set; }
}
