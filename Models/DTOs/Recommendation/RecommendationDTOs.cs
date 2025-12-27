namespace bixo_api.Models.DTOs.Recommendation;

// === Request DTOs ===

/// <summary>
/// Request body for candidate to request a recommendation
/// </summary>
public class RequestRecommendationRequest
{
    public string RecommenderName { get; set; } = string.Empty;
    public string RecommenderEmail { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty; // Manager, Tech Lead, Founder, Peer, Colleague
}

/// <summary>
/// Request body for recommender to submit their recommendation
/// </summary>
public class SubmitRecommendationRequest
{
    public string Content { get; set; } = string.Empty;
    public string? RecommenderRole { get; set; } // e.g., "Engineering Manager"
    public string? RecommenderCompany { get; set; } // Company where they worked with candidate
}

/// <summary>
/// Request body for admin to approve a recommendation
/// </summary>
public class ApproveRecommendationAdminRequest
{
    // No additional fields needed - just the action
}

/// <summary>
/// Request body for admin to reject a recommendation
/// </summary>
public class RejectRecommendationAdminRequest
{
    public string Reason { get; set; } = string.Empty;
}

// === Response DTOs ===

/// <summary>
/// Recommendation as seen by the candidate (full details)
/// </summary>
public class CandidateRecommendationResponse
{
    public Guid Id { get; set; }
    public string RecommenderName { get; set; } = string.Empty;
    public string RecommenderEmail { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string Status { get; set; } = string.Empty; // Pending, Submitted, Approved
    public bool IsSubmitted { get; set; }
    public bool IsApproved { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Recommendation form data for recommender (public page, no auth)
/// </summary>
public class RecommenderFormResponse
{
    public string CandidateName { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public bool IsAlreadySubmitted { get; set; }
    public DateTime? SubmittedAt { get; set; }
}

/// <summary>
/// Recommendation as seen by companies (limited info, only approved ones)
/// </summary>
public class CompanyRecommendationResponse
{
    public string RecommenderName { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string? RecommenderRole { get; set; }
    public string? RecommenderCompany { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
}

/// <summary>
/// Recommendation as seen by admins for review
/// </summary>
public class AdminRecommendationResponse
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    public string RecommenderName { get; set; } = string.Empty;
    public string RecommenderEmail { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string? RecommenderRole { get; set; }
    public string? RecommenderCompany { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // PendingReview, Approved, Rejected
    public bool IsAdminApproved { get; set; }
    public bool IsRejected { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? AdminApprovedAt { get; set; }
}

/// <summary>
/// Summary of candidate's recommendations for shortlist view
/// </summary>
public class CandidateRecommendationsSummary
{
    public Guid CandidateId { get; set; }
    public int ApprovedCount { get; set; }
    public List<CompanyRecommendationResponse> Recommendations { get; set; } = new();
}
