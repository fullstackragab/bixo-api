namespace bixo_api.Models.Entities;

/// <summary>
/// Private recommendation requested by a candidate from their professional network.
/// Recommendations are only visible to companies after candidate approval.
/// </summary>
public class Recommendation
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }

    // Recommender info
    public string RecommenderName { get; set; } = string.Empty;
    public string RecommenderEmail { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty; // Manager, Tech Lead, Founder, Peer, Colleague
    public string? RecommenderRole { get; set; } // Professional role (e.g., Engineering Manager)
    public string? RecommenderCompany { get; set; } // Company where they worked with candidate

    // Recommendation content (written by recommender)
    public string? Content { get; set; }

    // Status tracking
    public bool IsSubmitted { get; set; }
    public bool IsApprovedByCandidate { get; set; }

    // Admin approval (required before visible to companies)
    public bool IsAdminApproved { get; set; }
    public DateTime? AdminApprovedAt { get; set; }
    public Guid? AdminApprovedBy { get; set; }

    // Admin rejection
    public bool IsRejected { get; set; }
    public string? RejectionReason { get; set; }

    // Secure token for recommender access (no login required)
    public string AccessToken { get; set; } = string.Empty;
    public DateTime TokenExpiresAt { get; set; }

    // Timestamps
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Valid relationship types for recommendations
/// </summary>
public static class RecommendationRelationship
{
    public const string Manager = "Manager";
    public const string TechLead = "Tech Lead";
    public const string Founder = "Founder";
    public const string Peer = "Peer";
    public const string Colleague = "Colleague";

    public static readonly string[] All = { Manager, TechLead, Founder, Peer, Colleague };

    public static bool IsValid(string relationship) =>
        All.Contains(relationship, StringComparer.OrdinalIgnoreCase);
}
