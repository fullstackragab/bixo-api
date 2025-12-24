using pixo_api.Models.Enums;

namespace pixo_api.Models.DTOs.Candidate;

public class CandidateProfileResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? CvFileName { get; set; }
    public string? CvDownloadUrl { get; set; }
    public string? DesiredRole { get; set; }
    public string? LocationPreference { get; set; }
    public RemotePreference? RemotePreference { get; set; }
    public Availability Availability { get; set; }
    public bool OpenToOpportunities { get; set; }
    public bool ProfileVisible { get; set; }
    public SeniorityLevel? SeniorityEstimate { get; set; }
    public List<CandidateSkillResponse> Skills { get; set; } = new();
    public int RecommendationsCount { get; set; }
    public int ProfileViewsCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
}

public class CandidateSkillResponse
{
    public Guid Id { get; set; }
    public string SkillName { get; set; } = string.Empty;
    public double ConfidenceScore { get; set; }
    public SkillCategory Category { get; set; }
    public bool IsVerified { get; set; }
}
