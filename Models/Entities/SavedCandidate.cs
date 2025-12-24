namespace pixo_api.Models.Entities;

public class SavedCandidate
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CandidateId { get; set; }
    public string? Notes { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Company Company { get; set; } = null!;
    public Candidate Candidate { get; set; } = null!;
}
