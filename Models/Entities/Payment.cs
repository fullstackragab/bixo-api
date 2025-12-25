using bixo_api.Models.Enums;

namespace bixo_api.Models.Entities;

public class Payment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public PaymentType Type { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? StripePaymentIntentId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Initiated;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Company Company { get; set; } = null!;
}
