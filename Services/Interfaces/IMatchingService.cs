using pixo_api.Models.Entities;

namespace pixo_api.Services.Interfaces;

public interface IMatchingService
{
    Task<List<MatchResult>> FindMatchesAsync(ShortlistRequest request, int maxResults = 15);
    int CalculateMatchScore(Candidate candidate, ShortlistRequest request);
    string GenerateMatchReason(Candidate candidate, ShortlistRequest request, int score);
}

public class MatchResult
{
    public Guid CandidateId { get; set; }
    public int Score { get; set; }
    public string Reason { get; set; } = string.Empty;
}
