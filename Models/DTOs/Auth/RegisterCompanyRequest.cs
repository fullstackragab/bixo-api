using System.ComponentModel.DataAnnotations;

namespace bixo_api.Models.DTOs.Auth;

public class RegisterCompanyRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string CompanyName { get; set; } = string.Empty;

    public string? Industry { get; set; }
}
