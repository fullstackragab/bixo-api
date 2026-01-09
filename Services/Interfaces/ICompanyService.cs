using bixo_api.Models.DTOs.Company;
using bixo_api.Models.Entities;

namespace bixo_api.Services.Interfaces;

public interface ICompanyService
{
    Task<CompanyProfileResponse?> GetProfileAsync(Guid userId);
    Task<CompanyProfileResponse> UpdateProfileAsync(Guid userId, UpdateCompanyRequest request);
    Task<TalentListResponse> SearchTalentAsync(Guid companyId, TalentSearchRequest request);
    Task<CandidateDetailResponse?> GetCandidateDetailAsync(Guid companyId, Guid candidateId);
    Task<SavedCandidateResponse> SaveCandidateAsync(Guid companyId, SaveCandidateRequest request);
    Task RemoveSavedCandidateAsync(Guid companyId, Guid candidateId);
    Task<List<SavedCandidateResponse>> GetSavedCandidatesAsync(Guid companyId);

    // === Passwordless Company Support ===

    /// <summary>
    /// Find an existing company by contact email, or create a new passwordless company.
    /// Passwordless companies have no user account - they access via magic links.
    /// </summary>
    Task<Company> FindOrCreateByEmailAsync(string email, string? companyName = null);

    /// <summary>
    /// Get company by ID (for internal use).
    /// </summary>
    Task<Company?> GetByIdAsync(Guid companyId);
}

public class CandidateDetailResponse
{
    public Guid Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DesiredRole { get; set; }
    public string? LocationPreference { get; set; }
    public Models.Enums.RemotePreference? RemotePreference { get; set; }
    public Models.Enums.Availability Availability { get; set; }
    public Models.Enums.SeniorityLevel? SeniorityEstimate { get; set; }
    public List<Models.DTOs.Candidate.CandidateSkillResponse> Skills { get; set; } = new();
    public Dictionary<string, List<string>> Capabilities { get; set; } = new();
    public int RecommendationsCount { get; set; }
    public DateTime LastActiveAt { get; set; }
    public bool IsSaved { get; set; }

    // Shortlist-only fields (null if not in company's shortlist)
    public bool IsInShortlist { get; set; }
    public string? Email { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? CvDownloadUrl { get; set; }
    public string? GitHubUrl { get; set; }
    public string? GitHubSummary { get; set; }
}
