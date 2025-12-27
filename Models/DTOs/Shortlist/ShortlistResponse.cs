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

    // Versioning: Link to previous shortlist request
    public Guid? PreviousRequestId { get; set; }

    // Pricing type: 'new', 'follow_up', 'free_regen'
    public string PricingType { get; set; } = "new";

    // Discount applied for follow-up shortlists
    public decimal FollowUpDiscount { get; set; }

    // Count of new candidates (not previously recommended)
    public int NewCandidatesCount { get; set; }

    // Count of re-included candidates (previously recommended)
    public int RepeatedCandidatesCount { get; set; }

    // True if this is a follow-up to a previous shortlist
    public bool IsFollowUp => PreviousRequestId.HasValue;

    // === Pricing proposal fields (visible when status is PricingPending) ===

    /// <summary>Admin-proposed price awaiting company approval</summary>
    public decimal? ProposedPrice { get; set; }

    /// <summary>Expected number of candidates</summary>
    public int? ProposedCandidates { get; set; }

    /// <summary>When the price was proposed</summary>
    public DateTime? ScopeProposedAt { get; set; }

    /// <summary>Notes from admin about the scope</summary>
    public string? ScopeNotes { get; set; }

    // === Outcome tracking (visible to company) ===

    /// <summary>Final outcome of the shortlist request</summary>
    public ShortlistOutcome Outcome { get; set; } = ShortlistOutcome.Pending;

    /// <summary>Explanation for the outcome (e.g., why no suitable candidates)</summary>
    public string? OutcomeReason { get; set; }

    /// <summary>When the outcome was decided</summary>
    public DateTime? OutcomeDecidedAt { get; set; }

    /// <summary>
    /// True if company will not be charged for this shortlist (NoMatch or Cancelled outcome)
    /// </summary>
    public bool WillNotBeCharged => Outcome == ShortlistOutcome.NoMatch || Outcome == ShortlistOutcome.Cancelled;
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

    /// <summary>GitHub profile summary (auto-generated from public repos)</summary>
    public string? GitHubSummary { get; set; }

    // Versioning: TRUE if candidate is new in this shortlist, FALSE if previously recommended
    public bool IsNew { get; set; } = true;

    // If not new, the shortlist where this candidate was previously recommended
    public Guid? PreviouslyRecommendedIn { get; set; }

    // Reason for re-including a previously recommended candidate
    public string? ReInclusionReason { get; set; }

    /// <summary>
    /// Display label for UI (e.g., "New" or "Previously recommended")
    /// </summary>
    public string StatusLabel => IsNew ? "New" : "Previously recommended";

    /// <summary>Candidate's interest response: interested, not_interested, interested_later, or null for pending</summary>
    public string? InterestStatus { get; set; }

    /// <summary>When the candidate responded</summary>
    public DateTime? InterestRespondedAt { get; set; }

    /// <summary>Display label for interest status</summary>
    public string InterestLabel => InterestStatus switch
    {
        "interested" => "Interested",
        "not_interested" => "Not interested",
        "interested_later" => "Interested later",
        _ => "Pending response"
    };
}

public class ShortlistDetailResponse : ShortlistResponse
{
    /// <summary>Full candidate details (only populated after delivery)</summary>
    public List<ShortlistCandidateResponse> Candidates { get; set; } = new();

    /// <summary>Limited candidate previews (shown when status is PricingPending or Approved)</summary>
    public List<ShortlistCandidatePreviewResponse> CandidatePreviews { get; set; } = new();

    /// <summary>True if previews are available (status is PricingPending or Approved)</summary>
    public bool HasPreviews => CandidatePreviews.Count > 0;

    /// <summary>True if full profiles are unlocked (status is Delivered or Completed)</summary>
    public bool ProfilesUnlocked => Candidates.Count > 0;

    /// <summary>Counts by interest status for tab display</summary>
    public InterestStatusCounts InterestCounts { get; set; } = new();
}

public class InterestStatusCounts
{
    public int Interested { get; set; }
    public int Declined { get; set; }
    public int NoResponse { get; set; }
    public int Total => Interested + Declined + NoResponse;
}

/// <summary>
/// Limited candidate preview shown before approval.
/// Does NOT include identifying info (name, LinkedIn, exact location).
/// </summary>
public class ShortlistCandidatePreviewResponse
{
    /// <summary>Preview ID (NOT the actual candidate ID for privacy)</summary>
    public int PreviewId { get; set; }

    /// <summary>Candidate's current/desired role</summary>
    public string? Role { get; set; }

    /// <summary>Seniority level</summary>
    public SeniorityLevel? Seniority { get; set; }

    /// <summary>Top 3-5 skills</summary>
    public List<string> TopSkills { get; set; } = new();

    /// <summary>Availability status</summary>
    public Availability Availability { get; set; }

    /// <summary>Work setup preference (remote/hybrid/onsite)</summary>
    public RemotePreference? WorkSetup { get; set; }

    /// <summary>Location region (country only, not city for privacy)</summary>
    public string? Region { get; set; }

    /// <summary>Why this candidate was selected (match reason)</summary>
    public string? WhyThisCandidate { get; set; }

    /// <summary>Candidate's rank in shortlist</summary>
    public int Rank { get; set; }

    /// <summary>True if public project documentation has been reviewed</summary>
    public bool HasPublicWorkSummary { get; set; }

    /// <summary>Display label for seniority</summary>
    public string SeniorityLabel => Seniority?.ToString() ?? "Not specified";

    /// <summary>Display label for availability</summary>
    public string AvailabilityLabel => Availability switch
    {
        Availability.Open => "Available",
        Availability.NotNow => "Not available now",
        Availability.Passive => "Passively looking",
        _ => "Not specified"
    };

    /// <summary>Display label for work setup</summary>
    public string WorkSetupLabel => WorkSetup switch
    {
        RemotePreference.Remote => "Remote",
        RemotePreference.Hybrid => "Hybrid",
        RemotePreference.Onsite => "On-site",
        RemotePreference.Flexible => "Flexible",
        _ => "Not specified"
    };
}
