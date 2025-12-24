using pixo_api.Models.Enums;

namespace pixo_api.Models.DTOs.Company;

public class TalentSearchRequest
{
    public string? Query { get; set; }
    public List<string>? Skills { get; set; }
    public SeniorityLevel? Seniority { get; set; }
    public Availability? Availability { get; set; }
    public RemotePreference? RemotePreference { get; set; }
    public string? Location { get; set; }
    public int? LastActiveDays { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
