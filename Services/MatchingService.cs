using System.Text.Json;
using Dapper;
using bixo_api.Data;
using bixo_api.Models.Entities;
using bixo_api.Models.Enums;
using bixo_api.Services.Interfaces;

namespace bixo_api.Services;

public class MatchingService : IMatchingService
{
    private readonly IDbConnectionFactory _db;

    public MatchingService(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<List<MatchResult>> FindMatchesAsync(ShortlistRequest request, int maxResults = 15)
    {
        using var connection = _db.CreateConnection();

        var candidates = await connection.QueryAsync<dynamic>(@"
            SELECT c.id, c.user_id, c.first_name, c.last_name, c.desired_role, c.seniority_estimate,
                   c.availability, c.location_preference, c.remote_preference, c.profile_visible, c.open_to_opportunities,
                   u.last_active_at
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            WHERE c.profile_visible = TRUE AND c.open_to_opportunities = TRUE");

        // Get skills for all candidates
        var skills = await connection.QueryAsync<dynamic>(@"
            SELECT candidate_id, skill_name, confidence_score
            FROM candidate_skills");

        var skillsDict = skills
            .GroupBy(s => (Guid)s.candidate_id)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Get recommendations for all candidates
        var recommendations = await connection.QueryAsync<dynamic>(@"
            SELECT candidate_id, id
            FROM candidate_recommendations");

        var recommendationsDict = recommendations
            .GroupBy(r => (Guid)r.candidate_id)
            .ToDictionary(g => g.Key, g => g.Count());

        var results = new List<MatchResult>();

        foreach (var candidateData in candidates)
        {
            var candidateId = (Guid)candidateData.id;
            var candidateSkills = skillsDict.ContainsKey(candidateId) ? skillsDict[candidateId] : new List<dynamic>();
            var recommendationCount = recommendationsDict.ContainsKey(candidateId) ? recommendationsDict[candidateId] : 0;

            var candidate = new
            {
                Id = candidateId,
                UserId = (Guid)candidateData.user_id,
                DesiredRole = candidateData.desired_role as string,
                SeniorityEstimate = candidateData.seniority_estimate as int?,
                Availability = (Availability)(candidateData.availability ?? 0),
                LocationPreference = candidateData.location_preference as string,
                RemotePreference = (RemotePreference)(candidateData.remote_preference ?? 0),
                LastActiveAt = (DateTime)candidateData.last_active_at,
                Skills = candidateSkills,
                RecommendationCount = recommendationCount
            };

            var score = CalculateMatchScore(candidate, request);
            if (score > 20)
            {
                results.Add(new MatchResult
                {
                    CandidateId = candidateId,
                    Score = score,
                    Reason = GenerateMatchReason(candidate, request, score)
                });
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();
    }

    public int CalculateMatchScore(Candidate candidate, ShortlistRequest request)
    {
        // This method signature must be maintained for the interface
        // but we need to adapt it to work with dynamic data
        throw new NotImplementedException("Use the dynamic overload instead");
    }

    private int CalculateMatchScore(dynamic candidate, ShortlistRequest request)
    {
        double score = 0;

        // Skill match (35%)
        var requiredSkills = ParseTechStack(request.TechStackRequired);
        if (requiredSkills.Any())
        {
            var candidateSkills = ((IEnumerable<dynamic>)candidate.Skills)
                .Select(s => ((string)s.skill_name).ToLower())
                .ToHashSet();

            var matchedSkills = requiredSkills.Count(rs =>
                candidateSkills.Any(cs => cs.Contains(rs.ToLower()) || rs.ToLower().Contains(cs)));

            var skillScore = requiredSkills.Count > 0 ? (double)matchedSkills / requiredSkills.Count : 0;

            // Weight by confidence scores
            var avgConfidence = ((IEnumerable<dynamic>)candidate.Skills)
                .Where(s => requiredSkills.Any(rs =>
                    ((string)s.skill_name).ToLower().Contains(rs.ToLower()) ||
                    rs.ToLower().Contains(((string)s.skill_name).ToLower())))
                .Select(s => (double)s.confidence_score)
                .DefaultIfEmpty(0)
                .Average();

            score += (skillScore * 0.7 + avgConfidence * 0.3) * 35;
        }
        else
        {
            score += 20; // Partial score if no specific skills required
        }

        // Seniority match (20%)
        if (request.SeniorityRequired.HasValue && candidate.SeniorityEstimate != null)
        {
            var diff = Math.Abs((int)request.SeniorityRequired.Value - (int)candidate.SeniorityEstimate);
            var seniorityScore = diff switch
            {
                0 => 1.0,
                1 => 0.7,
                2 => 0.4,
                _ => 0.2
            };
            score += seniorityScore * 20;
        }
        else
        {
            score += 10; // Partial score if no seniority specified
        }

        // Role relevance (15%)
        if (!string.IsNullOrEmpty(request.RoleTitle) && !string.IsNullOrEmpty(candidate.DesiredRole))
        {
            var roleRelevance = CalculateTextSimilarity(
                request.RoleTitle.ToLower(),
                ((string)candidate.DesiredRole).ToLower());
            score += roleRelevance * 15;
        }
        else
        {
            score += 7.5;
        }

        // Activity level (10%)
        var daysSinceActive = (DateTime.UtcNow - (DateTime)candidate.LastActiveAt).TotalDays;
        var activityScore = daysSinceActive switch
        {
            < 1 => 1.0,
            < 7 => 0.9,
            < 14 => 0.7,
            < 30 => 0.5,
            < 60 => 0.3,
            _ => 0.1
        };
        score += activityScore * 10;

        // Availability (10%)
        var availabilityScore = (Availability)candidate.Availability switch
        {
            Availability.Open => 1.0,
            Availability.Passive => 0.5,
            Availability.NotNow => 0.2,
            _ => 0.5
        };
        score += availabilityScore * 10;

        // Recommendations (10%)
        var recommendationScore = (int)candidate.RecommendationCount switch
        {
            >= 5 => 1.0,
            >= 3 => 0.8,
            >= 1 => 0.5,
            _ => 0.0
        };
        score += recommendationScore * 10;

        // Location/Remote matching bonus
        if (request.RemoteAllowed && (RemotePreference)candidate.RemotePreference == RemotePreference.Remote)
        {
            score += 5;
        }
        else if (!string.IsNullOrEmpty(request.LocationPreference) &&
                 !string.IsNullOrEmpty(candidate.LocationPreference) &&
                 ((string)candidate.LocationPreference).ToLower().Contains(request.LocationPreference.ToLower()))
        {
            score += 5;
        }

        return (int)Math.Min(100, Math.Max(0, score));
    }

    public string GenerateMatchReason(Candidate candidate, ShortlistRequest request, int score)
    {
        // This method signature must be maintained for the interface
        // but we need to adapt it to work with dynamic data
        throw new NotImplementedException("Use the dynamic overload instead");
    }

    private string GenerateMatchReason(dynamic candidate, ShortlistRequest request, int score)
    {
        var reasons = new List<string>();

        // Skills
        var requiredSkills = ParseTechStack(request.TechStackRequired);
        if (requiredSkills.Any())
        {
            var candidateSkills = ((IEnumerable<dynamic>)candidate.Skills)
                .Select(s => ((string)s.skill_name).ToLower())
                .ToHashSet();

            var matched = requiredSkills.Where(rs =>
                candidateSkills.Any(cs => cs.Contains(rs.ToLower()) || rs.ToLower().Contains(cs))).ToList();

            if (matched.Any())
            {
                reasons.Add($"Matches {matched.Count}/{requiredSkills.Count} required skills: {string.Join(", ", matched.Take(3))}");
            }
        }

        // Seniority
        if (candidate.SeniorityEstimate != null)
        {
            var seniority = (SeniorityLevel)(int)candidate.SeniorityEstimate;
            reasons.Add($"{seniority} level");
        }

        // Availability
        if ((Availability)candidate.Availability == Availability.Open)
        {
            reasons.Add("Actively looking");
        }

        // Recommendations
        if ((int)candidate.RecommendationCount > 0)
        {
            reasons.Add($"{candidate.RecommendationCount} recommendation(s)");
        }

        // Remote
        if (request.RemoteAllowed && (RemotePreference)candidate.RemotePreference == RemotePreference.Remote)
        {
            reasons.Add("Prefers remote");
        }

        return string.Join(". ", reasons) + ".";
    }

    private List<string> ParseTechStack(string? techStackJson)
    {
        if (string.IsNullOrEmpty(techStackJson)) return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(techStackJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private double CalculateTextSimilarity(string text1, string text2)
    {
        var words1 = text1.Split(new[] { ' ', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var words2 = text2.Split(new[] { ' ', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (!words1.Any() || !words2.Any()) return 0;

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return (double)intersection / union;
    }
}
