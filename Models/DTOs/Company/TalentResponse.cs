using bixo_api.Models.Enums;

namespace bixo_api.Models.DTOs.Company;

public class TalentResponse
{
    public Guid CandidateId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DesiredRole { get; set; }
    public string? LocationPreference { get; set; }
    public RemotePreference? RemotePreference { get; set; }
    public Availability Availability { get; set; }
    public SeniorityLevel? SeniorityEstimate { get; set; }
    public List<string> TopSkills { get; set; } = new();
    public int RecommendationsCount { get; set; }
    public DateTime LastActiveAt { get; set; }
    public int MatchScore { get; set; }
    public bool IsSaved { get; set; }
}

public class TalentListResponse
{
    public List<TalentResponse> Candidates { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}
