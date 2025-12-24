using bixo_api.Models.Enums;

namespace bixo_api.Models.Entities;

public class CompanyMember
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public CompanyRole Role { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Company Company { get; set; } = null!;
    public User User { get; set; } = null!;
}
