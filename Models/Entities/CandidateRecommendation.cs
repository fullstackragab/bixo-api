using pixo_api.Models.Enums;

namespace pixo_api.Models.Entities;

public class CandidateRecommendation
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public string RecommenderEmail { get; set; } = string.Empty;
    public string? RecommenderName { get; set; }
    public RecommendationType Type { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Candidate Candidate { get; set; } = null!;
}
