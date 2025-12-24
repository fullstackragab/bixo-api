namespace pixo_api.Models.Entities;

public class CandidateProfileView
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Candidate Candidate { get; set; } = null!;
    public Company Company { get; set; } = null!;
}
