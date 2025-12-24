using bixo_api.Models.Enums;

namespace bixo_api.Models.Entities;

public class Candidate
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? CvFileKey { get; set; }
    public string? CvOriginalFileName { get; set; }
    public string? DesiredRole { get; set; }
    public string? LocationPreference { get; set; }
    public RemotePreference? RemotePreference { get; set; }
    public Availability Availability { get; set; } = Availability.Open;
    public bool OpenToOpportunities { get; set; } = true;
    public bool ProfileVisible { get; set; } = true;
    public SeniorityLevel? SeniorityEstimate { get; set; }
    public string? ParsedProfileJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;
    public ICollection<CandidateSkill> Skills { get; set; } = new List<CandidateSkill>();
    public ICollection<CandidateRecommendation> Recommendations { get; set; } = new List<CandidateRecommendation>();
    public ICollection<CandidateProfileView> ProfileViews { get; set; } = new List<CandidateProfileView>();
    public ICollection<SavedCandidate> SavedByCompanies { get; set; } = new List<SavedCandidate>();
    public ICollection<ShortlistCandidate> ShortlistEntries { get; set; } = new List<ShortlistCandidate>();
    public CandidateLocation? Location { get; set; }
}
