using bixo_api.Models.Enums;

namespace bixo_api.Models.Entities;

public class Payment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? ShortlistId { get; set; }
    public string Provider { get; set; } = "stripe"; // stripe | paypal | crypto
    public string? AuthorizationId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public PaymentStatus Status { get; set; } = PaymentStatus.None;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AuthorizedAt { get; set; }
    public DateTime? CapturedAt { get; set; }

    // Navigation
    public Company Company { get; set; } = null!;
    public ShortlistRequest? Shortlist { get; set; }
}
