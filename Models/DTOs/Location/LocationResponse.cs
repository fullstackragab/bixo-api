namespace bixo_api.Models.DTOs.Location;

/// <summary>
/// Location data returned in API responses
/// </summary>
public class LocationResponse
{
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Timezone { get; set; }

    /// <summary>
    /// Display string for UI (e.g., "Berlin, Germany" or "Remote")
    /// </summary>
    public string DisplayText => FormatDisplayText();

    private string FormatDisplayText()
    {
        if (!string.IsNullOrEmpty(City) && !string.IsNullOrEmpty(Country))
            return $"{City}, {Country}";
        if (!string.IsNullOrEmpty(City))
            return City;
        if (!string.IsNullOrEmpty(Country))
            return Country;
        return string.Empty;
    }
}

/// <summary>
/// Extended location response for candidates (includes relocation preference)
/// </summary>
public class CandidateLocationResponse : LocationResponse
{
    public bool WillingToRelocate { get; set; }
}

/// <summary>
/// Request to update location data
/// </summary>
public class UpdateLocationRequest
{
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Timezone { get; set; }
}

/// <summary>
/// Request to update candidate location (includes relocation preference)
/// </summary>
public class UpdateCandidateLocationRequest : UpdateLocationRequest
{
    public bool? WillingToRelocate { get; set; }
}

/// <summary>
/// Hiring location for shortlist requests
/// </summary>
public class HiringLocationRequest
{
    /// <summary>
    /// Whether the role is remote-friendly
    /// </summary>
    public bool IsRemote { get; set; } = true;

    /// <summary>
    /// Target country for the role (optional for remote roles)
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Target city for the role (optional)
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// Preferred timezone for the role (optional)
    /// </summary>
    public string? Timezone { get; set; }
}
