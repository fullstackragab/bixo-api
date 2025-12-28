using System.ComponentModel.DataAnnotations;

namespace bixo_api.Models.DTOs.Auth;

public class ResetPasswordRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;
}
