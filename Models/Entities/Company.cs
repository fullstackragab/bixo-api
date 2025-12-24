using bixo_api.Models.Enums;

namespace bixo_api.Models.Entities;

public class Company
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public string? CompanySize { get; set; }
    public string? Website { get; set; }
    public string? LogoFileKey { get; set; }
    public SubscriptionTier SubscriptionTier { get; set; } = SubscriptionTier.Free;
    public DateTime? SubscriptionExpiresAt { get; set; }
    public int MessagesRemaining { get; set; } = 5;
    public string? StripeCustomerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
    public ICollection<CompanyMember> Members { get; set; } = new List<CompanyMember>();
    public ICollection<SavedCandidate> SavedCandidates { get; set; } = new List<SavedCandidate>();
    public ICollection<ShortlistRequest> ShortlistRequests { get; set; } = new List<ShortlistRequest>();
    public ICollection<CandidateProfileView> ProfileViews { get; set; } = new List<CandidateProfileView>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public CompanyLocation? Location { get; set; }
}
