using bixo_api.Models.Enums;

namespace bixo_api.Models.Entities;

public class ShortlistRequest
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string RoleTitle { get; set; } = string.Empty;
    public string? TechStackRequired { get; set; }
    public SeniorityLevel? SeniorityRequired { get; set; }

    // Legacy location field (kept for backwards compatibility)
    public string? LocationPreference { get; set; }

    // New structured location fields for hiring location per request
    public string? LocationCountry { get; set; }
    public string? LocationCity { get; set; }
    public string? LocationTimezone { get; set; }

    // Renamed from RemoteAllowed for clarity
    public bool IsRemote { get; set; } = true;

    public string? AdditionalNotes { get; set; }
    public ShortlistStatus Status { get; set; } = ShortlistStatus.PendingScope;
    public decimal? PricePaid { get; set; }
    public string? PaymentIntentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // Versioning: Links to previous shortlist request for follow-up chains
    public Guid? PreviousRequestId { get; set; }

    // Pricing type: 'new', 'follow_up', 'free_regen'
    public string PricingType { get; set; } = "new";

    // Discount applied for follow-up shortlists
    public decimal FollowUpDiscount { get; set; } = 0;

    // Navigation
    public Company Company { get; set; } = null!;
    public ShortlistRequest? PreviousRequest { get; set; }
    public ICollection<ShortlistCandidate> Candidates { get; set; } = new List<ShortlistCandidate>();
}
