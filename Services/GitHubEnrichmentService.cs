using System.Text.Json;
using System.Text.RegularExpressions;
using bixo_api.Configuration;
using bixo_api.Data;
using bixo_api.Services.Interfaces;
using Dapper;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace bixo_api.Services;

public class GitHubEnrichmentService : IGitHubEnrichmentService
{
    private readonly HttpClient _httpClient;
    private readonly IDbConnectionFactory _db;
    private readonly OpenAISettings _openAISettings;
    private readonly ILogger<GitHubEnrichmentService> _logger;

    public GitHubEnrichmentService(
        IHttpClientFactory httpClientFactory,
        IDbConnectionFactory db,
        IOptions<OpenAISettings> openAISettings,
        ILogger<GitHubEnrichmentService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("GitHub");
        _db = db;
        _openAISettings = openAISettings.Value;
        _logger = logger;
    }

    public async Task<GitHubEnrichmentResult?> GenerateSummaryAsync(string githubUrl)
    {
        var username = ExtractUsername(githubUrl);
        if (string.IsNullOrEmpty(username))
        {
            _logger.LogWarning("Could not extract GitHub username from URL: {Url}", githubUrl);
            return null;
        }

        try
        {
            // Fetch user's public repositories (basic metadata only)
            var reposUrl = $"https://api.github.com/users/{username}/repos?per_page=30&sort=updated";
            var response = await _httpClient.GetAsync(reposUrl);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub API returned {StatusCode} for user {Username}",
                    response.StatusCode, username);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var repos = JsonSerializer.Deserialize<List<GitHubApiRepo>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (repos == null || repos.Count == 0)
            {
                _logger.LogInformation("No public repos found for GitHub user {Username}", username);
                return null;
            }

            // Filter out forks and get notable repos
            var ownRepos = repos.Where(r => !r.Fork).ToList();
            var notableRepos = ownRepos
                .OrderByDescending(r => r.Stargazers_Count + r.Forks_Count)
                .ThenByDescending(r => r.Updated_At)
                .Take(5)
                .ToList();

            // Fetch README content for top repos
            var readmeContents = new List<ReadmeInfo>();
            foreach (var repo in notableRepos.Take(5))
            {
                var readmeContent = await FetchReadmeAsync(username, repo.Name);
                if (!string.IsNullOrEmpty(readmeContent) && readmeContent.Length >= 100)
                {
                    readmeContents.Add(new ReadmeInfo
                    {
                        RepoName = repo.Name,
                        Description = repo.Description,
                        Language = repo.Language,
                        Stars = repo.Stargazers_Count,
                        Content = readmeContent.Length > 2000
                            ? readmeContent.Substring(0, 2000)
                            : readmeContent
                    });
                }
            }

            // Get top languages
            var topLanguages = ownRepos
                .Where(r => !string.IsNullOrEmpty(r.Language))
                .GroupBy(r => r.Language)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key!)
                .ToList();

            // Build notable repo info for result
            var notableRepoInfo = notableRepos
                .Select(r => new GitHubRepoInfo
                {
                    Name = r.Name,
                    Description = r.Description,
                    Language = r.Language,
                    Stars = r.Stargazers_Count,
                    Forks = r.Forks_Count,
                    UpdatedAt = r.Updated_At
                })
                .ToList();

            // Generate summary using AI
            var summary = await GenerateSummaryWithAIAsync(username, ownRepos.Count, topLanguages, readmeContents);

