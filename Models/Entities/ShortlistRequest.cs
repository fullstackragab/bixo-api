using bixo_api.Models.Enums;

namespace bixo_api.Models.Entities;

public class ShortlistRequest
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string RoleTitle { get; set; } = string.Empty;
    public string? TechStackRequired { get; set; }
    public SeniorityLevel? SeniorityRequired { get; set; }
    public string? LocationPreference { get; set; }
    public bool RemoteAllowed { get; set; } = true;
    public string? AdditionalNotes { get; set; }
    public ShortlistStatus Status { get; set; } = ShortlistStatus.Pending;
    public decimal? PricePaid { get; set; }
    public string? PaymentIntentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // Navigation
    public Company Company { get; set; } = null!;
    public ICollection<ShortlistCandidate> Candidates { get; set; } = new List<ShortlistCandidate>();
}
