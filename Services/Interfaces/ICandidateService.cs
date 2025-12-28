using bixo_api.Models.DTOs.Candidate;

namespace bixo_api.Services.Interfaces;

public interface ICandidateService
{
    Task<CandidateProfileResponse?> GetProfileAsync(Guid userId);
    Task<CandidateProfileResponse> OnboardAsync(Guid userId, CandidateOnboardRequest request);
    Task<CandidateProfileResponse> UpdateProfileAsync(Guid userId, UpdateCandidateRequest request);
    Task<CvUploadResponse> GetCvUploadUrlAsync(Guid userId, string fileName);
    Task<CvUploadResponse> UploadCvAsync(Guid userId, Stream fileStream, string fileName, string contentType);
    Task ProcessCvUploadAsync(Guid userId, string fileKey, string originalFileName);
    Task UpdateSkillsAsync(Guid userId, UpdateSkillsRequest request);
    Task SetVisibilityAsync(Guid userId, bool visible);

    /// <summary>
    /// Candidate requests a public work summary based on their GitHub profile.
    /// Bixo will prepare it within 2-3 business days.
    /// </summary>
    Task<bool> RequestPublicWorkSummaryAsync(Guid userId);

    /// <summary>
    /// Admin: Re-trigger CV parsing for a candidate. Used when initial parse failed.
    /// </summary>
    Task<CvReparseResult> ReparseCvAsync(Guid candidateId);
}

public class CvReparseResult
{
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    public int SkillsExtracted { get; set; }
}
