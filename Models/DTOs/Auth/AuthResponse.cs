using bixo_api.Models.Enums;

namespace bixo_api.Models.DTOs.Auth;

public class AuthResponse
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public UserType UserType { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public Guid? CandidateId { get; set; }
    public Guid? CompanyId { get; set; }
}
