using bixo_api.Models.Enums;

namespace bixo_api.Models.DTOs.Company;

public class TalentSearchRequest
{
    public string? Query { get; set; }
    public List<string>? Skills { get; set; }
    public SeniorityLevel? Seniority { get; set; }
    public Availability? Availability { get; set; }
    public RemotePreference? RemotePreference { get; set; }

    // Legacy location field (still supported)
    public string? Location { get; set; }

    // Location ranking preferences (these adjust ranking, not filter candidates)
    public LocationRankingOptions? LocationRanking { get; set; }

    public int? LastActiveDays { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Options for location-based ranking in talent search.
/// These are suggestive preferences, not hard filters.
/// </summary>
public class LocationRankingOptions
{
    /// <summary>
    /// Boost candidates who prefer remote work
    /// </summary>
    public bool PreferRemote { get; set; }

    /// <summary>
    /// Boost candidates in the same country
    /// </summary>
    public string? PreferCountry { get; set; }

    /// <summary>
    /// Boost candidates within timezone range (±hours)
    /// </summary>
    public string? PreferTimezone { get; set; }

    /// <summary>
    /// Boost candidates willing to relocate
    /// </summary>
    public bool PreferRelocationFriendly { get; set; }
}
