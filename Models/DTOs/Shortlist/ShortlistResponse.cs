using bixo_api.Models.Enums;

namespace bixo_api.Models.DTOs.Shortlist;

public class ShortlistResponse
{
    public Guid Id { get; set; }
    public string RoleTitle { get; set; } = string.Empty;
    public List<string> TechStackRequired { get; set; } = new();
    public SeniorityLevel? SeniorityRequired { get; set; }
    public string? LocationPreference { get; set; }
    public bool RemoteAllowed { get; set; }
    public string? AdditionalNotes { get; set; }
    public ShortlistStatus Status { get; set; }
    public decimal? PricePaid { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int CandidatesCount { get; set; }
}

public class ShortlistCandidateResponse
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DesiredRole { get; set; }
    public SeniorityLevel? SeniorityEstimate { get; set; }
    public List<string> TopSkills { get; set; } = new();
    public int MatchScore { get; set; }
    public string? MatchReason { get; set; }
    public int Rank { get; set; }
    public Availability Availability { get; set; }
}

public class ShortlistDetailResponse : ShortlistResponse
{
    public List<ShortlistCandidateResponse> Candidates { get; set; } = new();
}
