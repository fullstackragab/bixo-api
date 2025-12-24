using bixo_api.Models.DTOs.Location;
using bixo_api.Models.Enums;

namespace bixo_api.Models.DTOs.Candidate;

public class UpdateCandidateRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? DesiredRole { get; set; }

    // Legacy field (still supported for backwards compatibility)
    public string? LocationPreference { get; set; }

    // Structured location data (preferred)
    public UpdateCandidateLocationRequest? Location { get; set; }

    // Work mode preference (remote, onsite, hybrid, flexible)
    public RemotePreference? RemotePreference { get; set; }

    public Availability? Availability { get; set; }
    public bool? OpenToOpportunities { get; set; }
    public bool? ProfileVisible { get; set; }
}
