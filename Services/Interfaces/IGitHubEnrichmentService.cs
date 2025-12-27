namespace bixo_api.Services.Interfaces;

public interface IGitHubEnrichmentService
{
    /// <summary>
    /// Generates a 1-2 sentence summary of a candidate's public GitHub work.
    /// Returns null if GitHub URL is invalid, profile is empty, or rate limited.
    /// </summary>
    Task<GitHubEnrichmentResult?> GenerateSummaryAsync(string githubUrl);

    /// <summary>
    /// Enriches a candidate's profile with GitHub summary.
    /// Non-blocking - stores result directly in database.
    /// </summary>
    Task EnrichCandidateAsync(Guid candidateId);

    /// <summary>
    /// Extracts GitHub username from various URL formats.
    /// </summary>
    string? ExtractUsername(string? githubUrl);
}

public class GitHubEnrichmentResult
{
    public string Username { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int PublicRepoCount { get; set; }
    public List<string> TopLanguages { get; set; } = new();
    public List<GitHubRepoInfo> NotableRepos { get; set; } = new();
}

public class GitHubRepoInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Language { get; set; }
    public int Stars { get; set; }
    public int Forks { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
