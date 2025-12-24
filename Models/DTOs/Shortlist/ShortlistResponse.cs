using bixo_api.Models.DTOs.Location;
using bixo_api.Models.Enums;

namespace bixo_api.Models.DTOs.Shortlist;

public class ShortlistResponse
{
    public Guid Id { get; set; }
    public string RoleTitle { get; set; } = string.Empty;
    public List<string> TechStackRequired { get; set; } = new();
    public SeniorityLevel? SeniorityRequired { get; set; }

    // Legacy location field (kept for backwards compatibility)
    public string? LocationPreference { get; set; }

    // Structured hiring location
    public HiringLocationResponse? HiringLocation { get; set; }

    // Legacy field name (use HiringLocation.IsRemote for new implementations)
    public bool RemoteAllowed { get; set; }

    public string? AdditionalNotes { get; set; }
    public ShortlistStatus Status { get; set; }
    public decimal? PricePaid { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int CandidatesCount { get; set; }
}

/// <summary>
/// Hiring location details in shortlist response
/// </summary>
public class HiringLocationResponse
{
    public bool IsRemote { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Timezone { get; set; }

    /// <summary>
    /// Display string for UI (e.g., "Remote" or "Berlin, Germany · Hybrid")
    /// </summary>
    public string DisplayText => FormatDisplayText();

    private string FormatDisplayText()
    {
        if (IsRemote && string.IsNullOrEmpty(Country) && string.IsNullOrEmpty(City))
            return "Remote";

        var parts = new List<string>();

        if (!string.IsNullOrEmpty(City) && !string.IsNullOrEmpty(Country))
            parts.Add($"{City}, {Country}");
        else if (!string.IsNullOrEmpty(City))
            parts.Add(City);
        else if (!string.IsNullOrEmpty(Country))
            parts.Add(Country);

        if (IsRemote)
            parts.Add("Remote-friendly");

        return string.Join(" · ", parts);
    }
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
