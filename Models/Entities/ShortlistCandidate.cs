namespace pixo_api.Models.Entities;

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

    // Navigation
    public ShortlistRequest ShortlistRequest { get; set; } = null!;
    public Candidate Candidate { get; set; } = null!;
}
