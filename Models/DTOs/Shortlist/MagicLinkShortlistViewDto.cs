using bixo_api.Models.Enums;

namespace bixo_api.Models.DTOs.Shortlist;

/// <summary>
/// Limited shortlist view for magic-link (unauthenticated) access.
/// Does NOT include candidate details until approved and delivered.
/// </summary>
public class MagicLinkShortlistViewDto
{
    public Guid Id { get; set; }

    /// <summary>
    /// The role being hired for.
    /// </summary>
    public string RoleTitle { get; set; } = string.Empty;

    /// <summary>
    /// Tech stack requirements.
    /// </summary>
    public string? TechStack { get; set; }

    /// <summary>
    /// Seniority level.
    /// </summary>
    public string? Seniority { get; set; }

    /// <summary>
    /// Location preference.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Current shortlist status.
    /// </summary>
    public ShortlistStatus Status { get; set; }

    /// <summary>
    /// Human-readable status label.
    /// </summary>
    public string StatusLabel => Status switch
    {
        ShortlistStatus.Submitted => "Request received",
        ShortlistStatus.Processing => "Finding candidates",
        ShortlistStatus.PricingPending => "Ready for review",
        ShortlistStatus.Approved => "Approved - preparing delivery",
        ShortlistStatus.Delivered => "Delivered",
        ShortlistStatus.Completed => "Completed",
        ShortlistStatus.Cancelled => "Cancelled",
        ShortlistStatus.Declined => "Declined",
        ShortlistStatus.AwaitingAdjustment => "Awaiting your response",
        _ => "Unknown"
    };

    /// <summary>
    /// Number of candidates (shown when status is PricingPending or later).
    /// </summary>
    public int? CandidateCount { get; set; }

    /// <summary>
    /// Proposed price (shown when status is PricingPending).
    /// </summary>
    public decimal? ProposedPrice { get; set; }

    /// <summary>
    /// Admin notes about the scope (shown when status is PricingPending).
    /// </summary>
    public string? ScopeNotes { get; set; }

    /// <summary>
    /// When the request was submitted.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// True if approval action is available.
    /// </summary>
    public bool CanApprove => Status == ShortlistStatus.PricingPending;

    /// <summary>
    /// True if full candidate profiles are available (after delivery).
    /// </summary>
    public bool ProfilesAvailable => Status == ShortlistStatus.Delivered || Status == ShortlistStatus.Completed;

    /// <summary>
    /// Candidate previews (limited info, shown when PricingPending).
    /// No identifying info - just role, skills, region.
    /// </summary>
    public List<MagicLinkCandidatePreviewDto> CandidatePreviews { get; set; } = new();

    /// <summary>
    /// Full candidate profiles (shown after delivery).
    /// </summary>
    public List<MagicLinkCandidateDto>? Candidates { get; set; }
}

/// <summary>
/// Limited candidate preview for magic-link access (before approval).
/// Does NOT include identifying information.
/// </summary>
public class MagicLinkCandidatePreviewDto
{
    /// <summary>
    /// Preview index (not the actual candidate ID).
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Current/desired role.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Seniority level.
    /// </summary>
    public string? Seniority { get; set; }

    /// <summary>
    /// Top skills.
    /// </summary>
    public List<string> TopSkills { get; set; } = new();

    /// <summary>
    /// Region (country only, not city).
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Why this candidate matches.
    /// </summary>
    public string? MatchReason { get; set; }
}

/// <summary>
/// Full candidate info for magic-link access (after delivery).
/// </summary>
public class MagicLinkCandidateDto
{
    public Guid Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Role { get; set; }
    public string? Seniority { get; set; }
    public List<string> Skills { get; set; } = new();
    public string? Location { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? GitHubUrl { get; set; }
    public string? GitHubSummary { get; set; }
    public string? MatchReason { get; set; }
    public int Rank { get; set; }
}
