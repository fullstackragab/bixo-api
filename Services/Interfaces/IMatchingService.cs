using bixo_api.Models.Entities;

namespace bixo_api.Services.Interfaces;

public interface IMatchingService
{
    /// <summary>
    /// Find matching candidates for a shortlist request.
    /// </summary>
    /// <param name="request">The shortlist request</param>
    /// <param name="maxResults">Maximum number of results to return</param>
    /// <param name="excludeCandidateIds">Candidate IDs to exclude (e.g., from previous shortlists)</param>
    /// <param name="isFollowUp">Whether this is a follow-up shortlist (enables freshness boost)</param>
    /// <param name="previousShortlistCreatedAt">When the previous shortlist was created (for freshness scoring)</param>
    Task<List<MatchResult>> FindMatchesAsync(
        ShortlistRequest request,
        int maxResults = 15,
        HashSet<Guid>? excludeCandidateIds = null,
        bool isFollowUp = false,
        DateTime? previousShortlistCreatedAt = null);

    int CalculateMatchScore(Candidate candidate, ShortlistRequest request);
    string GenerateMatchReason(Candidate candidate, ShortlistRequest request, int score);
}

public class MatchResult
{
    public Guid CandidateId { get; set; }
    public int Score { get; set; }
    public string Reason { get; set; } = string.Empty;

    // For follow-up shortlists: indicates if this is a re-included candidate
    public bool IsNew { get; set; } = true;

    // For re-included candidates: why they were re-included
    public string? ReInclusionReason { get; set; }
}
