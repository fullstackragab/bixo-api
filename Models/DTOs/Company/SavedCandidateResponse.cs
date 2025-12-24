namespace pixo_api.Models.DTOs.Company;

public class SavedCandidateResponse
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public TalentResponse Candidate { get; set; } = null!;
    public string? Notes { get; set; }
    public DateTime SavedAt { get; set; }
}