            return new GitHubEnrichmentResult
            {
                Username = username,
                Summary = summary,
                PublicRepoCount = ownRepos.Count,
                TopLanguages = topLanguages,
                NotableRepos = notableRepoInfo
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching GitHub data for user {Username}", username);
            return null;
        }
    }

    private async Task<string> GenerateSummaryWithAIAsync(
        string username,
        int repoCount,
        List<string> topLanguages,
        List<ReadmeInfo> readmeContents)
    {
        // Fallback if no API key or no meaningful content
        if (string.IsNullOrEmpty(_openAISettings.ApiKey) || readmeContents.Count == 0)
        {
            return GenerateFallbackSummary(repoCount, topLanguages);
        }

        try
        {
            var client = new ChatClient(_openAISettings.Model, _openAISettings.ApiKey);

            var systemPrompt = @"You are summarizing a developer's public work for a recruiter's quick review.

Your task:
1. Read the provided README excerpts
2. IGNORE boilerplate (framework setup, getting started guides, installation instructions)
3. FOCUS on what they actually built - only what is clearly stated, not inferred

Output format (EXACTLY this structure):
1. One short intro sentence - anchor it in ""public project documentation"", not ""the candidate""
2. 2-4 bullet points - only what's clearly documented, no marketing language
3. One short closing sentence - descriptive, not evaluative

Rules:
- NO praise words (strong, excellent, expert, impressive, highlights skills)
- NO marketing language (scalable, performant, production-ready, optimized)
- NO judgment or scoring
- NO mention of GitHub, AI, or ""repositories""
- Bullets: state WHAT was built and WITH WHAT tech, nothing more
- Keep it observational, not declarative
- If all content is boilerplate, write one neutral sentence about their technology focus

Example output:
""Based on public project documentation, this profile reflects experience building web and mobile applications using TypeScript.

• Built a mobile-first platform using Expo React Native with media-rich content feeds
• Developed a web application using Next.js and Tailwind CSS for content presentation
• Implemented a Phantom wallet integration for Solana-based transactions

Overall, the documented work spans full-stack web development and cross-platform mobile applications."";";

            var userContent = new System.Text.StringBuilder();
            userContent.AppendLine($"Developer has {repoCount} public repositories.");
            userContent.AppendLine($"Primary languages: {string.Join(", ", topLanguages)}");
            userContent.AppendLine();
            userContent.AppendLine("README excerpts from their notable repositories:");
            userContent.AppendLine();

            foreach (var readme in readmeContents)
            {
                userContent.AppendLine($"--- {readme.RepoName} ({readme.Language ?? "unknown"}, {readme.Stars} stars) ---");
                if (!string.IsNullOrEmpty(readme.Description))
                    userContent.AppendLine($"Description: {readme.Description}");
                userContent.AppendLine(readme.Content);
                userContent.AppendLine();
            }

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userContent.ToString())
            };

            var completion = await client.CompleteChatAsync(messages);
            var summary = completion.Value.Content[0].Text?.Trim();

            if (string.IsNullOrEmpty(summary))
            {
                return GenerateFallbackSummary(repoCount, topLanguages);
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI summary generation failed, using fallback");
            return GenerateFallbackSummary(repoCount, topLanguages);
        }
    }

    private static string GenerateFallbackSummary(int repoCount, List<string> topLanguages)
    {
        if (repoCount == 0)
        {
            return "Limited public project documentation available.";
        }

        var languageStr = topLanguages.Count switch
        {
            0 => "",
            1 => $"working primarily with {topLanguages[0]}",
            2 => $"working primarily with {topLanguages[0]} and {topLanguages[1]}",
            _ => $"working primarily with {string.Join(", ", topLanguages.Take(topLanguages.Count - 1))}, and {topLanguages.Last()}"
        };

        var repoDesc = repoCount switch
        {
            >= 20 => $"maintains a substantial portfolio of {repoCount} public repositories",
            >= 10 => $"has {repoCount} public repositories",
            _ => $"has {repoCount} public " + (repoCount == 1 ? "repository" : "repositories")
        };

        return string.IsNullOrEmpty(languageStr)
            ? $"This developer {repoDesc}."
            : $"This developer {repoDesc}, {languageStr}.";
    }

    private async Task<string?> FetchReadmeAsync(string username, string repoName)
    {
        try
        {
            var readmeUrl = $"https://api.github.com/repos/{username}/{repoName}/readme";
            var response = await _httpClient.GetAsync(readmeUrl);

            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync();
            var readmeData = JsonSerializer.Deserialize<GitHubReadmeResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (readmeData?.Content == null)
                return null;

            // Decode base64 content
            var bytes = Convert.FromBase64String(readmeData.Content);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    public async Task EnrichCandidateAsync(Guid candidateId)
    {
        using var connection = _db.CreateConnection();

        var candidate = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT github_url, github_summary
            FROM candidates
            WHERE id = @Id",
            new { Id = candidateId });

        if (candidate == null)
        {
            _logger.LogWarning("Candidate {CandidateId} not found for GitHub enrichment", candidateId);
            return;
        }

        var githubUrl = candidate.github_url as string;
        if (string.IsNullOrEmpty(githubUrl))
        {
            _logger.LogDebug("No GitHub URL for candidate {CandidateId}, skipping enrichment", candidateId);
            return;
        }

        // Skip if already enriched (unless refresh is forced)
        if (!string.IsNullOrEmpty(candidate.github_summary as string))
        {
            _logger.LogDebug("GitHub summary already exists for candidate {CandidateId}", candidateId);
            return;
        }

        var result = await GenerateSummaryAsync(githubUrl);
        if (result == null)
        {
            _logger.LogInformation("Could not generate GitHub summary for candidate {CandidateId}", candidateId);
            return;
        }

        await connection.ExecuteAsync(@"
            UPDATE candidates
            SET github_summary = @Summary,
                github_summary_generated_at = @GeneratedAt,
                updated_at = @GeneratedAt
            WHERE id = @Id",
            new
            {
                Summary = result.Summary,
                GeneratedAt = DateTime.UtcNow,
                Id = candidateId
            });

        _logger.LogInformation("GitHub summary generated for candidate {CandidateId}: {Summary}",
            candidateId, result.Summary);
    }

    public string? ExtractUsername(string? githubUrl)
    {
        if (string.IsNullOrWhiteSpace(githubUrl))
            return null;

        var url = githubUrl.Trim().TrimEnd('/');

        // If it's just a username (no slashes, no domain)
        if (!url.Contains('/') && !url.Contains('.'))
        {
            return url.TrimStart('@');
        }

        // Try to extract from URL
        var match = Regex.Match(url, @"github\.com/([^/\s?#]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var username = match.Groups[1].Value;
            // Filter out GitHub reserved paths
            var reserved = new[] { "settings", "notifications", "explore", "marketplace", "pulls", "issues", "sponsors" };
            if (!reserved.Contains(username.ToLower()))
            {
                return username;
            }
        }

        return null;
    }

    // Internal classes
    private class GitHubApiRepo
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Language { get; set; }
        public bool Fork { get; set; }
        public int Stargazers_Count { get; set; }
        public int Forks_Count { get; set; }
        public DateTime? Updated_At { get; set; }
    }

    private class GitHubReadmeResponse
    {
        public string? Content { get; set; }
        public string? Encoding { get; set; }
    }

    private class ReadmeInfo
    {
        public string RepoName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Language { get; set; }
        public int Stars { get; set; }
        public string Content { get; set; } = string.Empty;
    }
}
