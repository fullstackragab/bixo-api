using bixo_api.Models.DTOs.Candidate;

namespace bixo_api.Services.Interfaces;

public interface ICvParsingService
{
    Task<CvParseResult> ParseCvAsync(Stream fileStream, string fileName);
}
