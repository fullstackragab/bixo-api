using System.ComponentModel.DataAnnotations;

namespace bixo_api.Models.DTOs.Shortlist;

/// <summary>
/// DTO for public shortlist request submission (no authentication required).
/// Companies submit requests via this endpoint without creating accounts.
/// </summary>
public class PublicShortlistRequestDto
{
    /// <summary>
    /// The role title being hired for (e.g., "Senior Backend Engineer").
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 3)]
    public string RoleTitle { get; set; } = string.Empty;

    /// <summary>
    /// Required tech stack (e.g., "Python, Django, PostgreSQL").
    /// </summary>
    [Required]
    [StringLength(500, MinimumLength = 2)]
    public string TechStack { get; set; } = string.Empty;

    /// <summary>
    /// Seniority level (e.g., "Senior", "Mid", "Lead").
    /// </summary>
    [Required]
    [StringLength(50)]
    public string Seniority { get; set; } = string.Empty;

    /// <summary>
    /// Location preference (e.g., "Remote", "Berlin, Germany", "US timezone").
    /// </summary>
    [Required]
    [StringLength(200)]
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Additional context or requirements (optional).
    /// </summary>
    [StringLength(2000)]
    public string? Notes { get; set; }

    /// <summary>
    /// Company email address (primary identifier).
    /// </summary>
    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string CompanyEmail { get; set; } = string.Empty;

    /// <summary>
    /// Company name (optional, can be inferred from email domain later).
    /// </summary>
    [StringLength(200)]
    public string? CompanyName { get; set; }

    /// <summary>
    /// Expected timeline (optional, e.g., "ASAP", "Next 2 weeks").
    /// </summary>
    [StringLength(100)]
    public string? Timeline { get; set; }
}
