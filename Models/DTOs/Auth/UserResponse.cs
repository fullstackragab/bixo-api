using bixo_api.Models.Enums;

namespace bixo_api.Models.DTOs.Auth;

public class UserResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public UserType UserType { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
    public Guid? CandidateId { get; set; }
    public Guid? CompanyId { get; set; }
}
