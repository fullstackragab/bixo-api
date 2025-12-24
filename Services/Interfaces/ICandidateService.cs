using pixo_api.Models.DTOs.Candidate;

namespace pixo_api.Services.Interfaces;

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
}
