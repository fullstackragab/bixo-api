using System.ComponentModel.DataAnnotations;
using bixo_api.Models.Enums;

namespace bixo_api.Models.DTOs.Shortlist;

public class CreateShortlistRequest
{
    [Required]
    public string RoleTitle { get; set; } = string.Empty;

    public List<string> TechStackRequired { get; set; } = new();
    public SeniorityLevel? SeniorityRequired { get; set; }
    public string? LocationPreference { get; set; }
    public bool RemoteAllowed { get; set; } = true;
    public string? AdditionalNotes { get; set; }
}
