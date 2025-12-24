using System.ComponentModel.DataAnnotations;
using bixo_api.Models.DTOs.Location;
using bixo_api.Models.Enums;

namespace bixo_api.Models.DTOs.Shortlist;

public class CreateShortlistRequest
{
    [Required]
    public string RoleTitle { get; set; } = string.Empty;

    public List<string> TechStackRequired { get; set; } = new();
    public SeniorityLevel? SeniorityRequired { get; set; }

    // Legacy field (still supported for backwards compatibility)
    public string? LocationPreference { get; set; }

    // Structured hiring location (preferred)
    public HiringLocationRequest? HiringLocation { get; set; }

    // Legacy field name kept for backwards compatibility
    // Use HiringLocation.IsRemote for new implementations
    public bool? RemoteAllowed { get; set; }

    public string? AdditionalNotes { get; set; }

    /// <summary>
    /// Gets the effective IsRemote value, preferring HiringLocation.IsRemote
    /// </summary>
    public bool GetEffectiveIsRemote()
    {
        if (HiringLocation != null)
            return HiringLocation.IsRemote;
        return RemoteAllowed ?? true;
    }
}
