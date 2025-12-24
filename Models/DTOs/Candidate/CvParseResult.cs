using pixo_api.Models.Enums;

namespace pixo_api.Models.DTOs.Candidate;

public class CvParseResult
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Summary { get; set; }
    public SeniorityLevel? SeniorityEstimate { get; set; }
    public int? YearsOfExperience { get; set; }
    public List<ParsedSkill> Skills { get; set; } = new();
    public List<string> RoleTypes { get; set; } = new();
    public string? RawJson { get; set; }
}

public class ParsedSkill
{
    public string Name { get; set; } = string.Empty;
    public double ConfidenceScore { get; set; }
    public SkillCategory Category { get; set; }
}
