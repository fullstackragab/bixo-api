using System.ComponentModel.DataAnnotations;

namespace bixo_api.Models.DTOs.Auth;

public class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
