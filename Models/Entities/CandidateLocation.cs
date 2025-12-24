namespace bixo_api.Models.Entities;

public class CandidateLocation
{
    public Guid CandidateId { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Timezone { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool WillingToRelocate { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Candidate Candidate { get; set; } = null!;
}
