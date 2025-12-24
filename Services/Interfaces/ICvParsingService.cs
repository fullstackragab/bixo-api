using pixo_api.Models.DTOs.Candidate;

namespace pixo_api.Services.Interfaces;

public interface ICvParsingService
{
    Task<CvParseResult> ParseCvAsync(Stream fileStream, string fileName);
}
