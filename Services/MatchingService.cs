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

    // Location scoring weights from design document
    private const int LOCATION_SCORE_REMOTE_MATCH = 25;
    private const int LOCATION_SCORE_SAME_COUNTRY = 15;
    private const int LOCATION_SCORE_SAME_CITY = 25;
    private const int LOCATION_SCORE_TIMEZONE_OVERLAP = 10;
    private const int LOCATION_SCORE_WILLING_TO_RELOCATE = 10;

    public MatchingService(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<List<MatchResult>> FindMatchesAsync(ShortlistRequest request, int maxResults = 15)
    {
        using var connection = _db.CreateConnection();

        // Fetch candidates with their location data
        var candidates = await connection.QueryAsync<dynamic>(@"
            SELECT c.id, c.user_id, c.first_name, c.last_name, c.desired_role, c.seniority_estimate,
                   c.availability, c.location_preference, c.remote_preference, c.profile_visible, c.open_to_opportunities,
                   u.last_active_at,
                   cl.country AS location_country,
                   cl.city AS location_city,
                   cl.timezone AS location_timezone,
                   cl.willing_to_relocate
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            LEFT JOIN candidate_locations cl ON cl.candidate_id = c.id
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
                RemotePreference = (RemotePreference)(candidateData.remote_preference ?? 3), // Default to Flexible
                LastActiveAt = (DateTime)candidateData.last_active_at,
                Skills = candidateSkills,
                RecommendationCount = recommendationCount,
                // Location data
                LocationCountry = candidateData.location_country as string,
                LocationCity = candidateData.location_city as string,
                LocationTimezone = candidateData.location_timezone as string,
                WillingToRelocate = candidateData.willing_to_relocate as bool? ?? false
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

        // Skill match (45% - increased weight as per design)
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

            score += (skillScore * 0.7 + avgConfidence * 0.3) * 45;
        }
        else
        {
            score += 22.5; // Partial score if no specific skills required
        }

        // Role/Seniority match (25% - as per design)
        double roleSeniorityScore = 0;

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
            roleSeniorityScore += seniorityScore * 15;
        }
        else
        {
            roleSeniorityScore += 7.5;
        }

        if (!string.IsNullOrEmpty(request.RoleTitle) && !string.IsNullOrEmpty(candidate.DesiredRole))
        {
            var roleRelevance = CalculateTextSimilarity(
                request.RoleTitle.ToLower(),
                ((string)candidate.DesiredRole).ToLower());
            roleSeniorityScore += roleRelevance * 10;
        }
        else
        {
            roleSeniorityScore += 5;
        }
        score += roleSeniorityScore;

        // Activity level (10% - as per design)
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

        // Availability bonus (5%)
        var availabilityScore = (Availability)candidate.Availability switch
        {
            Availability.Open => 1.0,
            Availability.Passive => 0.5,
            Availability.NotNow => 0.2,
            _ => 0.5
        };
        score += availabilityScore * 5;

        // Recommendations bonus (5%)
        var recommendationScore = (int)candidate.RecommendationCount switch
        {
            >= 5 => 1.0,
            >= 3 => 0.8,
            >= 1 => 0.5,
            _ => 0.0
        };
        score += recommendationScore * 5;

        // Location scoring (5% of total, but up to ~85 points raw before normalization)
        // Never hard filter by location - use for ranking only
        var locationScore = CalculateLocationScore(candidate, request);
        // Normalize location score: max possible is 85 (25+15+25+10+10), scale to 5% of final
        score += (locationScore / 85.0) * 5;

        return (int)Math.Min(100, Math.Max(0, score));
    }

    /// <summary>
    /// Calculate location score based on design document weights.
    /// This is a ranking signal, not a filter.
    /// </summary>
    private int CalculateLocationScore(dynamic candidate, ShortlistRequest request)
    {
        int locationScore = 0;
        var candidateRemote = (RemotePreference)candidate.RemotePreference;

        // Remote role + candidate prefers remote: +25
        if (request.IsRemote && (candidateRemote == RemotePreference.Remote || candidateRemote == RemotePreference.Flexible))
        {
            locationScore += LOCATION_SCORE_REMOTE_MATCH;
        }

        // Get the request's target location (prefer new fields, fall back to legacy)
        var requestCountry = request.LocationCountry ?? request.LocationPreference;
        var requestCity = request.LocationCity;
        var requestTimezone = request.LocationTimezone;

        // Get candidate's location
        var candidateCountry = candidate.LocationCountry as string;
        var candidateCity = candidate.LocationCity as string;
        var candidateTimezone = candidate.LocationTimezone as string;

        // Same country: +15
        if (!string.IsNullOrEmpty(requestCountry) && !string.IsNullOrEmpty(candidateCountry))
        {
            if (string.Equals(requestCountry, candidateCountry, StringComparison.OrdinalIgnoreCase))
            {
                locationScore += LOCATION_SCORE_SAME_COUNTRY;
            }
        }

        // Same city: +25
        if (!string.IsNullOrEmpty(requestCity) && !string.IsNullOrEmpty(candidateCity))
        {
            if (string.Equals(requestCity, candidateCity, StringComparison.OrdinalIgnoreCase))
            {
                locationScore += LOCATION_SCORE_SAME_CITY;
            }
        }

        // Timezone overlap (±2h): +10
        if (!string.IsNullOrEmpty(requestTimezone) && !string.IsNullOrEmpty(candidateTimezone))
        {
            if (IsTimezoneOverlap(requestTimezone, candidateTimezone, 2))
            {
                locationScore += LOCATION_SCORE_TIMEZONE_OVERLAP;
            }
        }

        // Willing to relocate: +10
        if ((bool)candidate.WillingToRelocate)
        {
            locationScore += LOCATION_SCORE_WILLING_TO_RELOCATE;
        }

        return locationScore;
    }

    /// <summary>
    /// Check if two timezones are within the specified hours difference.
    /// Handles common timezone formats like "UTC+2", "Europe/Berlin", "EST", etc.
    /// </summary>
    private bool IsTimezoneOverlap(string tz1, string tz2, int maxHoursDiff)
    {
        // Simple implementation for common cases
        // For MVP, just check if timezones are equal or similar
        if (string.Equals(tz1, tz2, StringComparison.OrdinalIgnoreCase))
            return true;

        // Try to extract UTC offset if available
        var offset1 = ParseTimezoneOffset(tz1);
        var offset2 = ParseTimezoneOffset(tz2);

        if (offset1.HasValue && offset2.HasValue)
        {
            return Math.Abs(offset1.Value - offset2.Value) <= maxHoursDiff;
        }

        return false;
    }

    /// <summary>
    /// Parse timezone offset from string (e.g., "UTC+2" -> 2, "UTC-5" -> -5)
    /// </summary>
    private int? ParseTimezoneOffset(string timezone)
    {
        if (string.IsNullOrEmpty(timezone))
            return null;

        // Common patterns: UTC+X, UTC-X, GMT+X, GMT-X
        var patterns = new[] { "UTC", "GMT" };
        foreach (var prefix in patterns)
        {
            if (timezone.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var remaining = timezone.Substring(prefix.Length);
                if (int.TryParse(remaining, out var offset))
                    return offset;
            }
        }

        // Map common abbreviations (simplified for MVP)
        return timezone.ToUpperInvariant() switch
        {
            "EST" => -5,
            "EDT" => -4,
            "CST" => -6,
            "CDT" => -5,
            "MST" => -7,
            "MDT" => -6,
            "PST" => -8,
            "PDT" => -7,
            "GMT" => 0,
            "UTC" => 0,
            "CET" => 1,
            "CEST" => 2,
            "EET" => 2,
            "EEST" => 3,
            _ => null
        };
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

        // Location context
        var locationReasons = GenerateLocationReason(candidate, request);
        if (!string.IsNullOrEmpty(locationReasons))
        {
            reasons.Add(locationReasons);
        }

        return string.Join(". ", reasons) + ".";
    }

    /// <summary>
    /// Generate a human-readable location match reason for the match summary.
    /// </summary>
    private string GenerateLocationReason(dynamic candidate, ShortlistRequest request)
    {
        var parts = new List<string>();
        var candidateRemote = (RemotePreference)candidate.RemotePreference;

        // Location display
        var candidateCity = candidate.LocationCity as string;
        var candidateCountry = candidate.LocationCountry as string;
        var locationDisplay = !string.IsNullOrEmpty(candidateCity) && !string.IsNullOrEmpty(candidateCountry)
            ? $"{candidateCity}, {candidateCountry}"
            : candidateCity ?? candidateCountry;

        if (!string.IsNullOrEmpty(locationDisplay))
        {
            parts.Add($"Based in {locationDisplay}");
        }

        // Work mode preference
        if (request.IsRemote)
        {
            if (candidateRemote == RemotePreference.Remote)
            {
                parts.Add("prefers remote");
            }
            else if (candidateRemote == RemotePreference.Flexible)
            {
                parts.Add("open to remote");
            }
        }

        // Relocation
        if ((bool)candidate.WillingToRelocate)
        {
            parts.Add("open to relocate");
        }

        if (!parts.Any())
            return string.Empty;

        // Join with proper grammar
        if (parts.Count == 1)
            return parts[0];

        return $"{parts[0]} · {string.Join(" · ", parts.Skip(1))}";
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
