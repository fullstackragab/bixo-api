using bixo_api.Models.Enums;

namespace bixo_api.Models.DTOs.Candidate;

public class UpdateSkillsRequest
{
    public List<SkillUpdateItem> Skills { get; set; } = new();
}

public class SkillUpdateItem
{
    public Guid? Id { get; set; }
    public string SkillName { get; set; } = string.Empty;
    public SkillCategory Category { get; set; }
    public bool IsVerified { get; set; }
    public bool Delete { get; set; }
    public SkillLevel SkillLevel { get; set; } = SkillLevel.Secondary;
}
