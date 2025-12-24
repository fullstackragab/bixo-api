using pixo_api.Models.Enums;

namespace pixo_api.Models.Entities;

public class CandidateSkill
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public string SkillName { get; set; } = string.Empty;
    public double ConfidenceScore { get; set; }
    public SkillCategory Category { get; set; }
    public bool IsVerified { get; set; }

    // Navigation
    public Candidate Candidate { get; set; } = null!;
}
