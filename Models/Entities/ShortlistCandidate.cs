namespace bixo_api.Models.Entities;

public class ShortlistCandidate
{
    public Guid Id { get; set; }
    public Guid ShortlistRequestId { get; set; }
    public Guid CandidateId { get; set; }
    public int MatchScore { get; set; }
    public string? MatchReason { get; set; }
    public int Rank { get; set; }
    public bool AdminApproved { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    // Versioning: TRUE if candidate is new in this shortlist, FALSE if previously recommended
    public bool IsNew { get; set; } = true;

    // References the shortlist where this candidate was previously recommended
    public Guid? PreviouslyRecommendedIn { get; set; }

    // Reason for re-including a previously recommended candidate
    public string? ReInclusionReason { get; set; }

    // Navigation
    public ShortlistRequest ShortlistRequest { get; set; } = null!;
    public Candidate Candidate { get; set; } = null!;
}
