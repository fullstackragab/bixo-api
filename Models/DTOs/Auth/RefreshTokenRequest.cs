using System.ComponentModel.DataAnnotations;

namespace bixo_api.Models.DTOs.Auth;

public class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
