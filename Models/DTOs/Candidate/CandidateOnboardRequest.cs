using bixo_api.Models.Enums;

namespace bixo_api.Models.DTOs.Candidate;

public class CandidateOnboardRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? GitHubUrl { get; set; }
    public string? DesiredRole { get; set; }
    public string? LocationPreference { get; set; }
    public RemotePreference? RemotePreference { get; set; }
    public Availability? Availability { get; set; }
}
