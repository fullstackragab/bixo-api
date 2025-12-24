using bixo_api.Models.DTOs.Location;
using bixo_api.Models.Enums;

namespace bixo_api.Models.DTOs.Candidate;

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

    // Legacy field (kept for backwards compatibility)
    public string? LocationPreference { get; set; }

    // Structured location data
    public CandidateLocationResponse? Location { get; set; }

    // Work mode preference (remote, onsite, hybrid, flexible)
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

    /// <summary>
    /// Display string combining location and work mode for UI
    /// e.g., "Berlin, Germany · Open to remote" or "Remote only"
    /// </summary>
    public string LocationDisplayText => FormatLocationDisplay();

    private string FormatLocationDisplay()
    {
        var parts = new List<string>();

        if (Location != null && !string.IsNullOrEmpty(Location.DisplayText))
            parts.Add(Location.DisplayText);

        var workMode = RemotePreference switch
        {
            Enums.RemotePreference.Remote => "Remote only",
            Enums.RemotePreference.Hybrid => "Open to hybrid",
            Enums.RemotePreference.Onsite => "Onsite only",
            Enums.RemotePreference.Flexible => "Flexible",
            _ => null
        };

        if (workMode != null)
            parts.Add(workMode);

        if (Location?.WillingToRelocate == true)
            parts.Add("Open to relocate");

        return string.Join(" · ", parts);
    }
}

public class CandidateSkillResponse
{
    public Guid Id { get; set; }
    public string SkillName { get; set; } = string.Empty;
    public double ConfidenceScore { get; set; }
    public SkillCategory Category { get; set; }
    public bool IsVerified { get; set; }
}
