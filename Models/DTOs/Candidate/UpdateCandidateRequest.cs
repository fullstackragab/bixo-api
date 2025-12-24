using pixo_api.Models.Enums;

namespace pixo_api.Models.DTOs.Candidate;

public class UpdateCandidateRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? DesiredRole { get; set; }
    public string? LocationPreference { get; set; }
    public RemotePreference? RemotePreference { get; set; }
    public Availability? Availability { get; set; }
    public bool? OpenToOpportunities { get; set; }
    public bool? ProfileVisible { get; set; }
}
